using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Pre-resolves upcoming tracks and caches artwork to reduce gaps between songs</summary>
public class TrackPrefetchService(IHttpClientFactory httpClientFactory) : ITrackPrefetchService
{
    // Artwork cache with LRU timestamps — keyed by URL
    private readonly ConcurrentDictionary<string, (byte[] Bytes, long Ticks)> _artworkCache = new();
    private const int MaxArtworkCacheEntries = 10;

    // Track the URL currently being prefetched to avoid duplicate work
    private volatile string? _currentlyPrefetching;

    /// <inheritdoc />
    public async Task PrefetchNextAsync(QueuedLavalinkPlayer player, CancellationToken cancellationToken = default)
    {
        try
        {
            // Peek at the next item in the queue
            if (player.Queue.Count == 0) return;

            ITrackQueueItem? nextItem = player.Queue.FirstOrDefault();
            if (nextItem is not CustomTrackQueueItem nextTrack) return;

            string? artworkUrl = nextTrack.Artwork;
            if (string.IsNullOrEmpty(artworkUrl) || artworkUrl == "N/A") return;

            // Skip if we're already prefetching this URL or it's already cached
            if (_currentlyPrefetching == artworkUrl || _artworkCache.ContainsKey(artworkUrl))
                return;

            _currentlyPrefetching = artworkUrl;

            // Pre-download artwork in background
            try
            {
                using HttpClient client = httpClientFactory.CreateClient();
                byte[] artworkBytes = await client.GetByteArrayAsync(artworkUrl, cancellationToken);

                // Evict oldest entries (by LRU timestamp) if cache is full
                while (_artworkCache.Count >= MaxArtworkCacheEntries)
                {
                    var oldest = _artworkCache.OrderBy(kvp => kvp.Value.Ticks).FirstOrDefault();
                    if (oldest.Key != null)
                        _artworkCache.TryRemove(oldest.Key, out _);
                    else
                        break;
                }

                _artworkCache[artworkUrl] = (artworkBytes, DateTime.UtcNow.Ticks);
                Logs.Debug($"Prefetched artwork for: {nextTrack.Title} ({artworkBytes.Length} bytes)");
            }
            finally
            {
                _currentlyPrefetching = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when track changes mid-prefetch
        }
        catch (Exception ex)
        {
            Logs.Debug($"Prefetch failed (non-critical): {ex.Message}");
            _currentlyPrefetching = null;
        }
    }

    /// <inheritdoc />
    public byte[]? GetCachedArtwork(string artworkUrl)
    {
        if (_artworkCache.TryGetValue(artworkUrl, out var cached))
        {
            // Update timestamp for LRU behavior (keeps artwork cached for repeat mode)
            _artworkCache[artworkUrl] = (cached.Bytes, DateTime.UtcNow.Ticks);
            Logs.Debug($"Prefetch artwork cache hit: {artworkUrl}");
            return cached.Bytes;
        }
        return null;
    }
}
