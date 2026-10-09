using System.Collections.Concurrent;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.PlexApi;
using PlexBot.Utils;

namespace PlexBot.Core.Services.Music;

/// <summary>Manages per-guild radio sessions. With <c>plex.radio.infinite</c> on, a session tops the queue up from its
/// seed when it runs low, so the radio keeps going until it is stopped, killed, or the bot leaves voice.</summary>
public class RadioSessionManager(IPlexSonicService sonicService)
{
    private readonly ConcurrentDictionary<ulong, RadioSession> _sessions = new();

    /// <summary>Starts a radio session for a guild. The tracks already queued from the seed are remembered, so a refill
    /// does not queue them again.</summary>
    /// <param name="guildId">The Discord guild ID</param>
    /// <param name="seedRatingKey">The Plex rating key to seed radio from</param>
    /// <param name="initialTracks">The tracks queued when the session started</param>
    public void StartSession(ulong guildId, string seedRatingKey, IEnumerable<Track>? initialTracks = null)
    {
        bool isInfinite = BotConfig.GetBool("plex.radio.infinite", false);
        RadioSession session = new()
        {
            SeedRatingKey = seedRatingKey,
            IsInfinite = isInfinite,
            StartedAt = DateTime.UtcNow
        };
        if (initialTracks is not null)
            session.Remember(initialTracks);
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

    /// <summary>Fetches the next radio batch for a session. Tracks it already queued are left out, and the seed moves to
    /// the last track Plex returned so the radio keeps drifting. Nothing is marked as queued here: the caller does that once the
    /// tracks are in the queue.</summary>
    /// <param name="session">The session to refill</param>
    /// <param name="cancellationToken">Cancels the Plex request</param>
    /// <returns>New tracks to queue, possibly empty</returns>
    public async Task<List<Track>> FetchRefillAsync(RadioSession session, CancellationToken cancellationToken = default)
    {
        int batchSize = BotConfig.GetInt("plex.radio.batchSize", 30);
        List<Track> candidates = await sonicService.GetRadioTracksAsync(session.SeedRatingKey, batchSize, cancellationToken);
        // Move the seed on even when every candidate was already queued, so the next fetch starts from somewhere new
        if (candidates.Count > 0)
            session.SeedRatingKey = RadioRefillPolicy.KeyOf(candidates[^1]);
        return session.TakeUnseen(candidates);
    }

    /// <summary>Whether a guild has an active radio session</summary>
    public bool HasActiveSession(ulong guildId) => _sessions.ContainsKey(guildId);
}

/// <summary>An active radio session for one guild</summary>
public class RadioSession
{
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private int _refilling;

    /// <summary>The Plex rating key the next refill is seeded from. Moves to the last track added.</summary>
    public required string SeedRatingKey { get; set; }

    /// <summary>Whether this session auto-refills when the queue runs low</summary>
    public required bool IsInfinite { get; init; }

    /// <summary>When the session was started</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>True while a refill is fetching. Only one refill runs per session at a time.</summary>
    public bool Refilling => Volatile.Read(ref _refilling) == 1;

    /// <summary>Takes the refill slot. Returns false when another refill is already running.</summary>
    public bool TryBeginRefill() => Interlocked.CompareExchange(ref _refilling, 1, 0) == 0;

    /// <summary>Frees the refill slot</summary>
    public void EndRefill() => Volatile.Write(ref _refilling, 0);

    /// <summary>Marks tracks as queued, so later refills leave them out</summary>
    public void Remember(IEnumerable<Track> tracks)
    {
        lock (_queued)
        {
            foreach (Track track in tracks)
                _queued.Add(RadioRefillPolicy.KeyOf(track));
        }
    }

    /// <summary>The tracks from a batch that this session has not queued yet, in the order Plex returned them.
    /// Does not mark them: see <see cref="Remember"/>.</summary>
    public List<Track> TakeUnseen(IEnumerable<Track> candidates)
    {
        lock (_queued)
        {
            return RadioRefillPolicy.Unseen(candidates, _queued);
        }
    }
}

/// <summary>Decisions for infinite radio, kept free of I/O so they can be unit tested</summary>
public static class RadioRefillPolicy
{
    /// <summary>Refill only for an infinite session whose queue is below the threshold, and when no refill is running</summary>
    public static bool ShouldRefill(bool infinite, int queueCount, int threshold, bool alreadyRefilling) =>
        infinite && !alreadyRefilling && queueCount < threshold;

    /// <summary>The identity of a track for de-duplication: its Plex rating key, or its ID when it has none</summary>
    public static string KeyOf(Track track)
    {
        string ratingKey = PlexJsonParser.ExtractRatingKey(track.SourceKey);
        return string.IsNullOrEmpty(ratingKey) ? track.Id : ratingKey;
    }

    /// <summary>Candidates whose key is not in <paramref name="seen"/>, keeping their order and dropping repeats within the batch</summary>
    public static List<Track> Unseen(IEnumerable<Track> candidates, ISet<string> seen)
    {
        List<Track> fresh = [];
        HashSet<string> inBatch = new(StringComparer.Ordinal);
        foreach (Track track in candidates)
        {
            string key = KeyOf(track);
            if (string.IsNullOrEmpty(key))
            {
                fresh.Add(track);
                continue;
            }
            if (seen.Contains(key) || !inBatch.Add(key))
                continue;
            fresh.Add(track);
        }
        return fresh;
    }
}
