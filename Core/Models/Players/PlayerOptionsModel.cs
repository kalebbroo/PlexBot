namespace PlexBot.Core.Models.Players;

/// <summary>Represents configuration options for a music player with customizable settings that control player behavior</summary>
/// <param name="TextChannel">The Discord text channel where player messages, status updates, and notifications will be sent</param>
public record PlayerOptions(ITextChannel? TextChannel) : QueuedLavalinkPlayerOptions
{
    /// <summary>Initial volume level (0.0 to 1.0) used when the player starts playback</summary>
    public float DefaultVolume { get; set; } = 0.5f;

    /// <summary>Controls whether the bot automatically leaves the voice channel after the queue is emptied</summary>
    public bool DisconnectAfterPlayback { get; set; } = true;

    /// <summary>Duration of inactivity before automatic disconnection to conserve resources</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Determines if the bot sends a "Now Playing" message for each new track</summary>
    public bool AnnounceNowPlaying { get; set; } = true;

    /// <summary>Controls whether player messages include album artwork or track thumbnails for visual enhancement</summary>
    public bool ShowThumbnails { get; set; } = true;

    /// <summary>Determines if "Now Playing" messages are removed when a new track starts to keep the channel clean</summary>
    public bool DeleteOutdatedMessages { get; set; } = true;

    /// <summary>Maximum number of items to display in queue listings to prevent oversized embeds</summary>
    public int MaxQueueItemsToShow { get; set; } = 10;

    /// <summary>Enables access to higher quality audio, advanced filters, and other enhanced features if available</summary>
    public bool UsePremiumFeatures { get; set; } = false;

    /// <summary>Controls how the queue behaves after playback completes (None, Track, Queue)</summary>
    public TrackRepeatMode DefaultRepeatMode { get; set; } = TrackRepeatMode.None;

    /// <summary>Creates a new PlayerOptions instance with default settings for consistent player configuration</summary>
    public PlayerOptions() : this((ITextChannel?)null)
    {
        // Base QueuedLavalinkPlayerOptions settings
        DisconnectOnStop = false;
        SelfDeaf = true;

        // Extended settings specific to our application
        DefaultVolume = 0.2f;
        DisconnectAfterPlayback = true;
        InactivityTimeout = TimeSpan.FromMinutes(2);
        AnnounceNowPlaying = true;
        ShowThumbnails = true;
        DeleteOutdatedMessages = true;
        MaxQueueItemsToShow = 10;
        UsePremiumFeatures = false;
        DefaultRepeatMode = TrackRepeatMode.None;
    }

    /// <summary>Creates a new PlayerOptions instance with a specified Discord text channel for convenience</summary>
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
