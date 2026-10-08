using System.Collections.Concurrent;
using LavalinkCacheMode = Lavalink4NET.Rest.Entities.CacheMode;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references. Plex loads go through the shared
/// gate and retry with backoff.</summary>
public class TrackResolverService(IAudioService audioService, PlexStreamGate plexGate, PlexLoadRetryPolicy retryPolicy,
    PlexStreamOptions options) : ITrackResolverService
{
    /// <summary>Deadline for one Lavalink load. Without one, a stalled Lavalink node leaves the deferred interaction
    /// waiting until Discord's token expires, with no message to the user.</summary>
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Resolved tracks, keyed by <see cref="CacheKey"/>, with the time each was cached</summary>
    public ConcurrentDictionary<string, (LavalinkTrack Track, DateTime CachedAt)> ResolveCache { get; } = new();

    /// <inheritdoc />
    public async Task<TrackResolution> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        string cacheKey = CacheKey(track);
        if (cacheKey.Length > 0 && ResolveCache.TryGetValue(cacheKey, out (LavalinkTrack Track, DateTime CachedAt) cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < options.ResolveCacheLifetime)
            {
                Logs.Debug($"Resolve cache hit: {track.Title}");
                return new TrackResolution(cached.Track, LoadOutcome.Loaded);
            }
            ResolveCache.TryRemove(cacheKey, out _);
        }

        TrackResolution resolution = track.IsPlex
            ? await LoadPlexWithRetryAsync(track, cancellationToken).ConfigureAwait(false)
            : await LoadOtherAsync(track, cancellationToken).ConfigureAwait(false);

        if (resolution.Track is not null && cacheKey.Length > 0)
        {
            EvictIfFull();
            ResolveCache[cacheKey] = (resolution.Track, DateTime.UtcNow);
        }
        return resolution;
    }

    /// <summary>Loads a Plex file URL through the shared gate, backing off and retrying when Plex drops the response</summary>
    public async Task<TrackResolution> LoadPlexWithRetryAsync(Track track, CancellationToken cancellationToken)
    {
        // Refresh, not the default Dynamic mode: Lavalink4NET caches failed loads for 30 minutes, so a retry in
        // Dynamic mode returns the cached failure without asking Lavalink again.
        TrackLoadOptions loadOptions = new() { SearchMode = TrackSearchMode.None, CacheMode = LavalinkCacheMode.Refresh };
        string described = PlexUrlHelper.Describe(track);

        for (int attempt = 0; attempt < retryPolicy.MaxAttempts; attempt++)
        {
            // Throttling is server-wide, so every caller waits, not just this track
            if (attempt > 0)
                plexGate.Cooldown(retryPolicy.DelayBefore(attempt));

            (TrackLoadResult? result, bool timedOut) = await plexGate.RunAsync(
                ct => LoadResultWithTimeoutAsync(track.PlaybackUrl, loadOptions, ct), cancellationToken).ConfigureAwait(false);

            LavalinkTrack? loaded = result?.Track;
            bool isError = result?.Exception is not null;
            LoadOutcome outcome = PlexLoadRetryPolicy.Classify(loaded is not null, isError, timedOut);

            switch (outcome)
            {
                case LoadOutcome.Loaded:
                    if (attempt > 0)
                        Logs.Info($"Plex load recovered on attempt {attempt + 1}: {described}");
                    return new TrackResolution(loaded, LoadOutcome.Loaded);

                case LoadOutcome.NotFound:
                    Logs.Warning($"Plex load found nothing, not retrying: {described}");
                    return new TrackResolution(null, LoadOutcome.NotFound);

                default:
                    string reason = timedOut
                        ? $"timed out after {LoadTimeout.TotalSeconds:N0}s"
                        : $"{result?.Exception?.Severity}: {result?.Exception?.Message}";
                    bool last = attempt + 1 >= retryPolicy.MaxAttempts;
                    string next = last ? "giving up" : $"retrying in {retryPolicy.DelayBefore(attempt + 1).TotalSeconds:N0}s";
                    Logs.Warning($"Plex load failed (attempt {attempt + 1}/{retryPolicy.MaxAttempts}, {reason}), {next}: {described}");
                    break;
            }
        }

        Logs.Error($"Plex load failed after {retryPolicy.MaxAttempts} attempts: {described}");
        return new TrackResolution(null, LoadOutcome.Retriable);
    }

    /// <summary>Loads a non-Plex URL (YouTube and other providers): one load, then a YouTube search fallback</summary>
    public async Task<TrackResolution> LoadOtherAsync(Track track, CancellationToken cancellationToken)
    {
        (TrackLoadResult? result, bool timedOut) = await LoadResultWithTimeoutAsync(track.PlaybackUrl,
            new TrackLoadOptions { SearchMode = TrackSearchMode.None }, cancellationToken).ConfigureAwait(false);

        if (result?.Track is null && track.SourceSystem.Equals("youtube", StringComparison.OrdinalIgnoreCase))
        {
            (result, timedOut) = await LoadResultWithTimeoutAsync(track.PlaybackUrl,
                new TrackLoadOptions { SearchMode = TrackSearchMode.YouTube }, cancellationToken).ConfigureAwait(false);
        }

        LoadOutcome outcome = PlexLoadRetryPolicy.Classify(result?.Track is not null, result?.Exception is not null, timedOut);
        return new TrackResolution(result?.Track, outcome);
    }

    /// <summary>Loads from Lavalink with <see cref="LoadTimeout"/>. A timeout is reported, not thrown; cancellation by
    /// the caller is thrown.</summary>
    public async Task<(TrackLoadResult? Result, bool TimedOut)> LoadResultWithTimeoutAsync(string url, TrackLoadOptions loadOptions,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoadTimeout);
        try
        {
            TrackLoadResult result = await audioService.Tracks.LoadTracksAsync(url, loadOptions, cancellationToken: deadline.Token).ConfigureAwait(false);
            return (result, false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, true);
        }
    }

    /// <summary>Cache key without the Plex token: the part key when known, else the URL with its token removed for
    /// Plex, else the full URL for other sources</summary>
    public static string CacheKey(Track track)
    {
        if (!string.IsNullOrEmpty(track.PartKey)) return "plex:" + track.PartKey;
        if (string.IsNullOrEmpty(track.PlaybackUrl)) return string.Empty;
        return track.IsPlex ? "plex:" + PlexUrlHelper.StripToken(track.PlaybackUrl) : track.PlaybackUrl;
    }

    /// <summary>Drops expired entries, then the oldest tenth of the cache, when it is full</summary>
    public void EvictIfFull()
    {
        if (ResolveCache.Count < options.ResolveCacheSize) return;

        DateTime cutoff = DateTime.UtcNow - options.ResolveCacheLifetime;
        foreach (KeyValuePair<string, (LavalinkTrack Track, DateTime CachedAt)> entry in ResolveCache)
        {
            if (entry.Value.CachedAt < cutoff)
                ResolveCache.TryRemove(entry.Key, out _);
        }

        int excess = ResolveCache.Count - options.ResolveCacheSize + 1;
        if (excess <= 0) return;
        // One sort per sweep, removing at least a tenth of the cache, rather than one sort per insert
        foreach (KeyValuePair<string, (LavalinkTrack Track, DateTime CachedAt)> entry in ResolveCache
            .OrderBy(e => e.Value.CachedAt).Take(Math.Max(excess, options.ResolveCacheSize / 10)).ToList())
        {
            ResolveCache.TryRemove(entry.Key, out _);
        }
    }
}
