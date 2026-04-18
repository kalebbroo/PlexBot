using System.Collections.Concurrent;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.Music;

/// <summary>Manages per-guild radio sessions for infinite radio functionality.
/// Tracks active radio seeds and handles auto-refill when queue runs low.</summary>
public class RadioSessionManager(IPlexSonicService sonicService)
{
    private readonly ConcurrentDictionary<ulong, RadioSession> _sessions = new();

    /// <summary>Starts a new radio session for a guild</summary>
    /// <param name="guildId">The Discord guild ID</param>
    /// <param name="seedRatingKey">The Plex rating key to seed radio from</param>
    public void StartSession(ulong guildId, string seedRatingKey)
    {
        bool isInfinite = BotConfig.GetBool("plex.radio.infinite", false);
        RadioSession session = new()
        {
            SeedRatingKey = seedRatingKey,
            IsInfinite = isInfinite,
            StartedAt = DateTime.UtcNow
        };
        _sessions.AddOrUpdate(guildId, session, (_, _) => session);
        Logs.Info($"Radio session started for guild {guildId}: seed={seedRatingKey}, infinite={isInfinite}");
    }

    /// <summary>Stops the radio session for a guild</summary>
    public void StopSession(ulong guildId)
    {
        if (_sessions.TryRemove(guildId, out _))
        {
            Logs.Debug($"Radio session stopped for guild {guildId}");
        }
    }

    /// <summary>Gets the active radio session for a guild, if any</summary>
    public RadioSession? GetSession(ulong guildId)
    {
        _sessions.TryGetValue(guildId, out RadioSession? session);
        return session;
    }

    /// <summary>Checks if the queue needs refilling and fetches more tracks if so</summary>
    /// <param name="guildId">The Discord guild ID</param>
    /// <param name="currentQueueCount">Current number of tracks in the queue</param>
    /// <returns>List of new tracks to add, or empty if no refill needed</returns>
    public async Task<List<Track>> GetRefillTracksAsync(ulong guildId, int currentQueueCount, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(guildId, out RadioSession? session))
            return [];

        if (!session.IsInfinite)
            return [];

        int threshold = BotConfig.GetInt("plex.radio.refillThreshold", 5);
        if (currentQueueCount >= threshold)
            return [];

        int batchSize = BotConfig.GetInt("plex.radio.batchSize", 30);
        Logs.Info($"Radio auto-refill for guild {guildId}: queue={currentQueueCount}, threshold={threshold}");

        try
        {
            return await sonicService.GetRadioTracksAsync(session.SeedRatingKey, batchSize, cancellationToken);
        }
        catch (Exception ex)
        {
            Logs.Error($"Radio auto-refill failed for guild {guildId}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Whether a guild has an active radio session</summary>
    public bool HasActiveSession(ulong guildId) => _sessions.ContainsKey(guildId);
}

/// <summary>Represents an active radio session for a guild</summary>
public class RadioSession
{
    /// <summary>The Plex rating key used to seed the radio</summary>
    public required string SeedRatingKey { get; init; }

    /// <summary>Whether this session auto-refills when the queue runs low</summary>
    public required bool IsInfinite { get; init; }

    /// <summary>When the session was started</summary>
    public required DateTime StartedAt { get; init; }
}
