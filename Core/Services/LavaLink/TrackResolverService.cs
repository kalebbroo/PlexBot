using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves Track objects into Lavalink-playable LavalinkTrack references with support for parallel batch resolution</summary>
public class TrackResolverService(IAudioService audioService) : ITrackResolverService
{
    /// <inheritdoc />
    public async Task<LavalinkTrack?> ResolveTrackAsync(Track track, CancellationToken cancellationToken = default)
    {
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

        return lavalinkTrack;
    }

    /// <inheritdoc />
    public async Task<int> ResolveTracksParallelAsync(
        IReadOnlyList<Track> tracks,
        Func<Track, LavalinkTrack, int, Task> onResolved,
        int maxConcurrency = 5,
        CancellationToken cancellationToken = default)
    {
        int successCount = 0;
        using SemaphoreSlim semaphore = new(maxConcurrency);

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
                    Logs.Warning($"Failed to resolve track: {track.Title}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error resolving track {track.Title}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        return successCount;
    }
}
