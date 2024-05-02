using Lavalink4NET.Players.Queued;
using Discord;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Players;

namespace PlexBot.Core.LavaLink
{
    public sealed class CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties, LavaLinkCommands lavaLink) : QueuedLavalinkPlayer(properties)
    {
        private readonly ITextChannel? _textChannel = properties.Options.Value.TextChannel;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;

        protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
        {
            try
            {
                await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);

                if (track is CustomTrackQueueItem customTrack)
                {
                    Dictionary<string, string> customTracks = new()
                    {
                        ["Title"] = customTrack.Title ?? "Unknown Title",
                        ["Artist"] = customTrack.Artist ?? "Unknown Artist",
                        ["Album"] = customTrack.Album ?? "Unknown Album",
                        ["Duration"] = customTrack.Duration ?? "00:00",
                        ["Url"] = customTrack.Url ?? "N/A",
                        // Add more custom track information
                    };
                    // Build the new player embed using the custom track information
                    EmbedBuilder player = Players.Players.BuildAndSendPlayer(customTracks);

                    // Create a ComponentBuilder for the buttons
                    ComponentBuilder components = new ComponentBuilder()
                        .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue", "queue:select", ButtonStyle.Primary)
                        .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                    // Find and delete the last player message (if it exists)
                    var messages = await _textChannel.GetMessagesAsync(10).FlattenAsync().ConfigureAwait(false);
                    IMessage? lastPlayerMessage = messages.FirstOrDefault(m => m.Embeds.Any(e => e.Title == "Now Playing"));
                    if (lastPlayerMessage != null)
                    {
                        await lastPlayerMessage.DeleteAsync().ConfigureAwait(false);
                        Console.WriteLine("Deleted last player message.");
                    }

                    // Send the new player embed to the text channel
                    await _textChannel.SendMessageAsync(components: components.Build(), embed: player.Build()).ConfigureAwait(false);
                }
                else
                {
                    Console.WriteLine("Error: Track is not a CustomTrackQueueItem.");
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it appropriately
                Console.WriteLine($"An error occurred while notifying track started: {ex.Message}");
                await _textChannel.SendMessageAsync("An error occurred while starting the track.").ConfigureAwait(false);
            }
        }

        protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(queueItem);

            await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

            string trackTitle = queueItem.Track?.Title ?? "Default Title";

            Console.WriteLine($"Track ended: {trackTitle}");
        }

        public ValueTask NotifyPlayerActiveAsync(CancellationToken cancellationToken = default)
        {
            // This method is called when the player was previously inactive and is now active again.
            // For example: All users in the voice channel left and now a user joined the voice channel again.
            cancellationToken.ThrowIfCancellationRequested();
            return default; // do nothing
        }

        public async ValueTask NotifyPlayerInactiveAsync(CancellationToken cancellationToken = default)
        {
            // This method is called when the player reached the inactivity deadline.
            // For example: All users in the voice channel left and the player was inactive for longer than 30 seconds.
            cancellationToken.ThrowIfCancellationRequested();

            // Add your custom logic here to handle the event when the player becomes inactive
            // For example, you can stop the player and send a message:
            await StopAsync(cancellationToken).ConfigureAwait(false);
            await _textChannel.SendMessageAsync("The player has been stopped due to inactivity.").ConfigureAwait(false);
        }

        public ValueTask NotifyPlayerTrackedAsync(CancellationToken cancellationToken = default)
        {
            // This method is called when the player was previously active and is now inactive.
            // For example: A user left the voice channel and now all users left the voice channel.
            cancellationToken.ThrowIfCancellationRequested();
            return default; // do nothing
        }
    }

    public sealed record class CustomPlayerOptions : QueuedLavalinkPlayerOptions
    {
        public ITextChannel? TextChannel { get; init; }

        public CustomPlayerOptions()
        {
            DisconnectOnStop = false;
        }
    }
}
