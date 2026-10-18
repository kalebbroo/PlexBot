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

    /// <summary>Static channel ids from config, one per server. Empty when static mode has no channel set.</summary>
    public IReadOnlyList<ulong> StaticChannelIds { get; } = ParseStaticChannelIds(
        BotConfig.GetULong("visualPlayer.staticChannel.channelId", 0),
        BotConfig.GetString("visualPlayer.staticChannel.channelIds"));

    /// <summary>The static channel each server resolved to, set once static setup finds it</summary>
    private readonly ConcurrentDictionary<ulong, ITextChannel> _staticChannels = new();

    /// <summary>The static channel id each server has claimed. Set before setup posts anything, so a second configured
    /// channel in the same server is refused rather than racing the first.</summary>
    private readonly ConcurrentDictionary<ulong, ulong> _claimedStaticChannels = new();

    /// <summary>Parses static channel ids: the single <c>channelId</c> first, then the comma-separated <c>channelIds</c>.
    /// Zero, duplicate and non-numeric entries are skipped, and non-numeric ones are logged.</summary>
    public static IReadOnlyList<ulong> ParseStaticChannelIds(ulong singleId, string commaSeparated)
    {
        List<ulong> ids = new();
        if (singleId != 0) ids.Add(singleId);
        foreach (string entry in commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ulong.TryParse(entry, out ulong id))
            {
                Logs.Warning($"visualPlayer.staticChannel.channelIds has an entry that is not a channel ID: '{entry}'");
                continue;
            }
            if (id != 0 && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Claims a server's static channel for one configured id. Returns false when the server already claimed a
    /// different channel. Claiming the same channel again succeeds, so a retried setup is not refused by its own claim.</summary>
    public bool TryClaimStaticChannel(ulong guildId, ulong channelId) => _claimedStaticChannels.GetOrAdd(guildId, channelId) == channelId;

    /// <summary>The static channel id for a server, once its static channel has been set up. Null before that, so a card
    /// is not treated as static while the setup is still posting it.</summary>
    public ulong? StaticChannelIdFor(ulong guildId) =>
        _staticChannels.TryGetValue(guildId, out ITextChannel? channel) ? channel.Id : null;

    /// <summary>The static channel for a server, or null when the server has none set up</summary>
    public ITextChannel? GetStaticChannel(ulong guildId) =>
        _staticChannels.TryGetValue(guildId, out ITextChannel? channel) ? channel : null;

    /// <summary>Returns the channel the visual player for a guild is posted in, if any</summary>
    public ITextChannel? GetChannel(ulong guildId) => GetState(guildId).Channel;

    /// <summary>Returns the visual player message for a guild, if any</summary>
    public IUserMessage? GetMessage(ulong guildId) => GetState(guildId).Message;

    /// <summary>Whether a static channel id is configured. BotConfig.GetULong defaults to 0, which means unset.</summary>
    public static bool HasStaticChannel(ulong? staticChannelId) => staticChannelId is ulong configured && configured != 0;

    /// <summary>Whether a guild's card is the configured static channel's card. The flag is global and only says that static
    /// mode is on, so the card's own channel decides. A configured id of 0 is the unset default.</summary>
    public static bool IsStaticCard(bool enabled, ulong? staticChannelId, ulong? cardChannelId) =>
        enabled && staticChannelId is ulong configured && configured != 0 && cardChannelId == configured;

    /// <summary>The channel a guild's card is in: the card message's channel when there is one, otherwise the configured
    /// static channel if it is in this guild. Null when neither applies.</summary>
    public static ulong? CardChannelFor(ulong? messageChannelId, ulong? staticChannelId) =>
        messageChannelId ?? (HasStaticChannel(staticChannelId) ? staticChannelId : null);

    /// <summary>Whether a guild's card is that guild's static channel card. The channel the card message is in decides,
    /// because a /play from another channel moves GetChannel without moving the card. With no card message, the server's
    /// static channel stands in.</summary>
    public bool IsStaticCard(ulong guildId)
    {
        ulong? staticChannelId = StaticChannelIdFor(guildId);
        ulong? cardChannelId = CardChannelFor(GetMessage(guildId)?.Channel.Id, staticChannelId);
        return IsStaticCard(UseStaticChannel, staticChannelId, cardChannelId);
    }

    /// <summary>The channel a lost card is recreated in. A static card goes only to the static channel, never to the channel
    /// a /play moved to. Null means there is no channel to post in.</summary>
    public static ulong? ResolveCardChannel(bool isStatic, ulong? staticChannelId, ulong? lastChannelId)
    {
        if (!isStatic) return lastChannelId;
        return HasStaticChannel(staticChannelId) ? staticChannelId : null;
    }

    /// <summary>Sets the channel the visual player for a guild is posted in</summary>
    public void SetChannel(ulong guildId, ITextChannel? channel)
    {
        GuildPlayerState state = GetState(guildId);
        lock (state) { state.Channel = channel; }
    }

    /// <summary>Sets a server's static channel, once static setup has posted its card</summary>
    public void SetStaticChannel(ITextChannel channel) => _staticChannels[channel.GuildId] = channel;

    /// <summary>Sets the visual player message for a guild</summary>
    public void SetMessage(ulong guildId, IUserMessage? message)
    {
        GuildPlayerState state = GetState(guildId);
        lock (state) { state.Message = message; }
    }

    /// <summary>Claims the guild's slot for a playback failure notice. Returns false, and claims nothing, when a notice
    /// was claimed less than the window before now.</summary>
    public bool TryClaimFailureNotice(ulong guildId, DateTimeOffset now, TimeSpan window)
    {
        GuildPlayerState state = GetState(guildId);
        lock (state)
        {
            if (!ShouldPostFailureNotice(state.LastFailureNotice, now, window)) return false;
            state.LastFailureNotice = now;
            return true;
        }
    }

    /// <summary>True when no failure notice was claimed within the window before now. A notice exactly one window after
    /// the last one is allowed.</summary>
    public static bool ShouldPostFailureNotice(DateTimeOffset? lastPosted, DateTimeOffset now, TimeSpan window) =>
        lastPosted is null || now - lastPosted.Value >= window;

    private GuildPlayerState GetState(ulong guildId) => _guilds.GetOrAdd(guildId, _ => new GuildPlayerState());

    /// <summary>Mutable visual player state for a single guild</summary>
    private sealed class GuildPlayerState
    {
        public ITextChannel? Channel;
        public IUserMessage? Message;
        public DateTimeOffset? LastFailureNotice;
    }
}
