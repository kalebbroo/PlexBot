using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;

namespace PlexBot.Services;

/// <summary>
/// Custom Lavalink player with enhanced Discord integration.
/// Extends the QueuedLavalinkPlayer with specialized player UI, richer metadata,
/// and event handlers specifically designed for the Plex Music Bot. This class
/// handles the actual playback while providing a rich visual interface in Discord.
/// </summary>
public sealed class CustomPlayer : QueuedLavalinkPlayer
{
    private readonly ITextChannel? _textChannel;
    private IUserMessage? _currentPlayerMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPlayer"/> class.
    /// Creates a player with the specified properties and options.
    /// </summary>
    /// <param name="properties">The player properties</param>
    public CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties)
        : base(properties)
    {
        _textChannel = properties.Options.Value.TextChannel;

        // Log initialization
        Logs.Debug($"CustomPlayer initialized for guild {GuildId} in channel {_textChannel?.Id}");
    }

    /// <summary>
    /// Called when a track starts playing.
    /// Creates and sends a visual player embed with track information and player controls.
    /// </summary>
    /// <param name="track">The track that started playing</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
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
            var trackInfo = new Dictionary<string, string>
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

            // Build the player image
            using var memoryStream = new MemoryStream();
            Image image = await ImageBuilder.BuildPlayerImageAsync(trackInfo);
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

    /// <summary>
    /// Called when a track finishes playing.
    /// Handles cleanup and logging of track completion.
    /// </summary>
    /// <param name="queueItem">The track that ended</param>
    /// <param name="endReason">The reason the track ended</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
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

    /// <summary>
    /// Called when the player becomes active.
    /// This happens when users join the voice channel after all users had left.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public ValueTask NotifyPlayerActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player active event for guild {GuildId}");
        return default; // No special handling needed
    }

    /// <summary>
    /// Called when the player becomes inactive due to inactivity timeout.
    /// This is triggered when all users leave the voice channel and the inactivity
    /// deadline is reached. The player will automatically stop and disconnect.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
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

    /// <summary>
    /// Called when the player state changes.
    /// This can be used to update the visual player interface when state changes.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public ValueTask NotifyPlayerTrackedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Logs.Debug($"Player tracked state change for guild {GuildId}");
        return default; // No special handling needed
    }

    /// <summary>
    /// Updates the player message with new button components.
    /// This is used when the player state changes to update control buttons.
    /// </summary>
    /// <param name="components">The new components to display</param>
    /// <returns>A task representing the asynchronous operation</returns>
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

/// <summary>
/// Custom options for the CustomPlayer class.
/// Extends the standard QueuedLavalinkPlayerOptions with additional configuration
/// specific to the Plex Music Bot implementation.
/// </summary>
/// <param name="TextChannel"> Gets or sets the Discord text channel where player messages will be sent.
/// This channel is used for displaying the visual player and notifications. </param>
public sealed record CustomPlayerOptions(ITextChannel? TextChannel) : QueuedLavalinkPlayerOptions
{
    /// <summary>
    /// Gets or sets the default volume level (0.0 to 1.0).
    /// </summary>
    public float DefaultVolume { get; init; } = 0.5f;

    /// <summary>
    /// Gets or sets whether to show track thumbnails in player messages.
    /// </summary>
    public bool ShowThumbnails { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to delete player messages when they become outdated.
    /// </summary>
    public bool DeleteOutdatedMessages { get; init; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPlayerOptions"/> class.
    /// Sets default values for player options.
    /// </summary>
    public CustomPlayerOptions() : this(null)
    {
        // Set LavaLink player defaults
        DisconnectOnStop = false;
        SelfDeaf = true;

        // Other defaults are set through auto-properties
    }
}