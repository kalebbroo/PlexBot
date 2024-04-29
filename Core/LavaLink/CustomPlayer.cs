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
        private readonly Players.Players _players;
        private readonly LavaLinkCommands _lavaLinkCommands;

        public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties, Players.Players players, LavaLinkCommands lavaLink)
        : base(properties)
        {
            _textChannel = properties.Options.Value.TextChannel;
            _players = players;
            _lavaLinkCommands = lavaLink;
        }

        protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
        {
            try
            {
                await base.NotifyTrackStartedAsync(track).ConfigureAwait(false);

                // Create a list of dictionaries to store the track information
                List<Dictionary<string, string>> tracks = [];

                // Get queued songs from cache
                List<Dictionary<string, string>> queuedSongs = _lavaLinkCommands.GetQueuedSongs();

                try
                {
                    // Call the BuildAndSendPlayer method and pass the tracks list
                    EmbedBuilder player = await _players.BuildAndSendPlayer(queuedSongs);

                    // Create a ComponentBuilder for the buttons
                    var components = new ComponentBuilder()
                        .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue", "queue:select", ButtonStyle.Primary)
                        .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                    // Send the player embed to the text channel
                    await _textChannel.SendMessageAsync(components: components.Build(), embed: player.Build()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Log the exception or handle it appropriately
                    Console.WriteLine($"An error occurred while building and sending the player: {ex.Message}");
                    // You can also send an error message to the text channel if needed
                    await _textChannel.SendMessageAsync("An error occurred while starting the track.").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it appropriately
                Console.WriteLine($"An error occurred while notifying track started: {ex.Message}");
                // You can also send an error message to the text channel if needed
                await _textChannel.SendMessageAsync("An error occurred while starting the track.").ConfigureAwait(false);
            }
        }

        protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
        {
#warning TODO: Get the queue and match it with the name and then remove the track from the queue
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(queueItem);

            await base
                .NotifyTrackEndedAsync(queueItem, endReason, cancellationToken)
                .ConfigureAwait(false);

            if (endReason.MayStartNext() && AutoPlay)
            {
                //await PlayNextAsync(skipCount: 1, respectTrackRepeat: true, cancellationToken).ConfigureAwait(false);
            }
            else if (endReason is not TrackEndReason.Replaced)
            {
                CurrentItem = null;
            }
            //var track = await GetNextTrackAsync(skipCount, respectTrackRepeat, respectHistory, cancellationToken).ConfigureAwait(false);
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

        public async Task GetQueueInfo()
        {
            // Get the current track
            ITrackQueueItem? currentTrack = Queue.FirstOrDefault(); 
            if (currentTrack != null)
            {
                Console.WriteLine($"Current track: {currentTrack.Track.Title}");
            }

            // Get the total number of tracks in the queue
            int trackCount = Queue.Count;
            Console.WriteLine($"Total tracks in queue: {trackCount}");

            TimeSpan totalDuration = TimeSpan.Zero;
            for ( int i = 0; i < trackCount; i++ )
            {
                ITrackQueueItem track = Queue.ElementAt(i);
                totalDuration += track.Track.Duration;
            }
            Console.WriteLine($"Total duration of queue: {totalDuration}");

            // Iterate through all tracks in the queue
            foreach (ITrackQueueItem track in Queue)
            {
                Console.WriteLine($"Track: {track.Track.Title}");
            }

            // Get a specific track by index
            ITrackQueueItem? trackAtIndex = Queue.ElementAtOrDefault(1);
            if (trackAtIndex != null)
            {
                Console.WriteLine($"Track at index 1: {trackAtIndex.Track.Title}");
            }
        }
    }

    public sealed record class CustomPlayerOptions : QueuedLavalinkPlayerOptions
    {
        public ITextChannel TextChannel { get; init; }

        public CustomPlayerOptions()
        {
            DisconnectOnStop = false;
        }
    }

}
