using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;

namespace PlexBot.Services;

/// <summary>Enhanced Lavalink player implementation that integrates with Discord to provide rich visual UI, track metadata, and interactive controls</summary>
public sealed class CustomPlayer : QueuedLavalinkPlayer
{
    private readonly ITextChannel? _textChannel;
    private IUserMessage? _currentPlayerMessage;

    /// <summary>Constructs the player with specified properties to enable audio playback with enhanced Discord integration for visual feedback</summary>
    /// <param name="properties">Configuration container with player settings, options, and channel information</param>
    public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties)
        : base(properties)
    {
        _textChannel = properties.Options.Value.TextChannel;

        // Log initialization
        Logs.Debug($"CustomPlayer initialized for guild {GuildId} in channel {_textChannel?.Id}");
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

            // Ensure we have a text channel to send messages to
            if (_textChannel == null)
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
                ["Artwork"] = customTrack.Artwork ?? "https://via.placeholder.com/150",
                ["Studio"] = customTrack.Studio ?? "Unknown Studio"
            };

            Logs.Debug($"Now playing: {trackInfo["Title"]} by {trackInfo["Artist"]}");

            try
            {
                // Build the player image
                Logs.Debug("Building player image");
                using var memoryStream = new MemoryStream();
                SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(trackInfo);
                await image.SaveAsync(memoryStream, new PngEncoder());
                memoryStream.Position = 0;

                // Create the Discord file attachment
                var fileAttachment = new FileAttachment(memoryStream, "playerImage.png");
                string fileName = "playerImage.png";

                // Build the player embed
                EmbedBuilder embed = PlayerEmbedBuilder.BuildPlayerEmbed(trackInfo, $"attachment://{fileName}");

                // Create player control buttons
                ComponentBuilder components = new ComponentBuilder()
                    .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                    .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                    .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                    .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                    .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                // Find and delete the previous player message if it exists
                if (_currentPlayerMessage != null)
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

                // Send the new player message
                _currentPlayerMessage = await _textChannel.SendFileAsync(
                    fileAttachment,
                    embed: embed.Build(),
                    components: components.Build()).ConfigureAwait(false);

                Logs.Debug("Player message sent successfully");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error building/sending player image: {ex.Message}");

                // Try to send a simplified player message without the image
                try
                {
                    EmbedBuilder simpleEmbed = new EmbedBuilder()
                        .WithTitle("Now Playing")
                        .WithDescription($"{trackInfo["Artist"]} - {trackInfo["Title"]}\n{trackInfo["Album"]}\nDuration: {trackInfo["Duration"]}")
                        .WithColor(Discord.Color.Blue);

                    ComponentBuilder components = new ComponentBuilder()
                        .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                    await _textChannel.SendMessageAsync(
                        embed: simpleEmbed.Build(),
                        components: components.Build()).ConfigureAwait(false);

                    Logs.Debug("Sent fallback text-only player message");
                }
                catch (Exception innerEx)
                {
                    Logs.Error($"Failed to send fallback player message: {innerEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in NotifyTrackStartedAsync: {ex.Message}");

            // Try to notify the user of the error
            try
            {
                if (_textChannel != null)
                {
                    await _textChannel.SendMessageAsync("An error occurred while displaying the player.").ConfigureAwait(false);
                }
            }
            catch
            {
                // Suppress secondary errors
            }
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

    /// <summary>Updates the player message with new button components, used to refresh the visual player interface</summary>
    /// <param name="components">The new components to display, containing updated button configurations</param>
    /// <returns>A task representing the asynchronous operation of updating the player message</returns>
    public async Task UpdatePlayerComponentsAsync(ComponentBuilder components)
    {
        if (_currentPlayerMessage == null)
        {
            return;
        }

        try
        {
            await _currentPlayerMessage.ModifyAsync(msg =>
            {
                msg.Components = components.Build();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Failed to update player components: {ex.Message}");
        }
    }
}

/// <summary>Custom options for the CustomPlayer class, extending the standard QueuedLavalinkPlayerOptions with additional configuration</summary>
/// <param name="TextChannel">Gets or sets the Discord text channel where player messages will be sent, used for displaying the visual player and notifications</param>
public sealed record CustomPlayerOptions(ITextChannel? TextChannel) : QueuedLavalinkPlayerOptions
{
    /// <summary>Gets or sets the default volume level, ranging from 0.0 to 1.0</summary>
    public float DefaultVolume { get; init; } = 0.5f;

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