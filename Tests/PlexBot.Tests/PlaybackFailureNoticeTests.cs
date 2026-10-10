using Microsoft.Extensions.Time.Testing;
using PlexBot.Core.Models.Players;
using Xunit;

namespace PlexBot.Tests;

public class PlaybackFailureNoticeTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    [Fact]
    public void NoEarlierNotice_IsAllowed()
    {
        FakeTimeProvider time = new();
        Assert.True(VisualPlayerStateManager.ShouldPostFailureNotice(null, time.GetUtcNow(), Window));
    }

    [Fact]
    public void NoticeWithinTheWindow_IsSuppressed()
    {
        FakeTimeProvider time = new();
        DateTimeOffset first = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(VisualPlayerStateManager.ShouldPostFailureNotice(first, time.GetUtcNow(), Window));
    }

    [Fact]
    public void NoticeAfterTheWindow_IsAllowed()
    {
        FakeTimeProvider time = new();
        DateTimeOffset first = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(VisualPlayerStateManager.ShouldPostFailureNotice(first, time.GetUtcNow(), Window));
    }

    [Fact]
    public void NoticeExactlyOneWindowLater_IsAllowed()
    {
        FakeTimeProvider time = new();
        DateTimeOffset first = time.GetUtcNow();
        time.Advance(Window);
        Assert.True(VisualPlayerStateManager.ShouldPostFailureNotice(first, time.GetUtcNow(), Window));
    }
}
