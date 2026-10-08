using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable references</summary>
public interface ITrackResolverService
{
    /// <summary>Resolves a single track's playback through Lavalink. The result carries the track when it loaded,
    /// and how the load ended when it didn't. Throws <see cref="OperationCanceledException"/> if cancelled.</summary>
    Task<TrackResolution> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default);
}
