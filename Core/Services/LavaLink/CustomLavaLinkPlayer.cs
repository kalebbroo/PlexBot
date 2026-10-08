using PlexBot.Utils;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Events;
using PlexBot.Core.Models.Players;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Enhanced Lavalink player implementation that integrates with Discord to provide rich visual UI, track metadata, and interactive controls</summary>
/// <remarks>Constructs the player with specified properties to enable audio playback with enhanced Discord integration for visual feedback</remarks>
/// <param name="properties">Configuration container with player settings, options, and channel information</param>
public sealed class CustomLavaLinkPlayer(IPlayerProperties<CustomLavaLinkPlayer, CustomPlayerOptions> properties,
    IServiceProvider serviceProvider) : QueuedLavalinkPlayer(properties), IInactivityPlayerListener
{

    /// <summary>Counts track starts and ends. Work deferred off the event path (a replay, or moving to the next track)
    /// records it and is dropped if any track has started or ended since, which covers a user who plays and skips
    /// something else meanwhile. A field because it is updated with Interlocked.</summary>
    private long _playbackEpoch;

    /// <summary>The current value of the start/end counter, read from background tasks</summary>
    public long PlaybackEpoch => Interlocked.Read(ref _playbackEpoch);

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _playbackEpoch);
        try
        {
            VisualPlayer visualPlayer = serviceProvider.GetRequiredService<VisualPlayer>();
            DiscordButtonBuilder buttonBuilder = serviceProvider.GetRequiredService<DiscordButtonBuilder>();
            await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);
            if (track is not CustomTrackQueueItem customTrack)
            {
                Logs.Error("Track is not a CustomTrackQueueItem");
                return;
            }
            ButtonContext context = new() { Player = this };
            ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
            await visualPlayer.AddOrUpdateVisualPlayerAsync(GuildId, components, recreateImage: true).ConfigureAwait(false);

            // The queue moved on, so the resolve-ahead window did too
            serviceProvider.GetRequiredService<QueueResolveService>().Wake(GuildId);

            // Prefetch next track's artwork in background (fire and forget)
            ITrackPrefetchService prefetch = serviceProvider.GetRequiredService<ITrackPrefetchService>();
            _ = prefetch.PrefetchNextAsync(this, cancellationToken);

            // Publish track started event for extensions
            BotEventBus eventBus = serviceProvider.GetRequiredService<BotEventBus>();
            _ = eventBus.PublishAsync(new BotEvent
            {
                EventType = BotEvents.TrackStarted,
                Data = new Dictionary<string, object>
                {
                    ["title"] = customTrack.Title ?? "Unknown",
                    ["artist"] = customTrack.Artist ?? "Unknown",
                    ["guildId"] = GuildId
                }
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in NotifyTrackStartedAsync: {ex.Message}");
        }
    }

    /// <summary>Times a Plex track is played again after its stream fails to open, before it is skipped</summary>
    public const int MaxPlayRetries = 1;

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackExceptionAsync(ITrackQueueItem track, TrackException exception, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackExceptionAsync(track, exception, cancellationToken).ConfigureAwait(false);
        string where = track is CustomTrackQueueItem item ? PlexUrlHelper.Describe(item.SourceTrack) : track.Track?.Title ?? "Unknown Track";
        Logs.Warning($"[guild {GuildId}] Playback failed: {where}: {exception.Severity}: {exception.Message} ({exception.Cause})");
    }

    /// <inheritdoc />
    /// <remarks>Lavalink4NET calls this from the node's receive loop, which every guild shares, so nothing here may
    /// wait on Plex. Work that has to wait (a replay after a failed stream, or resolving the next placeholder) is
    /// handed to a background task.</remarks>
    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queueItem);
        Interlocked.Increment(ref _playbackEpoch);

        string trackTitle = (queueItem as CustomTrackQueueItem)?.Title ?? queueItem.Track?.Title ?? "Unknown Track";
        CustomTrackQueueItem? endedItem = queueItem as CustomTrackQueueItem;

        // A Plex stream that fails to open at play time is usually the same dropped response seen at load time.
        // Retry it after the backoff, rather than skipping straight to the next track.
        if (endReason == TrackEndReason.LoadFailed && endedItem is not null && endedItem.SourceTrack.IsPlex)
        {
            if (endedItem.PlayRetries < MaxPlayRetries)
            {
                // The track hasn't really ended: it's about to be played again, so neither the base class (which
                // would move to the next item) nor the TrackEnded event for extensions runs here
                endedItem.PlayRetries++;
                ScheduleReplay(endedItem);
                return;
            }
            _ = NotifyChannelAsync("Track Skipped", $"Skipped **{trackTitle}**: Plex didn't return the file.");
        }
        if (endReason == TrackEndReason.Finished && endedItem is not null)
            endedItem.PlayRetries = 0;

        // The base class plays the next item. If that is a placeholder, it is resolved first, off this thread.
        if (endReason.MayStartNext() && AutoPlay && NextItemNeedsResolve())
            AdvanceWhenResolved(queueItem, endReason);
        else
            await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);
        Logs.Debug($"Track ended: {trackTitle}, Reason: {endReason}");

        // Publish track ended event for extensions
        try
        {
            BotEventBus eventBus = serviceProvider.GetRequiredService<BotEventBus>();
            _ = eventBus.PublishAsync(new BotEvent
            {
                EventType = BotEvents.TrackEnded,
                Data = new Dictionary<string, object>
                {
                    ["title"] = trackTitle,
                    ["guildId"] = GuildId,
                    ["endReason"] = endReason.ToString()
                }
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"Error publishing track ended event: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>Resolves the item the skip lands on first, so Lavalink never loads a placeholder's URL itself,
    /// outside the Plex gate and its retries. Items that fail for good are removed while waiting.</remarks>
    public override async ValueTask SkipAsync(int count = 1, CancellationToken cancellationToken = default)
    {
        ITrackQueueItem? skipping = CurrentItem;
        await serviceProvider.GetRequiredService<QueueResolveService>()
            .EnsureResolvedAtAsync(this, Math.Max(0, count - 1), cancellationToken).ConfigureAwait(false);

        // The track may have ended on its own while the next one loaded, and the player already moved on
        if (!ReferenceEquals(CurrentItem, skipping))
        {
            Logs.Debug($"[guild {GuildId}] Skip dropped: the player moved to the next track while it loaded");
            return;
        }
        await base.SkipAsync(count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when the base class would play a queued placeholder next. With track repeat, the ended item
    /// plays again, so nothing needs resolving.</summary>
    public bool NextItemNeedsResolve() =>
        RepeatMode != TrackRepeatMode.Track
        && Queue.TryPeek(out ITrackQueueItem? next)
        && next is CustomTrackQueueItem item
        && !item.IsResolved;

    /// <summary>Resolves the next item in the background, then lets the base class move to it. Dropped if the user
    /// starts something else, or stops, clears or replaces the queue, while it loads.</summary>
    public void AdvanceWhenResolved(ITrackQueueItem endedItem, TrackEndReason endReason)
    {
        QueueResolveService queueResolver = serviceProvider.GetRequiredService<QueueResolveService>();
        CancellationToken stopped = queueResolver.GetWorker(GuildId).Token;
        long generation = PlayerService.CurrentGeneration(GuildId);
        long epoch = PlaybackEpoch;

        _ = Task.Run(async () =>
        {
            try
            {
                await queueResolver.EnsureResolvedAtAsync(this).ConfigureAwait(false);
                if (stopped.IsCancellationRequested || !IsUnchangedSince(generation, epoch))
                {
                    Logs.Debug($"[guild {GuildId}] Moving to the next track dropped: the player or queue changed while it loaded");
                    return;
                }
                await base.NotifyTrackEndedAsync(endedItem, endReason).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Error($"[guild {GuildId}] Could not move to the next track: {ex.Message}");
            }
        });
    }

    /// <summary>Plays a failed item again after the Plex backoff. Runs off the event path so other player events
    /// aren't held up. Dropped if the user skipped or started something else, or stopped, cleared or replaced the
    /// queue, meanwhile.</summary>
    public void ScheduleReplay(CustomTrackQueueItem item)
    {
        TimeSpan delay = serviceProvider.GetRequiredService<PlexLoadRetryPolicy>().DelayBefore(1);
        long generation = PlayerService.CurrentGeneration(GuildId);
        long epoch = PlaybackEpoch;
        serviceProvider.GetRequiredService<PlexStreamGate>().Cooldown(delay);
        Logs.Warning($"[guild {GuildId}] Retrying playback in {delay.TotalSeconds:N0}s: {PlexUrlHelper.Describe(item.SourceTrack)}");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (!IsUnchangedSince(generation, epoch))
                {
                    Logs.Debug($"[guild {GuildId}] Playback retry dropped: the player or queue changed");
                    return;
                }
                await PlayAsync(item, enqueue: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Error($"[guild {GuildId}] Playback retry failed: {ex.Message}");
            }
        });
    }

    /// <summary>True when the player is still idle, no track has started or ended, and the queue has not been
    /// stopped, cleared or replaced since the given generation and epoch were recorded</summary>
    public bool IsUnchangedSince(long generation, long epoch) =>
        State == PlayerState.NotPlaying
        && PlaybackEpoch == epoch
        && PlayerService.CurrentGeneration(GuildId) == generation;

    /// <summary>Posts a short notice in the player's channel, deleted after 30 seconds</summary>
    public async Task NotifyChannelAsync(string title, string description)
    {
        try
        {
            ITextChannel? channel = serviceProvider.GetRequiredService<VisualPlayerStateManager>().GetChannel(GuildId);
            if (channel is null) return;
            IUserMessage message = await channel.SendMessageAsync(components: Discord.Embeds.ComponentV2Builder.Info(title, description),
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                try { await message.DeleteAsync().ConfigureAwait(false); }
                catch (Exception ex) { Logs.Debug($"[guild {GuildId}] Could not delete channel notice: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            Logs.Debug($"[guild {GuildId}] Could not post channel notice: {ex.Message}");
        }
    }

    /// <summary>Called by Lavalink4NET inactivity tracking when the player becomes active again (users rejoin voice)</summary>
    public ValueTask NotifyPlayerActiveAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player active event for guild {GuildId}");
        return default;
    }

    /// <summary>Called by Lavalink4NET inactivity tracking when the inactivity timeout is reached, stopping playback and disconnecting</summary>
    public async ValueTask NotifyPlayerInactiveAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Info($"Player inactive timeout reached for guild {GuildId}, disconnecting...");

        try
        {
            serviceProvider.GetRequiredService<QueueResolveService>().Stop(GuildId);
            await StopAsync(cancellationToken).ConfigureAwait(false);
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling player inactivity: {ex.Message}");
        }
    }

    /// <summary>Called by Lavalink4NET inactivity tracking when player tracking state changes</summary>
    public ValueTask NotifyPlayerTrackedAsync(PlayerTrackingState trackingState, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player tracked state change for guild {GuildId}: {trackingState.Status}");
        return default;
    }
}

/// <summary>Custom options for the CustomPlayer class, extending the standard QueuedLavalinkPlayerOptions with additional configuration</summary>
/// <param name="TextChannel">Gets or sets the Discord text channel where player messages will be sent, used for displaying the visual player and notifications</param>
public sealed record CustomPlayerOptions(ITextChannel? TextChannel) : QueuedLavalinkPlayerOptions
{
    /// <summary>Gets or sets the default volume level, ranging from 0.0 to 1.0</summary>
    public float DefaultVolume { get; init; } = 0.2f;

    /// <summary>Gets or sets whether to show track thumbnails in player messages, used for visual feedback</summary>
    public bool ShowThumbnails { get; init; } = true;

    /// <summary>Gets or sets whether to delete player messages when they become outdated, used for cleanup and organization</summary>
    public bool DeleteOutdatedMessages { get; init; } = true;

    /// <summary>Initializes a new instance of the CustomPlayerOptions class, setting default values for LavaLink player configuration</summary>
    public CustomPlayerOptions() : this((ITextChannel?)null)
    {
        // Set LavaLink player defaults
        DisconnectOnStop = false;
        SelfDeaf = true;

        // Other defaults are set through auto-properties
    }
}