using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves queued tracks just in time. Large batches are queued as placeholders (Plex metadata, no
/// Lavalink track yet), and a per-guild worker resolves only the first few items in the queue. This keeps the
/// number of Plex file requests close to what is actually played, and a big playlist no longer has to load
/// before the next request can be queued. The player never plays a placeholder: it waits for
/// <see cref="EnsureResolvedAtAsync"/> first, so every Plex load goes through the shared gate and retries.</summary>
public sealed class QueueResolveService(IAudioService audioService, ITrackResolverService resolver, PlexStreamOptions options) : IDisposable
{
    /// <summary>How often a guild's worker re-reads the queue when nothing wakes it</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>One worker per guild that has a queue being resolved</summary>
    public ConcurrentDictionary<ulong, GuildWorker> Workers { get; } = new();

    /// <summary>Asks the guild's worker to look at the queue again, starting the worker if needed</summary>
    public void Wake(ulong guildId) => GetWorker(guildId).Wake();

    /// <summary>Stops the guild's worker and cancels its resolves in progress (on stop, kill, or disconnect)</summary>
    public void Stop(ulong guildId)
    {
        if (Workers.TryRemove(guildId, out GuildWorker? worker))
            worker.Dispose();
    }

    /// <summary>Makes sure the queue item at <paramref name="index"/> is resolved before the player moves to it.
    /// Items that fail for good are removed, and the item that moves into their place is tried next. Returns true
    /// when the item at that position is playable or the queue is shorter than that; false if cancelled, or if a
    /// failed item could not be removed.</summary>
    public async Task<bool> EnsureResolvedAtAsync(QueuedLavalinkPlayer player, int index = 0, CancellationToken cancellationToken = default)
    {
        GuildWorker worker = GetWorker(player.GuildId);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, worker.Token);
        while (!linked.IsCancellationRequested)
        {
            if (index >= player.Queue.Count || player.Queue[index] is not CustomTrackQueueItem item || item.IsResolved)
                return true;
            if (item.ResolveFailed)
                return false;

            try
            {
                if (await worker.ResolveAsync(player, item).WaitAsync(linked.Token).ConfigureAwait(false))
                    return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            // It failed for good and was removed; the loop checks the item that took its place
        }
        return false;
    }

    /// <summary>The unresolved items among the first <paramref name="resolveAhead"/> in the queue, in queue order</summary>
    public static List<CustomTrackQueueItem> SelectWindow(IEnumerable<ITrackQueueItem> queue, int resolveAhead) =>
        queue.Take(resolveAhead)
            .OfType<CustomTrackQueueItem>()
            .Where(item => !item.IsResolved && !item.ResolveFailed)
            .ToList();

    /// <summary>Resolves one item. Returns true once it has a Lavalink track; false if it failed for good, in which
    /// case it is marked failed and removed from the queue, or if the guild's resolves were cancelled.</summary>
    public async Task<bool> ResolveItemAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item, CancellationToken cancellationToken)
    {
        TrackResolution resolution;
        try
        {
            resolution = await resolver.ResolveTrackAsync(item.SourceTrack, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logs.Error($"[guild {player.GuildId}] Error resolving queued track {PlexUrlHelper.Describe(item.SourceTrack)}: {ex.Message}");
            resolution = TrackResolution.Failed;
        }

        if (resolution.Track is LavalinkTrack loaded)
        {
            item.Reference = new TrackReference(loaded);
            return true;
        }

        item.ResolveFailed = true;
        await RemoveFailedAsync(player, item, resolution).ConfigureAwait(false);
        return false;
    }

    /// <summary>Removes an item that could not be loaded and posts a short notice saying why</summary>
    public static async Task RemoveFailedAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item, TrackResolution resolution)
    {
        try
        {
            if (player.State == PlayerState.Destroyed || !await player.Queue.RemoveAsync(item).ConfigureAwait(false))
                return;
            Logs.Warning($"[guild {player.GuildId}] Removed from queue, could not load ({resolution.Outcome}): {PlexUrlHelper.Describe(item.SourceTrack)}");
            if (player is CustomLavaLinkPlayer custom)
                await custom.NotifyChannelAsync("Track Skipped", $"Skipped **{item.Title}**: {resolution.FailureReason}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logs.Warning($"[guild {player.GuildId}] Could not remove failed track {PlexUrlHelper.Describe(item.SourceTrack)}: {ex.Message}");
        }
    }

    /// <summary>One pass: resolve every unresolved item among the first few in the queue, in order. Returns false
    /// when the guild no longer has a player, which ends the worker.</summary>
    public async Task<bool> RunPassAsync(GuildWorker worker)
    {
        if (await audioService.Players.GetPlayerAsync(worker.GuildId).ConfigureAwait(false) is not QueuedLavalinkPlayer player
            || player.State == PlayerState.Destroyed)
            return false;

        // Re-read the queue on every pass, so shuffle, remove, clear and replace never leave stale positions
        foreach (CustomTrackQueueItem item in SelectWindow(player.Queue, options.ResolveAhead))
        {
            worker.Token.ThrowIfCancellationRequested();
            if (player.Queue.Contains(item))
                await worker.ResolveAsync(player, item).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>The guild's worker, created and started if it isn't running</summary>
    public GuildWorker GetWorker(ulong guildId)
    {
        GuildWorker? created = null;
        GuildWorker worker = Workers.GetOrAdd(guildId, id => created = new GuildWorker(this, id));
        // GetOrAdd may run the factory and then discard its result; only the instance that was stored is started
        if (ReferenceEquals(worker, created))
            worker.Start();
        return worker;
    }

    public void Dispose()
    {
        foreach (ulong guildId in Workers.Keys.ToList())
            Stop(guildId);
    }

    /// <summary>Resolves one guild's queue in the background. Its token is cancelled when the guild stops or the
    /// player goes away, which cancels every resolve it started.</summary>
    public sealed class GuildWorker(QueueResolveService owner, ulong guildId) : IDisposable
    {
        public ulong GuildId { get; } = guildId;

        /// <summary>Released to make the worker run a pass now instead of waiting for the poll interval</summary>
        public SemaphoreSlim WakeSignal { get; } = new(0, 1);

        /// <summary>Cancelled when the worker stops. Not disposed: it never has a timer or wait handle, and callers
        /// may still read <see cref="Token"/> after the worker has stopped.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        public CancellationToken Token => Cancellation.Token;

        /// <summary>Resolves in progress, so concurrent callers share one load per item</summary>
        public ConcurrentDictionary<CustomTrackQueueItem, Lazy<Task<bool>>> InFlight { get; } = new();

        public void Start() => _ = Task.Run(LoopAsync);

        public void Wake()
        {
            try { if (WakeSignal.CurrentCount == 0) WakeSignal.Release(); }
            catch (SemaphoreFullException) { /* already signalled */ }
        }

        /// <summary>Resolves the item, or joins the resolve already running for it</summary>
        public Task<bool> ResolveAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item)
        {
            if (item.IsResolved) return Task.FromResult(true);
            if (item.ResolveFailed) return Task.FromResult(false);
            Lazy<Task<bool>> entry = InFlight.GetOrAdd(item, key =>
            {
                Lazy<Task<bool>>? self = null;
                self = new Lazy<Task<bool>>(() => RunAndReleaseAsync(player, key, self!));
                return self;
            });
            return entry.Value;
        }

        public async Task<bool> RunAndReleaseAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item, Lazy<Task<bool>> entry)
        {
            try
            {
                return await owner.ResolveItemAsync(player, item, Token).ConfigureAwait(false);
            }
            finally
            {
                InFlight.TryRemove(new KeyValuePair<CustomTrackQueueItem, Lazy<Task<bool>>>(item, entry));
            }
        }

        public async Task LoopAsync()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    if (!await owner.RunPassAsync(this).ConfigureAwait(false))
                        break; // no player any more
                    await WakeSignal.WaitAsync(PollInterval, Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* stopped */ }
            catch (Exception ex)
            {
                Logs.Error($"[guild {GuildId}] Queue resolve worker stopped: {ex.Message}");
            }
            finally
            {
                owner.Workers.TryRemove(new KeyValuePair<ulong, GuildWorker>(GuildId, this));
                Cancellation.Cancel();
            }
        }

        public void Dispose() => Cancellation.Cancel();
    }
}
