using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Resolves queued tracks just in time. Large batches are queued as placeholders (Plex metadata, no
/// Lavalink track yet), and a per-guild worker resolves only the first few items in the queue. This keeps the
/// number of Plex file requests close to what is actually played, and a big playlist no longer has to load
/// before the next request can be queued.</summary>
public sealed class QueueResolveService(IAudioService audioService, ITrackResolverService resolver) : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeadWait = TimeSpan.FromSeconds(5);

    private readonly int _resolveAhead = Math.Max(1, BotConfig.GetInt("plex.stream.resolveAhead", 3));
    private readonly ConcurrentDictionary<ulong, GuildWorker> _workers = new();

    /// <summary>Asks the guild's worker to look at the queue again, starting the worker if needed</summary>
    public void Wake(ulong guildId)
    {
        GuildWorker worker = _workers.GetOrAdd(guildId, id => new GuildWorker(this, id));
        worker.Signal();
    }

    /// <summary>Stops the guild's worker (on stop or kill)</summary>
    public void Stop(ulong guildId)
    {
        if (_workers.TryRemove(guildId, out GuildWorker? worker))
            worker.Dispose();
    }

    /// <summary>Makes sure the next item in the queue is resolved before the player moves to it. Items that fail
    /// for good are removed and the next one is tried, so the player never gets an unresolved placeholder from a
    /// skip. Waits at most <paramref name="maxWait"/> per item; after that, the player falls back to letting
    /// Lavalink load the URL itself.</summary>
    public async Task EnsureHeadResolvedAsync(QueuedLavalinkPlayer player, TimeSpan? maxWait = null, CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < 5; i++)
        {
            if (!player.Queue.TryPeek(out ITrackQueueItem? head) || head is not CustomTrackQueueItem item || item.IsResolved)
                return;

            Task<bool> resolve = ResolveItemAsync(player, item);
            Task finished = await Task.WhenAny(resolve, Task.Delay(maxWait ?? Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
            if (finished != resolve || await resolve.ConfigureAwait(false))
                return;
            // It failed for good and was removed; check the new head
        }
    }

    /// <inheritdoc cref="EnsureHeadResolvedAsync(QueuedLavalinkPlayer, TimeSpan?, CancellationToken)"/>
    public Task EnsureHeadResolvedBrieflyAsync(QueuedLavalinkPlayer player) => EnsureHeadResolvedAsync(player, HeadWait);

    /// <summary>Resolves one item, sharing the work if it's already in progress. Returns false if it failed for
    /// good, in which case it has been removed from the queue.</summary>
    private Task<bool> ResolveItemAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item)
    {
        lock (item)
        {
            return item.ResolveTask ??= Task.Run(() => ResolveItemCoreAsync(player, item));
        }
    }

    private async Task<bool> ResolveItemCoreAsync(QueuedLavalinkPlayer player, CustomTrackQueueItem item)
    {
        try
        {
            LavalinkTrack? resolved = await resolver.ResolveTrackAsync(item.SourceTrack).ConfigureAwait(false);
            if (resolved != null)
            {
                item.Reference = new TrackReference(resolved);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"[guild {player.GuildId}] Error resolving queued track {item.Title}: {ex.Message}");
        }

        item.ResolveFailed = true;
        if (await player.Queue.RemoveAsync(item).ConfigureAwait(false))
        {
            Logs.Warning($"[guild {player.GuildId}] Removed from queue, could not load: {item.Title} ({TrackResolverService.PartId(item.SourceTrack)})");
            if (player is CustomLavaLinkPlayer custom)
                _ = custom.NotifyChannelAsync("Track Skipped", $"Skipped **{item.Title}**: Plex didn't return the file after several tries.");
        }
        return false;
    }

    /// <summary>One pass: resolve every unresolved item among the first few in the queue, in order</summary>
    private async Task<bool> RunPassAsync(ulong guildId, CancellationToken cancellationToken)
    {
        if (await audioService.Players.GetPlayerAsync(guildId).ConfigureAwait(false) is not QueuedLavalinkPlayer player
            || player.State == PlayerState.Destroyed)
            return false;

        // Re-read the queue on every pass, so shuffle, remove, clear and replace never leave stale positions
        List<CustomTrackQueueItem> window = player.Queue
            .Take(_resolveAhead)
            .OfType<CustomTrackQueueItem>()
            .Where(i => !i.IsResolved && !i.ResolveFailed)
            .ToList();

        foreach (CustomTrackQueueItem item in window)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!player.Queue.Contains(item)) continue;
            await ResolveItemAsync(player, item).ConfigureAwait(false);
        }
        return true;
    }

    public void Dispose()
    {
        foreach (ulong guildId in _workers.Keys.ToList())
            Stop(guildId);
    }

    private sealed class GuildWorker : IDisposable
    {
        private readonly QueueResolveService _owner;
        private readonly ulong _guildId;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly CancellationTokenSource _cts = new();

        public GuildWorker(QueueResolveService owner, ulong guildId)
        {
            _owner = owner;
            _guildId = guildId;
            _ = Task.Run(LoopAsync);
        }

        public void Signal()
        {
            try { if (_signal.CurrentCount == 0) _signal.Release(); }
            catch (SemaphoreFullException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task LoopAsync()
        {
            CancellationToken ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!await _owner.RunPassAsync(_guildId, ct).ConfigureAwait(false))
                        break; // no player any more
                    await _signal.WaitAsync(PollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logs.Error($"[guild {_guildId}] Queue resolve worker stopped: {ex.Message}");
            }
            finally
            {
                _owner._workers.TryRemove(new KeyValuePair<ulong, GuildWorker>(_guildId, this));
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
