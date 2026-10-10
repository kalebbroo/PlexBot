using Microsoft.Extensions.Time.Testing;
using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class OrderedTurnsTests
{
    // Only a safety net: every wait in these tests completes at once when the behaviour is right
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Turns_AreGranted_InTicketOrder()
    {
        OrderedTurns turns = new();
        long first = turns.Take();
        long second = turns.Take();
        long third = turns.Take();

        Task waitSecond = turns.WaitTurnAsync(second, CancellationToken.None);
        Task waitThird = turns.WaitTurnAsync(third, CancellationToken.None);
        Assert.False(waitSecond.IsCompleted);
        Assert.False(waitThird.IsCompleted);

        turns.Release(first);
        await waitSecond.WaitAsync(Safety);
        Assert.False(waitThird.IsCompleted);

        turns.Release(second);
        await waitThird.WaitAsync(Safety);
    }

    [Fact]
    public async Task CancelledWait_ReleasesItsTicket_SoLaterTicketsProceed()
    {
        OrderedTurns turns = new();
        long first = turns.Take();
        long cancelled = turns.Take();
        long later = turns.Take();

        using CancellationTokenSource cancel = new();
        Task waitCancelled = turns.WaitTurnAsync(cancelled, cancel.Token);
        Task waitLater = turns.WaitTurnAsync(later, CancellationToken.None);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitCancelled.WaitAsync(Safety));

        turns.Release(first);
        await waitLater.WaitAsync(Safety);
    }

    [Fact]
    public async Task DoubleRelease_IsHarmless_AndDoesNotSkipAPendingTicket()
    {
        OrderedTurns turns = new();
        long first = turns.Take();
        long second = turns.Take();
        long third = turns.Take();

        turns.Release(first);
        turns.Release(first);

        Task waitThird = turns.WaitTurnAsync(third, CancellationToken.None);
        Assert.False(waitThird.IsCompleted);

        await turns.WaitTurnAsync(second, CancellationToken.None).WaitAsync(Safety);
        turns.Release(second);
        turns.Release(second);

        await waitThird.WaitAsync(Safety);
    }

    [Fact]
    public async Task AbandonedTicket_IsSkipped_WhenItsTurnComes()
    {
        OrderedTurns turns = new();
        long first = turns.Take();
        long skipped = turns.Take();
        long later = turns.Take();

        turns.Release(skipped);
        turns.Release(skipped);
        Task waitLater = turns.WaitTurnAsync(later, CancellationToken.None);
        Assert.False(waitLater.IsCompleted);

        turns.Release(first);
        await waitLater.WaitAsync(Safety);
    }

    [Fact]
    public async Task Deadline_FiresAfterTheConfiguredLimit_AndFreesTheTicket()
    {
        FakeTimeProvider time = new();
        OrderedTurns turns = new();
        long blocker = turns.Take();
        long waiting = turns.Take();
        long later = turns.Take();

        using CancellationTokenSource deadline = new(PlayerService.BatchDeadline, time);
        Task wait = turns.WaitTurnAsync(waiting, deadline.Token);
        Task waitLater = turns.WaitTurnAsync(later, CancellationToken.None);

        time.Advance(PlayerService.BatchDeadline - TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.False(deadline.IsCancellationRequested);
        Assert.False(wait.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(deadline.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(Safety));

        turns.Release(blocker);
        await waitLater.WaitAsync(Safety);
    }
}
