using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Pre-resolves upcoming tracks and caches artwork to reduce gaps between songs</summary>
public class TrackPrefetchService(IHttpClientFactory httpClientFactory) : ITrackPrefetchService
{
    // Artwork cache with LRU timestamps — keyed by URL
    private readonly ConcurrentDictionary<string, (byte[] Bytes, long Ticks)> _artworkCache = new();
    private const int MaxArtworkCacheEntries = 10;

    // The URL each server is prefetching now. One slot for every server would let one server's download hide another's,
    // and a finishing download would clear a slot it no longer owns.
    private readonly ConcurrentDictionary<ulong, string> _prefetching = new();

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

            // Skip if this server is already prefetching this URL or it's already cached
            if ((_prefetching.TryGetValue(player.GuildId, out string? inFlight) && inFlight == artworkUrl)
                || _artworkCache.ContainsKey(artworkUrl))
                return;

            _prefetching[player.GuildId] = artworkUrl;

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
                _prefetching.TryRemove(new KeyValuePair<ulong, string>(player.GuildId, artworkUrl));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when track changes mid-prefetch
        }
        catch (Exception ex)
        {
            Logs.Debug($"Prefetch failed (non-critical): {ex.Message}");
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
