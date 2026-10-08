using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references</summary>
public interface ITrackResolverService
{
    /// <summary>Resolves a single track's playback through Lavalink. Returns null if resolution fails.</summary>
    Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default);
}
