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

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
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

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackExceptionAsync(ITrackQueueItem track, TrackException exception, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackExceptionAsync(track, exception, cancellationToken).ConfigureAwait(false);
        string where = track is CustomTrackQueueItem item ? $"{item.Title} ({TrackResolverService.PartId(item.SourceTrack)})" : track.Track?.Title ?? "Unknown Track";
        Logs.Warning($"[guild {GuildId}] Playback failed: {where}: {exception.Severity}: {exception.Message} ({exception.Cause})");
    }

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queueItem);

        string trackTitle = (queueItem as CustomTrackQueueItem)?.Title ?? queueItem.Track?.Title ?? "Unknown Track";

        // A Plex stream that fails to open at play time is usually the same dropped response seen at load time.
        // Retry it once after the backoff, rather than skipping straight to the next track.
        if (endReason == TrackEndReason.LoadFailed && queueItem is CustomTrackQueueItem failed
            && failed.SourceTrack.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase))
        {
            if (failed.PlayRetries < 1)
            {
                // The track hasn't really ended: it's about to be played again, so neither the base class (which
                // would move to the next item) nor the TrackEnded event for extensions runs here
                failed.PlayRetries++;
                ScheduleReplay(failed);
                return;
            }
            _ = NotifyChannelAsync("Track Skipped", $"Skipped **{trackTitle}**: Plex didn't return the file.");
        }

        // The base class moves to the next item. If that is a placeholder still loading, give it a few seconds so the
        // player gets a checked track; after that, Lavalink loads the URL itself (with the play-time retry above).
        if (endReason.MayStartNext() && AutoPlay)
            await serviceProvider.GetRequiredService<QueueResolveService>().EnsureHeadResolvedBrieflyAsync(this).ConfigureAwait(false);

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

    /// <summary>Plays a failed item again after the Plex backoff. Runs off the event path so other player events
    /// aren't held up. Dropped if the queue was cleared, stopped, or replaced meanwhile.</summary>
    private void ScheduleReplay(CustomTrackQueueItem item)
    {
        PlexStreamGate gate = serviceProvider.GetRequiredService<PlexStreamGate>();
        TimeSpan delay = serviceProvider.GetRequiredService<PlexLoadRetryPolicy>().DelayBefore(1);
        long generation = PlayerService.CurrentGeneration(GuildId);
        gate.Cooldown(delay);
        Logs.Warning($"[guild {GuildId}] Retrying playback in {delay.TotalSeconds:N0}s: {item.Title} ({TrackResolverService.PartId(item.SourceTrack)})");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (State == PlayerState.Destroyed || PlayerService.CurrentGeneration(GuildId) != generation)
                {
                    Logs.Debug($"[guild {GuildId}] Playback retry dropped: the queue changed");
                    return;
                }
                // A placeholder that Lavalink failed to load directly goes through the resolver's retries instead
                if (!item.IsResolved)
                {
                    LavalinkTrack? resolved = await serviceProvider.GetRequiredService<ITrackResolverService>()
                        .ResolveTrackAsync(item.SourceTrack).ConfigureAwait(false);
                    if (resolved is null)
                    {
                        _ = NotifyChannelAsync("Track Skipped", $"Skipped **{item.Title}**: Plex didn't return the file.");
                        if (State == PlayerState.NotPlaying && PlayerService.CurrentGeneration(GuildId) == generation)
                            await SkipAsync().ConfigureAwait(false);
                        return;
                    }
                    item.Reference = new TrackReference(resolved);
                }
                if (State == PlayerState.NotPlaying)
                    await PlayAsync(item, enqueue: false).ConfigureAwait(false);
                else
                    await Queue.InsertAsync(0, item).ConfigureAwait(false); // something else started meanwhile
            }
            catch (Exception ex)
            {
                Logs.Error($"[guild {GuildId}] Playback retry failed: {ex.Message}");
            }
        });
    }

    /// <summary>Posts a short notice in the player's channel, deleted after 30 seconds</summary>
    internal async Task NotifyChannelAsync(string title, string description)
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