using PlexBot.Utils;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
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
            await visualPlayer.AddOrUpdateVisualPlayerAsync(components, recreateImage: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in NotifyTrackStartedAsync: {ex.Message}");
        }
    }

    /// <inheritdoc />
    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queueItem);

        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        string trackTitle = (queueItem as CustomTrackQueueItem)?.Title ?? queueItem.Track?.Title ?? "Unknown Track";
        Logs.Debug($"Track ended: {trackTitle}, Reason: {endReason}");
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