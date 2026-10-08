using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LavalinkCacheMode = Lavalink4NET.Rest.Entities.CacheMode;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references with support for parallel batch resolution</summary>
public partial class TrackResolverService(IAudioService audioService, PlexStreamGate plexGate, PlexLoadRetryPolicy retryPolicy) : ITrackResolverService
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);

    // Resolved tracks, keyed by Plex part key (or the URL without its token), with a time limit
    private readonly ConcurrentDictionary<string, (LavalinkTrack Track, DateTime CachedAt)> _resolveCache = new();
    private readonly int _maxResolveCacheEntries = Math.Max(1, BotConfig.GetInt("plex.resolveCacheSize", 500));
    private readonly TimeSpan _resolveCacheLifetime = TimeSpan.FromMinutes(Math.Max(1, BotConfig.GetInt("plex.resolveCacheMinutes", 60)));

    /// <inheritdoc />
    public async Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        string cacheKey = CacheKey(track);
        if (cacheKey.Length > 0 && _resolveCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < _resolveCacheLifetime)
            {
                Logs.Debug($"Resolve cache hit: {track.Title}");
                return cached.Track;
            }
            _resolveCache.TryRemove(cacheKey, out _);
        }

        LavalinkTrack? lavalinkTrack = IsPlex(track)
            ? await LoadPlexWithRetryAsync(track, cancellationToken)
            : await LoadOtherAsync(track, cancellationToken);

        if (lavalinkTrack != null && cacheKey.Length > 0)
        {
            EvictIfFull();
            _resolveCache[cacheKey] = (lavalinkTrack, DateTime.UtcNow);
        }

        return lavalinkTrack;
    }

    /// <inheritdoc />
    public void Invalidate(Track track)
    {
        string cacheKey = CacheKey(track);
        if (cacheKey.Length > 0)
            _resolveCache.TryRemove(cacheKey, out _);
    }

    /// <inheritdoc />
    public async Task<TrackResolveResult> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        int maxConcurrency = 5,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int successCount = 0;
        ConcurrentDictionary<int, (Track Track, LavalinkTrack Resolved)> resolvedMap = new();
        ConcurrentDictionary<int, string> failedMap = new();

        // Plex loads are also limited process-wide by the gate, so this only bounds how many tasks queue for it.
        // Each load retries with backoff on its own, so there is no separate retry pass.
        using SemaphoreSlim semaphore = new(Math.Max(1, maxConcurrency));

        Task[] tasks = tracks.Select(async (Track track, int index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                LavalinkTrack? resolved = await ResolveTrackAsync(track, cancellationToken);
                if (resolved != null)
                {
                    int count = Interlocked.Increment(ref successCount);
                    resolvedMap[index] = (track, resolved);
                    progress?.Report(count);
                }
                else
                {
                    failedMap[index] = track.Title ?? "Unknown Track";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error resolving track: {track.Title} ({PartId(track)}): {ex.Message}");
                failedMap[index] = track.Title ?? "Unknown Track";
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        List<string> failed = failedMap.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToList();
        Logs.Info($"Resolved {successCount} of {tracks.Count} tracks, {failed.Count} failed");

        List<(int Index, Track Track, LavalinkTrack Resolved)> ordered = resolvedMap
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value.Track, kvp.Value.Resolved))
            .ToList();

        return new TrackResolveResult(successCount, failed, ordered);
    }

    /// <summary>Loads a Plex file URL through the shared gate, backing off and retrying when Plex drops the response</summary>
    private async Task<LavalinkTrack?> LoadPlexWithRetryAsync(Track track, CancellationToken cancellationToken)
    {
        // Refresh, not the default Dynamic mode: Lavalink4NET caches failed loads for 30 minutes, so a retry in
        // Dynamic mode returns the cached failure without asking Lavalink again.
        TrackLoadOptions options = new() { SearchMode = TrackSearchMode.None, CacheMode = LavalinkCacheMode.Refresh };

        for (int attempt = 0; attempt < retryPolicy.MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                // Throttling is server-wide, so every caller waits, not just this track
                TimeSpan delay = retryPolicy.DelayBefore(attempt);
                plexGate.Cooldown(delay);
            }

            (TrackLoadResult? result, bool timedOut) = await plexGate.RunAsync(
                ct => LoadResultWithTimeoutAsync(track.PlaybackUrl, options, ct), cancellationToken);

            LavalinkTrack? loaded = result?.Track;
            bool isError = result is { } r && r.Exception is not null;
            LoadOutcome outcome = PlexLoadRetryPolicy.Classify(loaded is not null, isError, timedOut);

            switch (outcome)
            {
                case LoadOutcome.Loaded:
                    if (attempt > 0)
                        Logs.Info($"Plex load recovered on attempt {attempt + 1}: {track.Title} ({PartId(track)})");
                    return loaded;

                case LoadOutcome.NotFound:
                    Logs.Warning($"Plex load found nothing, not retrying: {track.Title} ({PartId(track)})");
                    return null;

                default:
                    string reason = timedOut
                        ? $"timed out after {LoadTimeout.TotalSeconds:N0}s"
                        : $"{result?.Exception?.Severity}: {result?.Exception?.Message}";
                    bool last = attempt + 1 >= retryPolicy.MaxAttempts;
                    string next = last ? "giving up" : $"retrying in {retryPolicy.DelayBefore(attempt + 1).TotalSeconds:N0}s";
                    Logs.Warning($"Plex load failed (attempt {attempt + 1}/{retryPolicy.MaxAttempts}, {reason}), {next}: {track.Title} ({PartId(track)})");
                    break;
            }
        }

        Logs.Error($"Plex load failed after {retryPolicy.MaxAttempts} attempts: {track.Title} ({PartId(track)})");
        return null;
    }

    /// <summary>Loads a non-Plex URL (YouTube and other providers) with the previous behaviour: one load, then a
    /// YouTube search fallback</summary>
    private async Task<LavalinkTrack?> LoadOtherAsync(Track track, CancellationToken cancellationToken)
    {
        (TrackLoadResult? result, _) = await LoadResultWithTimeoutAsync(track.PlaybackUrl,
            new TrackLoadOptions { SearchMode = TrackSearchMode.None }, cancellationToken);
        LavalinkTrack? loaded = result?.Track;

        if (loaded == null && track.SourceSystem.Equals("youtube", StringComparison.OrdinalIgnoreCase))
        {
            (result, _) = await LoadResultWithTimeoutAsync(track.PlaybackUrl,
                new TrackLoadOptions { SearchMode = TrackSearchMode.YouTube }, cancellationToken);
            loaded = result?.Track;
        }
        return loaded;
    }

    /// <summary>Loads from Lavalink with a deadline. Without one, a stalled Lavalink node leaves the deferred
    /// interaction waiting until Discord's token expires, with no message to the user.</summary>
    private async Task<(TrackLoadResult? Result, bool TimedOut)> LoadResultWithTimeoutAsync(string url, TrackLoadOptions options,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoadTimeout);
        try
        {
            TrackLoadResult result = await audioService.Tracks.LoadTracksAsync(url, options, cancellationToken: deadline.Token);
            return (result, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, true);
        }
    }

    private static bool IsPlex(Track track) =>
        track.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase);

    /// <summary>Cache key without the Plex token: the part key when known, else the URL with its query removed
    /// for Plex, else the full URL for other sources</summary>
    internal static string CacheKey(Track track)
    {
        if (!string.IsNullOrEmpty(track.PartKey)) return "plex:" + track.PartKey;
        if (string.IsNullOrEmpty(track.PlaybackUrl)) return "";
        return IsPlex(track) ? "plex:" + StripToken(track.PlaybackUrl) : track.PlaybackUrl;
    }

    internal static string StripToken(string url) => TokenPattern().Replace(url, "");

    /// <summary>"part 12345" for logs, from the part key or URL, never the token</summary>
    internal static string PartId(Track track)
    {
        Match m = PartPattern().Match(!string.IsNullOrEmpty(track.PartKey) ? track.PartKey : track.PlaybackUrl ?? "");
        return m.Success ? $"part {m.Groups[1].Value}" : "no part id";
    }

    [GeneratedRegex(@"[?&]X-Plex-Token=[^&]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"/library/parts/(\d+)")]
    private static partial Regex PartPattern();

    /// <summary>Drops expired entries, then the oldest ones, until there is room for one more</summary>
    private void EvictIfFull()
    {
        if (_resolveCache.Count < _maxResolveCacheEntries) return;

        DateTime cutoff = DateTime.UtcNow - _resolveCacheLifetime;
        foreach (var kvp in _resolveCache)
            if (kvp.Value.CachedAt < cutoff)
                _resolveCache.TryRemove(kvp.Key, out _);

        int excess = _resolveCache.Count - _maxResolveCacheEntries + 1;
        if (excess <= 0) return;
        // One sort per eviction sweep, removing a tenth of the cache, rather than one sort per insert
        foreach (var kvp in _resolveCache.OrderBy(k => k.Value.CachedAt).Take(Math.Max(excess, _maxResolveCacheEntries / 10)))
            _resolveCache.TryRemove(kvp.Key, out _);
    }
}
