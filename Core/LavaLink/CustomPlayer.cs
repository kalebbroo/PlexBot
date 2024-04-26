using Lavalink4NET.Players.Queued;
using Discord;
using Lavalink4NET.Tracks;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Players;
using Lavalink4NET.Extensions;

namespace PlexBot.Core.LavaLink
{
    public sealed class CustomPlayer : QueuedLavalinkPlayer
    {
        private readonly ITextChannel _textChannel;

        public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties)
            : base(properties)
        {
            _textChannel = properties.Options.Value.TextChannel;
        }

        protected override async ValueTask OnTrackStartedAsync(ITrackQueueItem eventArgs)
        {
            await base.OnTrackStartedAsync(eventArgs).ConfigureAwait(false);

            // Send a message to the text channel
            await _textChannel.SendMessageAsync($"Now playing: {eventArgs.Track.Title}").ConfigureAwait(false);
        }

        protected override async ValueTask OnTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
        {
            await base
                .OnTrackStartedAsync(track, cancellationToken)
                .ConfigureAwait(false);

            // send a message to the text channel
            await _textChannel
                .SendMessageAsync($"Now playing: {track.Title}")
                .ConfigureAwait(false);
        }

        protected override void OnPlayersChanged(PlayerChangedEventArgs eventArgs)
        {
            if (eventArgs.NewPlayers.Count == 0)
            {
                // Handle the event when no users are in the voice channel
                // Replace with your desired logic
                _ = Task.Run(async () =>
                {
                    await Task.CompletedTask;
                });
            }
        }
        protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(queueItem);

            await base
                .NotifyTrackEndedAsync(queueItem, endReason, cancellationToken)
                .ConfigureAwait(false);

            if (endReason.MayStartNext() && AutoPlay)
            {
                await PlayNextAsync(skipCount: 1, respectTrackRepeat: true, cancellationToken).ConfigureAwait(false);
            }
            else if (endReason is not TrackEndReason.Replaced)
            {
                CurrentItem = null;
            }
            var track = await GetNextTrackAsync(skipCount, respectTrackRepeat, respectHistory, cancellationToken).ConfigureAwait(false);
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
