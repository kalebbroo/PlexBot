using PlexBot.Utils;

namespace PlexBot.Core.Models.Players;

/// <summary>Represents configuration options for a music player with customizable settings that control player behavior</summary>
/// <param name="CurrentPlayerChannel">The Discord text channel initially specified for player messages</param>
public record PlayerOptions(ITextChannel? CurrentPlayerChannel) : QueuedLavalinkPlayerOptions
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
}

/// <summary>Manages runtime state for the Visual Player across the application</summary>
public class VisualPlayerStateManager
{
    /// <summary>Current active channel for player messages</summary>
    public ITextChannel? CurrentPlayerChannel { get; set; }

    /// <summary>Current active player message in the channel</summary>
    public IUserMessage? CurrentPlayerMessage { get; set; }

    /// <summary>Controls whether to use visual album art display vs text-only player</summary>
    public bool UseModernPlayer { get; set; } = EnvConfig.GetBool("USE_MODERN_PLAYER", true);

    /// <summary>Controls whether to use a dedicated channel for player messages</summary>
    public bool UseStaticChannel { get; set; } = EnvConfig.GetBool("USE_STATIC_PLAYER_CHANNEL", false);

    /// <summary>Optional channel ID to use as static player channel</summary>
    public ulong? StaticChannelId { get; set; } = EnvConfig.GetLong("STATIC_PLAYER_CHANNEL_ID", 0);
}
