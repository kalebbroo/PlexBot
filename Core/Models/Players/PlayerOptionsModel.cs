namespace PlexBot.Core.Models.Players;

/// <summary>
/// Represents configuration options for a music player.
/// This model encapsulates all the customizable settings that control
/// player behavior, providing a single point of configuration for the
/// player's operation. It extends the base Lavalink player options with
/// our application-specific settings.
/// </summary>
/// <param name="TextChannel"> Gets or sets the text channel where player messages will be sent.
/// This is the Discord channel where the bot will post now playing messages,
/// error notifications, and other player status updates. </param>
public record PlayerOptions(ITextChannel? TextChannel) : QueuedLavalinkPlayerOptions
{
    /// <summary>
    /// Gets or sets the default volume level (0.0 to 1.0).
    /// This is the initial volume used when the player starts playback.
    /// The value is a floating point between 0 (muted) and 1 (full volume).
    /// </summary>
    public float DefaultVolume { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets whether the player should disconnect after playback ends.
    /// When true, the bot will leave the voice channel after the queue is emptied.
    /// When false, it will remain in the channel until manually disconnected.
    /// </summary>
    public bool DisconnectAfterPlayback { get; set; } = true;

    /// <summary>
    /// Gets or sets the inactivity timeout before automatic disconnection.
    /// If no playback or user interaction occurs for this duration, the bot
    /// will automatically disconnect from the voice channel to free resources.
    /// </summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets whether to announce tracks when they start playing.
    /// When true, the bot will send a "Now Playing" message for each new track.
    /// </summary>
    public bool AnnounceNowPlaying { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show track thumbnails in player messages.
    /// When true, player messages will include album artwork or track thumbnails.
    /// When false, messages will be text-only for reduced bandwidth usage.
    /// </summary>
    public bool ShowThumbnails { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to delete player messages when they become outdated.
    /// When true, "Now Playing" messages will be deleted when a new track starts.
    /// When false, messages will remain in the channel history.
    /// </summary>
    public bool DeleteOutdatedMessages { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of items to show in queue displays.
    /// Used to prevent excessively large embeds when displaying the queue.
    /// </summary>
    public int MaxQueueItemsToShow { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether the player should use premium features if available.
    /// These include higher quality audio, advanced filters, and other enhancements.
    /// </summary>
    public bool UsePremiumFeatures { get; set; } = false;

    /// <summary>
    /// Gets or sets the default repeat mode for new player instances.
    /// Determines how the queue behaves after playback completes.
    /// </summary>
    public TrackRepeatMode DefaultRepeatMode { get; set; } = TrackRepeatMode.None;

    /// <summary>
    /// Creates a new PlayerOptions instance with default settings.
    /// This constructor initializes all settings to their default values,
    /// providing a consistent starting point for player configuration.
    /// </summary>
    public PlayerOptions() : this((ITextChannel?)null)
    {
        // Base QueuedLavalinkPlayerOptions settings
        DisconnectOnStop = false;
        SelfDeaf = true;

        // Extended settings specific to our application
        DefaultVolume = 0.5f;
        DisconnectAfterPlayback = true;
        InactivityTimeout = TimeSpan.FromMinutes(2);
        AnnounceNowPlaying = true;
        ShowThumbnails = true;
        DeleteOutdatedMessages = true;
        MaxQueueItemsToShow = 10;
        UsePremiumFeatures = false;
        DefaultRepeatMode = TrackRepeatMode.None;
    }

    /// <summary>
    /// Creates a new PlayerOptions instance with specified Discord text channel.
    /// This is a convenience method for the most common initialization pattern.
    /// </summary>
    /// <param name="textChannel">The Discord text channel for player messages</param>
    /// <returns>A configured PlayerOptions instance</returns>
    public static PlayerOptions CreateDefault(ITextChannel textChannel)
    {
        return new PlayerOptions
        {
            TextChannel = textChannel
        };
    }
}
