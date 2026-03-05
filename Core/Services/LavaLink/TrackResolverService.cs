using System.Collections.Concurrent;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references with support for parallel batch resolution</summary>
public class TrackResolverService(IAudioService audioService) : ITrackResolverService
{
    // Cache resolved tracks by PlaybackUrl to avoid redundant Lavalink calls (replays, repeat mode)
    private readonly ConcurrentDictionary<string, LavalinkTrack> _resolveCache = new();
    private const int MaxResolveCacheEntries = 50;

    /// <inheritdoc />
    public async Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (!string.IsNullOrEmpty(track.PlaybackUrl) && _resolveCache.TryGetValue(track.PlaybackUrl, out LavalinkTrack? cached))
        {
            Logs.Debug($"Resolve cache hit: {track.Title}");
            return cached;
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

        // Cache the result
        if (lavalinkTrack != null && !string.IsNullOrEmpty(track.PlaybackUrl))
        {
            // Evict oldest entries if cache is full
            while (_resolveCache.Count >= MaxResolveCacheEntries)
            {
                string? keyToRemove = _resolveCache.Keys.FirstOrDefault();
                if (keyToRemove != null) _resolveCache.TryRemove(keyToRemove, out _);
            }
            _resolveCache[track.PlaybackUrl] = lavalinkTrack;
        }

        return lavalinkTrack;
    }

    /// <inheritdoc />
    public async Task<TrackResolveResult> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        Func<Track, LavalinkTrack, int, Task> onResolved,
        int maxConcurrency = 5,
        CancellationToken cancellationToken = default)
    {
        int successCount = 0;
        ConcurrentBag<Track> failedFirstPass = [];
        ConcurrentDictionary<int, Track> trackIndexMap = new();

        using SemaphoreSlim semaphore = new(maxConcurrency);

        // First pass — resolve all tracks in parallel
        Task[] tasks = tracks.Select(async (Track track, int index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                LavalinkTrack? resolved = await ResolveTrackAsync(track, cancellationToken);
                if (resolved != null)
                {
                    Interlocked.Increment(ref successCount);
                    await onResolved(track, resolved, index);
                }
                else
                {
                    Logs.Warning($"Failed to resolve track (will retry): {track.Title}");
                    trackIndexMap[index] = track;
                    failedFirstPass.Add(track);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logs.Warning($"Error resolving track (will retry): {track.Title} — {ex.Message}");
                trackIndexMap[index] = track;
                failedFirstPass.Add(track);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // Retry pass — try failed tracks once more, sequentially with a small delay
        List<string> permanentlyFailed = [];

        if (!failedFirstPass.IsEmpty)
        {
            Logs.Info($"Retrying {failedFirstPass.Count} failed tracks...");
            await Task.Delay(2000, cancellationToken);

            foreach (Track track in failedFirstPass)
            {
                try
                {
                    LavalinkTrack? resolved = await ResolveTrackAsync(track, cancellationToken);
                    if (resolved != null)
                    {
                        Interlocked.Increment(ref successCount);
                        int index = trackIndexMap.First(kvp => kvp.Value == track).Key;
                        await onResolved(track, resolved, index);
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

        return new TrackResolveResult(successCount, permanentlyFailed);
    }
}
