using PlexBot.Core.Services.LavaLink;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Pre-resolves upcoming tracks and caches artwork to minimize gaps between songs</summary>
public interface ITrackPrefetchService
{
    /// <summary>Prefetches the next track in the queue. Safe to call multiple times — will not duplicate work.</summary>
    Task PrefetchNextAsync(QueuedLavalinkPlayer player, CancellationToken cancellationToken = default);

    /// <summary>Gets pre-downloaded artwork bytes if available, null otherwise.</summary>
    byte[]? GetCachedArtwork(string artworkUrl);
}
