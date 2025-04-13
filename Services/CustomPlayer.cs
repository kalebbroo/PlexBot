using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;

namespace PlexBot.Services;

/// <summary>Enhanced Lavalink player implementation that integrates with Discord to provide rich visual UI, track metadata, and interactive controls</summary>
public sealed class CustomPlayer : QueuedLavalinkPlayer
{
    private readonly ITextChannel? _textChannel;
    private IUserMessage? _currentPlayerMessage;
    private readonly bool _useVisualPlayer;
    private readonly bool _useStaticChannel;
    private readonly ulong? _staticChannelId;

    /// <summary>Constructs the player with specified properties to enable audio playback with enhanced Discord integration for visual feedback</summary>
    /// <param name="properties">Configuration container with player settings, options, and channel information</param>
    public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties) : base(properties)
    {
        _textChannel = properties.Options.Value.TextChannel;
        _useVisualPlayer = EnvConfig.GetBool("PLAYER_STYLE_VISUAL", true); // Default to visual player
        _useStaticChannel = EnvConfig.GetBool("USE_STATIC_PLAYER_CHANNEL", false);
        string staticChannelIdStr = EnvConfig.Get("STATIC_PLAYER_CHANNEL_ID", "");
        if (_useStaticChannel && !string.IsNullOrEmpty(staticChannelIdStr) && ulong.TryParse(staticChannelIdStr, out ulong channelId))
        {
            _staticChannelId = channelId;
            Logs.Debug($"Static player channel ID configured: {channelId}");
        }
        // Log initialization
        Logs.Debug($"CustomPlayer initialized for guild {GuildId} in channel {_textChannel?.Id}, Visual Player: {_useVisualPlayer}, Static Channel: {_useStaticChannel}");
    }

    /// <summary>Handles the track start event by building and sending a rich visual player interface with artwork and interactive controls</summary>
    /// <param name="track">The track that started playing, containing metadata for display</param>
    /// <param name="cancellationToken">Token to cancel the operation in case of shutdown or timeout</param>
    /// <returns>A task representing the asynchronous operation of creating and sending the player UI</returns>
    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        try
        {
            // Call base implementation first
            await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);
            // Get the actual text channel to use (either regular or static channel)
            ITextChannel? targetChannel = await GetTargetTextChannelAsync();
            if (targetChannel == null)
            {
                Logs.Warning("Cannot display player: No text channel specified");
                return;
            }
            // Get the track information
            CustomTrackQueueItem customTrack = track as CustomTrackQueueItem
                ?? throw new InvalidOperationException("Track is not a CustomTrackQueueItem");
            // Create track information dictionary for the visual player
            Dictionary<string, string> trackInfo = new()
            {
                ["Title"] = customTrack.Title ?? "Unknown Title",
                ["Artist"] = customTrack.Artist ?? "Unknown Artist",
                ["Album"] = customTrack.Album ?? "Unknown Album",
                ["Duration"] = customTrack.Duration ?? "00:00",
                ["Url"] = customTrack.Url ?? "N/A",
                ["ArtistUrl"] = customTrack.ArtistUrl ?? "N/A",
                ["ReleaseDate"] = customTrack.ReleaseDate ?? "N/A",
                ["Artwork"] = customTrack.Artwork ?? "https://via.placeholder.com/150", // TODO: Replace this with an actual placeholder
                ["Studio"] = customTrack.Studio ?? "Unknown Studio" 
            };
            foreach (var item in trackInfo) // Log track information for debugging
            {
                Logs.Debug($"{item.Key}: {item.Value}");
            }
            try
            {
                // Find and delete the previous player message if it exists
                if (_currentPlayerMessage != null && !_useStaticChannel)
                {
                    try
                    {
                        await _currentPlayerMessage.DeleteAsync().ConfigureAwait(false);
                        Logs.Debug("Deleted previous player message");
                    }
                    catch (Exception ex)
                    {
                        Logs.Warning($"Failed to delete previous player message: {ex.Message}");
                    }
                    _currentPlayerMessage = null;
                }
                ButtonContext context = new() { Player = this };
                ComponentBuilder components = DiscordButtonBuilder.Instance.BuildButtons(ButtonFlag.VisualPlayer, context);
                // Send player message based on style preference
                if (_useVisualPlayer)
                {
                    // Visual player style - send as image attachment with buttons below
                    // Build the player image
                    Logs.Debug("Building player image");
                    using MemoryStream memoryStream = new();
                    SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(trackInfo, this);
                    await image.SaveAsync(memoryStream, new PngEncoder());
                    memoryStream.Position = 0;
                    // Create the Discord file attachment
                    FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                    // Send the new player message with image only (no embed) and buttons
                    if (_currentPlayerMessage == null)
                    {
                        _currentPlayerMessage = await targetChannel.SendFileAsync(fileAttachment, components: components.Build()).ConfigureAwait(false);
                    }
                    // If the message already exists, modify it instead of sending a new one
                    else
                    {
                        await _currentPlayerMessage.ModifyAsync(msg =>
                        {
                            msg.Content = "Now Playing:";
                            msg.Attachments = new List<FileAttachment> { fileAttachment };
                            msg.Components = components.Build();
                        }).ConfigureAwait(false);
                    }
                    Logs.Debug("Visual player message sent successfully");
                }
                else
                {
                    // Classic embed style - send with embed and thumbnail
                    // Build the player embed
                    EmbedBuilder embed = DiscordEmbedBuilder.BuildPlayerEmbed(trackInfo, trackInfo["Artwork"]);
                    // Send the new player message with embed and buttons
                    _currentPlayerMessage = await targetChannel.SendMessageAsync(
                        embed: embed.Build(),
                        components: components.Build()).ConfigureAwait(false);
                    Logs.Debug("Classic embed player message sent successfully");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error building/sending player image: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in NotifyTrackStartedAsync: {ex.Message}");
        }
    }

    /// <summary>Determines the appropriate text channel to use for player messages based on static channel configuration</summary>
    /// <returns>The target text channel, or null if no valid channel is available</returns>
    private async Task<ITextChannel?> GetTargetTextChannelAsync()
    {
        // If static channel is not enabled, use the default channel
        if (!_useStaticChannel || !_staticChannelId.HasValue)
        {
            return _textChannel;
        }
        try
        {
            // Get the guild from the cached guild of the default channel
            IGuild? guild = _textChannel?.Guild;
            if (guild == null)
            {
                Logs.Warning("Cannot get static channel: Guild is not available");
                return _textChannel; // Fall back to default channel
            }
            // Get the static channel from the guild
            ITextChannel? staticChannel = await guild.GetTextChannelAsync(_staticChannelId.Value);
            if (staticChannel == null)
            {
                Logs.Warning($"Static channel with ID {_staticChannelId.Value} not found, falling back to default channel");
                return _textChannel;
            }
            return staticChannel;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting static channel: {ex.Message}");
            return _textChannel; // Fall back to default channel
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

            // Notify users if possible
            if (_textChannel != null)
            {
                await _textChannel.SendMessageAsync("Disconnected due to inactivity.").ConfigureAwait(false);
            }
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

    ///<summary>Updates the visual player with new button components, optionally recreating the full player image</summary>
    /// <param name="components">The new components to display</param>
    /// <param name="recreateImage">Whether to fully recreate the player image (for volume/repeat changes)</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task UpdateVisualPlayerAsync(ComponentBuilder components, bool recreateImage = false)
    {
        if (_currentPlayerMessage == null)
        {
            return;
        }
        try
        {
            // If we just need to update buttons, use the simple approach
            if (!recreateImage)
            {
                await _currentPlayerMessage.ModifyAsync(msg =>
                {
                    msg.Components = components.Build();
                }).ConfigureAwait(false);
                Logs.Debug("Updated player buttons successfully");
                return;
            }
            // For visual changes, use the existing track to fully recreate the player
            if (CurrentItem is CustomTrackQueueItem currentTrack)
            {
                // Delete the existing message
                try
                {
                    await _currentPlayerMessage.DeleteAsync().ConfigureAwait(false);
                    _currentPlayerMessage = null;
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to delete current player message: {ex.Message}");
                    // Continue anyway - we'll create a new one
                }
                // Use the existing method to create a fresh player with current state
                await NotifyTrackStartedAsync(currentTrack).ConfigureAwait(false);
                Logs.Debug("Recreated player message with updated visual state");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to update visual player: {ex.Message}");
        }
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