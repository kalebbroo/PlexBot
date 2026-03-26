using System.Collections.Concurrent;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references with support for parallel batch resolution</summary>
public class TrackResolverService(IAudioService audioService) : ITrackResolverService
{
    // Cache resolved tracks by PlaybackUrl to avoid redundant Lavalink calls (replays, repeat mode)
    // Values are (LavalinkTrack, Ticks) for LRU eviction
    private readonly ConcurrentDictionary<string, (LavalinkTrack Track, long Ticks)> _resolveCache = new();
    private readonly int _maxResolveCacheEntries = BotConfig.GetInt("plex.resolveCacheSize", 500);

    /// <inheritdoc />
    public async Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (!string.IsNullOrEmpty(track.PlaybackUrl) && _resolveCache.TryGetValue(track.PlaybackUrl, out var cached))
        {
            // Update timestamp for LRU behavior
            _resolveCache[track.PlaybackUrl] = (cached.Track, DateTime.UtcNow.Ticks);
            Logs.Debug($"Resolve cache hit: {track.Title}");
            return cached.Track;
        }

        TrackLoadOptions loadOptions = new() { SearchMode = TrackSearchMode.None };

        LavalinkTrack? lavalinkTrack = await audioService.Tracks.LoadTrackAsync(
            track.PlaybackUrl,
            loadOptions,
            cancellationToken: cancellationToken);

        // YouTube fallback: try search mode if direct URL fails
        if (lavalinkTrack == null && track.SourceSystem.Equals("youtube", StringComparison.OrdinalIgnoreCase))
        {
            TrackLoadOptions searchOptions = new() { SearchMode = TrackSearchMode.YouTube };
            lavalinkTrack = await audioService.Tracks.LoadTrackAsync(
                track.PlaybackUrl,
                searchOptions,
                cancellationToken: cancellationToken);
        }

        // Cache the result with LRU timestamp
        if (lavalinkTrack != null && !string.IsNullOrEmpty(track.PlaybackUrl))
        {
            EvictOldestIfFull();
            _resolveCache[track.PlaybackUrl] = (lavalinkTrack, DateTime.UtcNow.Ticks);
        }

        return lavalinkTrack;
    }

    /// <inheritdoc />
    public async Task<TrackResolveResult> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        int maxConcurrency = 5,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int successCount = 0;
        ConcurrentDictionary<int, Track> failedIndexMap = new();
        ConcurrentDictionary<int, (Track Track, LavalinkTrack Resolved)> resolvedMap = new();

        using SemaphoreSlim semaphore = new(maxConcurrency);

        // First pass — resolve all tracks in parallel, collecting results by index
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
                    Logs.Warning($"Failed to resolve track (will retry): {track.Title}");
                    failedIndexMap[index] = track;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logs.Warning($"Error resolving track (will retry): {track.Title} — {ex.Message}");
                failedIndexMap[index] = track;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // Retry pass — try failed tracks once more, sequentially with a small delay
        List<string> permanentlyFailed = [];

        if (!failedIndexMap.IsEmpty)
        {
            Logs.Info($"Retrying {failedIndexMap.Count} failed tracks...");
            await Task.Delay(2000, cancellationToken);

            foreach (var (index, track) in failedIndexMap.OrderBy(kvp => kvp.Key))
            {
                try
                {
                    LavalinkTrack? resolved = await ResolveTrackAsync(track, cancellationToken);
                    if (resolved != null)
                    {
                        int count = Interlocked.Increment(ref successCount);
                        resolvedMap[index] = (track, resolved);
                        progress?.Report(count);
                        Logs.Info($"Retry succeeded: {track.Title}");
                    }
                    else
                    {
                        string name = track.Title ?? "Unknown Track";
                        Logs.Error($"Failed to resolve track after retry: {name} — URL: {track.PlaybackUrl}");
                        permanentlyFailed.Add(name);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    string name = track.Title ?? "Unknown Track";
                    Logs.Error($"Error resolving track after retry: {name} — {ex.Message}");
                    permanentlyFailed.Add(name);
                }

                await Task.Delay(500, cancellationToken);
            }
        }

        // Build ordered list preserving original playlist order
        List<(int Index, Track Track, LavalinkTrack Resolved)> ordered = resolvedMap
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value.Track, kvp.Value.Resolved))
            .ToList();

        return new TrackResolveResult(successCount, permanentlyFailed, ordered);
    }

    /// <summary>Evicts the oldest cache entry (by timestamp) when the cache is full</summary>
    private void EvictOldestIfFull()
    {
        while (_resolveCache.Count >= _maxResolveCacheEntries)
        {
            var oldest = _resolveCache.OrderBy(kvp => kvp.Value.Ticks).FirstOrDefault();
            if (oldest.Key != null)
                _resolveCache.TryRemove(oldest.Key, out _);
            else
                break;
        }
    }
}
