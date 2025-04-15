using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Main;
using PlexBot.Core.Models.Players;
using Microsoft.VisualBasic;
using System;

namespace PlexBot.Services.LavaLink;

/// <summary>Enhanced Lavalink player implementation that integrates with Discord to provide rich visual UI, track metadata, and interactive controls</summary>
/// <remarks>Constructs the player with specified properties to enable audio playback with enhanced Discord integration for visual feedback</remarks>
/// <param name="properties">Configuration container with player settings, options, and channel information</param>
public sealed class CustomLavaLinkPlayer(IPlayerProperties<CustomLavaLinkPlayer, CustomPlayerOptions> properties,
    IServiceProvider serviceProvider) : QueuedLavalinkPlayer(properties)
{

    /// <summary>Handles the track start event by building and sending a rich visual player interface with artwork and interactive controls</summary>
    /// <param name="track">The track that started playing, containing metadata for display</param>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of creating and sending the player UI</returns>
    /// <summary>Handles the track start event by building and sending a rich visual player interface</summary>
    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        try
        {
            VisualPlayer visualPlayer = serviceProvider.GetRequiredService<VisualPlayer>();
            await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);
            if (track is not CustomTrackQueueItem customTrack)
            {
                Logs.Error("Track is not a CustomTrackQueueItem");
                return;
            }
            ButtonContext context = new() { Player = this };
            ComponentBuilder components = DiscordButtonBuilder.Instance.BuildButtons(ButtonFlag.VisualPlayer, context);
            await visualPlayer.AddOrUpdateVisualPlayerAsync(components, recreateImage: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in NotifyTrackStartedAsync: {ex.Message}");
        }
    }

    /// <summary>Handles the track end event by logging track completion and performing cleanup</summary>
    /// <param name="queueItem">The track that ended, containing metadata for logging</param>
    /// <param name="endReason">The reason the track ended, used for logging and debugging</param>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of logging track completion</returns>
    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queueItem);

        // Call base implementation first
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        // Log track end
        string trackTitle = (queueItem as CustomTrackQueueItem)?.Title ?? queueItem.Track?.Title ?? "Unknown Track";
        Logs.Debug($"Track ended: {trackTitle}, Reason: {endReason}");
    }

    /// <summary>Handles the player active event when users join the voice channel after all users had left</summary>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of handling player activation</returns>
    public ValueTask NotifyPlayerActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player active event for guild {GuildId}");
        return default; // No special handling needed
    }

    /// <summary>Handles the player inactive event due to inactivity timeout, stopping playback and disconnecting from the voice channel</summary>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of handling player inactivity</returns>
    public async ValueTask NotifyPlayerInactiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Info($"Player inactive timeout reached for guild {GuildId}");

        try
        {
            // Stop playback
            await StopAsync(cancellationToken).ConfigureAwait(false);

            // Disconnect from voice channel
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling player inactivity: {ex.Message}");
        }
    }

    /// <summary>Handles the player tracked state change event, used to update the visual player interface</summary>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of handling player state changes</returns>
    public ValueTask NotifyPlayerTrackedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player tracked state change for guild {GuildId}");
        return default; // No special handling needed
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

    public TimeSpan InactivityTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>Initializes a new instance of the CustomPlayerOptions class, setting default values for LavaLink player configuration</summary>
    public CustomPlayerOptions() : this((ITextChannel?)null)
    {
        // Set LavaLink player defaults
        DisconnectOnStop = false;
        SelfDeaf = true;

        // Other defaults are set through auto-properties
    }
}