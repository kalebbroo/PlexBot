using System.Collections.Concurrent;
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

/// <summary>Manages runtime state for the Visual Player across the application with thread-safe access</summary>
public class VisualPlayerStateManager
{
    private readonly ConcurrentDictionary<ulong, GuildPlayerState> _guilds = new();

    /// <summary>Controls whether to use visual album art display vs text-only player</summary>
    public bool UseModernPlayer { get; set; } = BotConfig.GetBool("visualPlayer.useModernPlayer", true);

    /// <summary>Controls whether the live-updating progress bar is shown on the visual player</summary>
    public bool UseProgressBar { get; set; } = BotConfig.GetBool("visualPlayer.progressBar.enabled", true);

    /// <summary>Controls whether to use a dedicated channel for player messages</summary>
    public bool UseStaticChannel { get; set; } = BotConfig.GetBool("visualPlayer.staticChannel.enabled", false);

    /// <summary>Optional channel ID to use as static player channel</summary>
    public ulong? StaticChannelId { get; set; } = BotConfig.GetULong("visualPlayer.staticChannel.channelId", 0);

    /// <summary>Returns the channel the visual player for a guild is posted in, if any</summary>
    public ITextChannel? GetChannel(ulong guildId) => GetState(guildId).Channel;

    /// <summary>Returns the visual player message for a guild, if any</summary>
    public IUserMessage? GetMessage(ulong guildId) => GetState(guildId).Message;

    /// <summary>Sets the channel the visual player for a guild is posted in</summary>
    public void SetChannel(ulong guildId, ITextChannel? channel)
    {
        GuildPlayerState state = GetState(guildId);
        lock (state) { state.Channel = channel; }
    }

    /// <summary>Sets the visual player message for a guild</summary>
    public void SetMessage(ulong guildId, IUserMessage? message)
    {
        GuildPlayerState state = GetState(guildId);
        lock (state) { state.Message = message; }
    }

    private GuildPlayerState GetState(ulong guildId) => _guilds.GetOrAdd(guildId, _ => new GuildPlayerState());

    /// <summary>Mutable visual player state for a single guild</summary>
    private sealed class GuildPlayerState
    {
        public ITextChannel? Channel;
        public IUserMessage? Message;
    }
}
