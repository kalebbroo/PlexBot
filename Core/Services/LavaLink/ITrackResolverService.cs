using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references</summary>
public interface ITrackResolverService
{
    /// <summary>Resolves a single track's playback through Lavalink. Returns null if resolution fails.</summary>
    Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>Resolves multiple tracks in parallel with bounded concurrency and one retry for failures.
    /// Returns results in original order for sequential queue insertion.</summary>
    Task<TrackResolveResult> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        int maxConcurrency = 5,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a parallel track resolution batch</summary>
/// <param name="SuccessCount">Number of tracks successfully resolved</param>
/// <param name="FailedTracks">Names of tracks that permanently failed resolution</param>
/// <param name="ResolvedTracks">Successfully resolved tracks in original playlist order</param>
public record TrackResolveResult(
    int SuccessCount,
    List<string> FailedTracks,
    List<(int Index, Track Track, LavalinkTrack Resolved)> ResolvedTracks);
