using Microsoft.Extensions.Time.Testing;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class QueueWaitTests
{
    // Only a safety net: the waits below finish at once when the behaviour is right
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PastTheDeadline_TheWaitFailsWithAPlayerException()
    {
        FakeTimeProvider time = new();
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task wait = PlayerService.WaitWithinDeadlineAsync(ct => never.Task.WaitAsync(ct), CancellationToken.None,
            "its turn in the queue", time);

        time.Advance(PlayerService.BatchDeadline - TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.False(wait.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<PlayerException>(() => wait.WaitAsync(Safety));
    }

    [Fact]
    public async Task CallerCancellation_IsNotTurnedIntoAPlayerException()
    {
        FakeTimeProvider time = new();
        using CancellationTokenSource caller = new();
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task wait = PlayerService.WaitWithinDeadlineAsync(ct => never.Task.WaitAsync(ct), caller.Token,
            "the queue lock", time);

        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(Safety));
    }
}
