using PlexBot.Core.Events;
using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class PlayerTeardownTests
{
    [Fact]
    public async Task PlayerDestroyed_IsPublished_WhenStopThrows()
    {
        BotEventBus bus = new();
        TaskCompletionSource<ulong> destroyed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.Subscribe(BotEvents.PlayerDestroyed, e =>
        {
            destroyed.TrySetResult((ulong)e.Data["guildId"]);
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => PlayerTeardown.RunAsync(
            () => throw new InvalidOperationException("Cannot access a disposed object"),
            () => bus.PublishPlayerDestroyed(42)));

        Assert.Equal(42UL, await destroyed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Announcement_RunsAfterDisconnectThrows_AndTheException_Propagates()
    {
        List<string> steps = [];
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlayerTeardown.RunAsync(
            async () =>
            {
                steps.Add("stop");
                await Task.CompletedTask;
                throw new InvalidOperationException("Voice connection already closed");
            },
            () => steps.Add("announced")));

        Assert.Equal(new[] { "stop", "announced" }, steps);
    }

    [Fact]
    public async Task Announcement_FollowsStopAndDisconnect_OnSuccess()
    {
        List<string> steps = [];
        await PlayerTeardown.RunAsync(
            async () =>
            {
                steps.Add("stop");
                await Task.CompletedTask;
                steps.Add("disconnect");
            },
            () => steps.Add("announced"));

        Assert.Equal(new[] { "stop", "disconnect", "announced" }, steps);
    }
}
