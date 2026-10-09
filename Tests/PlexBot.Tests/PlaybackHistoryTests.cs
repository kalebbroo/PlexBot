using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class PlaybackHistoryTests
{
    [Fact]
    public void Back_ReturnsTheTrackBeforeTheCurrentOne()
    {
        PlaybackHistory<string> history = new(20);
        history.OnStarted("A");
        history.OnStarted("B");

        Assert.Equal("A", history.TakePrevious());
        Assert.Null(history.TakePrevious());
    }

    [Fact]
    public void Back_DoesNotRecordTheTrackItReplaces_SoASecondBackGoesFurther()
    {
        PlaybackHistory<string> history = new(20);
        history.OnStarted("A");
        history.OnStarted("B");
        history.OnStarted("C");

        Assert.Equal("B", history.TakePrevious());
        history.OnStarted("B");                       // the start Back causes: C is queued again, not recorded
        Assert.Equal("A", history.TakePrevious());
    }

    [Fact]
    public void History_KeepsOnlyTheMostRecentTracks()
    {
        PlaybackHistory<string> history = new(2);
        foreach (string track in new[] { "A", "B", "C", "D" })
            history.OnStarted(track);

        Assert.Equal(2, history.Count);
        Assert.Equal("C", history.TakePrevious());
        Assert.Equal("B", history.TakePrevious());
    }
}
