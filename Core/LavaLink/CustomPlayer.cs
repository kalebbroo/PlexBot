using Lavalink4NET.Players.Queued;
using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Lavalink4NET.Tracks;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Lavalink4NET.Extensions;
using Lavalink4NET.Clients.Events;
using Lavalink4NET.Rest;
using Lavalink4NET.Integrations;
using PlexBot.Core.Players;
using PlexBot.Core.LavaLink;
using Microsoft.Extensions.Caching.Memory;

namespace PlexBot.Core.LavaLink
{
    public sealed class CustomPlayer : QueuedLavalinkPlayer
    {
        private readonly ITextChannel _textChannel;
        private readonly LavaLinkCommands _lavaLinkCommands;

        public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties, LavaLinkCommands lavaLink)
        : base(properties)
        {
            _textChannel = properties.Options.Value.TextChannel;
            _lavaLinkCommands = lavaLink;
        }

        protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
        {
            try
            {
                await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);

                if (track is CustomTrackQueueItem customTrack)
                {
                    // Get the queue information as a list of dictionaries
                    List<Dictionary<string, string>> queueInfo = await GetQueueInfo().ConfigureAwait(false);
                    // Build the new player embed using the custom track information
                    EmbedBuilder player = Players.Players.BuildAndSendPlayer(queueInfo);

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

            if (endReason.MayStartNext() && AutoPlay)
            {
                // Retrieve the current queue
                List<Dictionary<string, string>> queuedSongs = _lavaLinkCommands.GetQueuedSongs();
                if (queuedSongs.Any())
                {
                    // Check if the first track in the queue matches the track that ended
                    Dictionary<string, string> currentTrack = queuedSongs.First();
                    if (currentTrack["Title"].Equals(trackTitle))
                    {
                        // Remove the track from the cache if it matches
                        _lavaLinkCommands.RemoveTrackFromCache(currentTrack["Url"]);
                        Console.WriteLine($"Removed track: {trackTitle}");
                    }
                    else
                    {
                        // Log error if the track titles do not match
                        Console.WriteLine($"Error: The track in the queue ('{currentTrack["Title"]}') does not match the ended track ('{trackTitle}').");
                    }
                }
            }
            else if (endReason is not TrackEndReason.Replaced)
            {
                CurrentItem = null;
            }
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

        public Task<List<Dictionary<string, string>>> GetQueueInfo()
        {
            List<Dictionary<string, string>> queueInfo = new();

            // Iterate through all tracks in the queue
            foreach (ITrackQueueItem track in Queue)
            {
                if (track is CustomTrackQueueItem customTrack)
                {
                    // Create a dictionary for each custom track
                    Dictionary<string, string> trackInfo = new()
                    {
                        ["Title"] = customTrack.Title ?? "Unknown Title",
                        ["Duration"] = customTrack.Duration ?? "00:00",
                        ["Artist"] = customTrack.Artist ?? "Unknown Artist",
                        ["Album"] = customTrack.Album ?? "Unknown Album",
                        ["Studio"] = customTrack.Studio ?? "Unknown Studio",
                        ["Artwork"] = customTrack.Artwork ?? "https://via.placeholder.com/150",
                        ["Url"] = customTrack.Url ?? string.Empty
                    };
                    // Add the dictionary to the list
                    queueInfo.Add(trackInfo);
                    Console.WriteLine($"Track: {trackInfo["Title"]}, Artist: {trackInfo["Artist"]}, Duration: {trackInfo["Duration"]}"); // debug
                }
            }
            // Return the list of dictionaries encapsulated in a completed task
            return Task.FromResult(queueInfo);
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

    public class CustomTrackQueueItem(TrackReference reference) : ITrackQueueItem
    {
        public TrackReference Reference { get; } = reference;
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Artwork { get; set; }
        public string? Url { get; set; }
        public string? ArtistUrl { get; set; }
        public string? Duration { get; set; }
        public string? Studio { get; set; }

        public T? As<T>() where T : class, ITrackQueueItem => this as T;
    }
}
