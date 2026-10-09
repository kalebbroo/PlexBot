using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.Music;
using Xunit;

namespace PlexBot.Tests;

public class RadioRefillPolicyTests
{
    [Theory]
    [InlineData(true, 4, 5, false, true)]
    [InlineData(true, 5, 5, false, false)]
    [InlineData(true, 0, 5, true, false)]
    [InlineData(false, 0, 5, false, false)]
    public void ShouldRefill_OnlyWhenInfiniteLowAndNotAlreadyRunning(bool infinite, int queue, int threshold, bool running, bool expected)
    {
        Assert.Equal(expected, RadioRefillPolicy.ShouldRefill(infinite, queue, threshold, running));
    }

    [Fact]
    public void KeyOf_UsesThePlexRatingKey()
    {
        Assert.Equal("12345", RadioRefillPolicy.KeyOf(Track("/library/metadata/12345", "x")));
    }

    [Fact]
    public void Unseen_KeepsOrderAndDropsTracksAlreadyQueued()
    {
        List<Track> batch = [Track("/library/metadata/1", "a"), Track("/library/metadata/2", "b"), Track("/library/metadata/3", "c")];
        List<Track> fresh = RadioRefillPolicy.Unseen(batch, new HashSet<string> { "2" });
        Assert.Equal(["a", "c"], fresh.Select(t => t.Title));
    }

    [Fact]
    public void Unseen_DropsRepeatsWithinOneBatch()
    {
        List<Track> batch = [Track("/library/metadata/1", "a"), Track("/library/metadata/1", "a again"), Track("/library/metadata/2", "b")];
        List<Track> fresh = RadioRefillPolicy.Unseen(batch, new HashSet<string>());
        Assert.Equal(["a", "b"], fresh.Select(t => t.Title));
    }

    private static Track Track(string sourceKey, string title) => new() { SourceKey = sourceKey, Title = title, Id = title };
}

public class RadioSessionTests
{
    private static RadioSession NewSession() => new() { SeedRatingKey = "1", IsInfinite = true, StartedAt = DateTime.UtcNow };

    [Fact]
    public void RefillSlot_IsTakenOnceUntilReleased()
    {
        RadioSession session = NewSession();
        Assert.True(session.TryBeginRefill());
        Assert.True(session.Refilling);
        Assert.False(session.TryBeginRefill());

        session.EndRefill();
        Assert.False(session.Refilling);
        Assert.True(session.TryBeginRefill());
    }

    [Fact]
    public void RememberedTracks_AreNotOfferedAgain()
    {
        RadioSession session = NewSession();
        Track first = new() { SourceKey = "/library/metadata/10", Title = "first", Id = "10" };
        Track second = new() { SourceKey = "/library/metadata/11", Title = "second", Id = "11" };

        session.Remember([first]);
        List<Track> fresh = session.TakeUnseen([first, second]);

        Assert.Equal(["second"], fresh.Select(t => t.Title));
    }

    [Fact]
    public void TakeUnseen_DoesNotMarkTracksAsQueued()
    {
        RadioSession session = NewSession();
        Track track = new() { SourceKey = "/library/metadata/20", Title = "only", Id = "20" };

        Assert.Single(session.TakeUnseen([track]));
        Assert.Single(session.TakeUnseen([track]));
    }
}
