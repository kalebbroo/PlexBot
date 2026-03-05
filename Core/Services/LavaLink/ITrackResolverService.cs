using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references</summary>
public interface ITrackResolverService
{
    /// <summary>Resolves a single track's playback through Lavalink. Returns null if resolution fails.</summary>
    Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default);

    /// <summary>Resolves multiple tracks in parallel with bounded concurrency and one retry for failures.
    /// Calls onResolved for each track as it completes (with original index for ordered insertion).
    /// Returns a result containing success/fail counts and names of failed tracks.</summary>
    Task<TrackResolveResult> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        Func<Track, LavalinkTrack, int, Task> onResolved,
        int maxConcurrency = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a parallel track resolution batch</summary>
public record TrackResolveResult(int SuccessCount, List<string> FailedTracks);
