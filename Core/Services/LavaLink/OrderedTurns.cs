namespace PlexBot.Core.Services.LavaLink;

/// <summary>Keeps queue additions in the order they were requested. A batch takes a ticket when it is requested, waits for
/// its turn before it touches the queue, and releases the ticket when it is done. A ticket released before its turn (a batch
/// that failed, or gave up waiting) is skipped, so it never holds up the batches behind it.</summary>
internal sealed class OrderedTurns
{
    private readonly object _sync = new();
    private readonly Dictionary<long, TaskCompletionSource> _waiting = new();
    private readonly HashSet<long> _abandoned = new();
    private long _issued;
    private long _next;

    /// <summary>The next ticket, in request order</summary>
    public long Take()
    {
        lock (_sync) return _issued++;
    }

    /// <summary>Completes when it is the ticket's turn. A wait that is cancelled releases its ticket, so the tickets
    /// behind it are not held up.</summary>
    public async Task WaitTurnAsync(long ticket, CancellationToken cancellationToken)
    {
        Task granted = Enqueue(ticket);
        try
        {
            await granted.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Release(ticket);
            throw;
        }
    }

    /// <summary>Ends the ticket's place in the order. Never waits, and calling it again does nothing.</summary>
    public void Release(long ticket)
    {
        lock (_sync)
        {
            if (ticket < _next)
                return;
            if (ticket > _next)
            {
                // Not reached yet: it is skipped when its turn comes
                _abandoned.Add(ticket);
                if (_waiting.Remove(ticket, out TaskCompletionSource? waiter))
                    waiter.TrySetCanceled();
                return;
            }
            _next++;
            AdvanceLocked();
        }
    }

    private Task Enqueue(long ticket)
    {
        lock (_sync)
        {
            if (ticket < _next || _abandoned.Contains(ticket))
                throw new InvalidOperationException($"Queue ticket {ticket} was already released");
            if (ticket == _next)
                return Task.CompletedTask;
            TaskCompletionSource waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting[ticket] = waiter;
            return waiter.Task;
        }
    }

    /// <summary>Moves past abandoned tickets, then wakes the ticket that is now up, if it is waiting</summary>
    private void AdvanceLocked()
    {
        while (_abandoned.Remove(_next))
            _next++;
        if (_waiting.Remove(_next, out TaskCompletionSource? waiter))
            waiter.TrySetResult();
    }
}
