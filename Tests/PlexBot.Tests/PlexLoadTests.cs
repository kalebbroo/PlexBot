using Lavalink4NET.Players;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Time.Testing;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;
using Xunit;

namespace PlexBot.Tests;

public class PlexStreamOptionsTests
{
    [Fact]
    public void ParseDelays_ReadsCommaSeparatedSeconds()
    {
        List<TimeSpan> delays = PlexStreamOptions.ParseDelays("2, 5, 15");
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], delays);
    }

    [Fact]
    public void ParseDelays_SkipsInvalidAndNegativeEntries()
    {
        List<TimeSpan> delays = PlexStreamOptions.ParseDelays("1.5, abc, -3, 4");
        Assert.Equal([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4)], delays);
    }
}

public class PlexLoadRetryPolicyTests
{
    [Fact]
    public void EmptySchedule_FallsBackToDefaults()
    {
        PlexLoadRetryPolicy policy = new(PlexStreamOptions.ParseDelays(string.Empty));
        Assert.Equal(PlexStreamOptions.DefaultRetryDelays, policy.Delays);
        Assert.Equal(4, policy.MaxAttempts);
    }

    [Fact]
    public void DelayBefore_UsesScheduleAndClampsPastTheEnd()
    {
        PlexLoadRetryPolicy policy = new([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)]);
        Assert.Equal(TimeSpan.FromSeconds(2), policy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBefore(9));
    }

    [Theory]
    [InlineData(true, false, false, LoadOutcome.Loaded)]
    [InlineData(false, true, false, LoadOutcome.Retriable)]   // Lavalink fault: Plex dropped the response
    [InlineData(false, false, true, LoadOutcome.Retriable)]   // load timed out
    [InlineData(false, false, false, LoadOutcome.NotFound)]   // no match: retrying won't help
    public void Classify_SeparatesRetriableFromPermanent(bool hasTrack, bool isError, bool timedOut, LoadOutcome expected)
    {
        Assert.Equal(expected, PlexLoadRetryPolicy.Classify(hasTrack, isError, timedOut));
    }
}

public class TrackResolutionTests
{
    [Fact]
    public void FailureReason_DistinguishesMissingFileFromExhaustedRetries()
    {
        Assert.Contains("couldn't find", new TrackResolution(null, LoadOutcome.NotFound).FailureReason);
        Assert.Contains("several tries", new TrackResolution(null, LoadOutcome.Retriable).FailureReason);
        Assert.False(TrackResolution.Failed.IsLoaded);
    }
}

public class PlexStreamGateTests
{
    [Fact]
    public async Task RunAsync_NeverExceedsTheLimit()
    {
        PlexStreamGate gate = new(2, TimeProvider.System);
        int running = 0;
        int peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => gate.RunAsync(async ct =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(30, ct);
            Interlocked.Decrement(ref running);
            return 0;
        }, CancellationToken.None)));

        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task Cooldown_HoldsBackCallersUntilItEnds()
    {
        FakeTimeProvider time = new();
        PlexStreamGate gate = new(4, time);
        gate.Cooldown(TimeSpan.FromSeconds(10));

        Task<int> load = gate.RunAsync(_ => Task.FromResult(1), CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(9));
        Assert.False(load.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await load.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Cooldown_KeepsTheLongerOfTwo()
    {
        FakeTimeProvider time = new();
        PlexStreamGate gate = new(1, time);
        gate.Cooldown(TimeSpan.FromSeconds(10));
        gate.Cooldown(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(10), gate.CooldownRemaining);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.Zero, gate.CooldownRemaining);
    }

    [Fact]
    public async Task RunAsync_StopsWaitingWhenCancelled()
    {
        PlexStreamGate gate = new(1, new FakeTimeProvider());
        gate.Cooldown(TimeSpan.FromMinutes(1));
        using CancellationTokenSource cts = new();

        Task<int> load = gate.RunAsync(_ => Task.FromResult(1), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    [Fact]
    public void Limit_IsAtLeastOne()
    {
        Assert.Equal(1, new PlexStreamGate(0, TimeProvider.System).MaxConcurrentLoads);
    }

    public static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}

public class PlexUrlTests
{
    public const string Url = "http://plex.local:32400/library/parts/25631/1659312000/file.mp3?X-Plex-Token=secret123";

    [Fact]
    public void CacheKey_UsesPartKeyWithoutToken()
    {
        Track track = new() { SourceSystem = "plex", PlaybackUrl = Url, PartKey = "/library/parts/25631/1659312000/file.mp3" };
        string key = TrackResolverService.CacheKey(track);
        Assert.Equal("plex:/library/parts/25631/1659312000/file.mp3", key);
        Assert.DoesNotContain("secret123", key);
    }

    [Fact]
    public void CacheKey_StripsTokenWhenPartKeyIsMissing()
    {
        Track track = new() { SourceSystem = "plex", PlaybackUrl = Url };
        string key = TrackResolverService.CacheKey(track);
        Assert.Equal("plex:http://plex.local:32400/library/parts/25631/1659312000/file.mp3", key);
    }

    [Fact]
    public void CacheKey_KeepsNonPlexUrlsWhole()
    {
        Track track = new() { SourceSystem = "youtube", PlaybackUrl = "https://youtube.com/watch?v=x" };
        Assert.Equal("https://youtube.com/watch?v=x", TrackResolverService.CacheKey(track));
    }

    [Theory]
    [InlineData("http://h/a?b=1&X-Plex-Token=t", "http://h/a?b=1")]
    [InlineData("http://h/a?X-Plex-Token=t&b=1", "http://h/a?b=1")]
    [InlineData("http://h/a?X-Plex-Token=t", "http://h/a")]
    [InlineData("", "")]
    public void StripToken_KeepsOtherQueryParameters(string url, string expected)
    {
        Assert.Equal(expected, PlexUrlHelper.StripToken(url));
    }

    [Fact]
    public void PartId_ReadsThePartNumberOnly()
    {
        Assert.Equal("part 25631", PlexUrlHelper.PartId(new Track { SourceSystem = "plex", PlaybackUrl = Url }));
        Assert.Equal("no part id", PlexUrlHelper.PartId(new Track { SourceSystem = "youtube", PlaybackUrl = "https://youtube.com/watch?v=x" }));
        Assert.DoesNotContain("secret123", PlexUrlHelper.Describe(new Track { Title = "Song", PlaybackUrl = Url }));
    }

    [Fact]
    public void IsPlex_IgnoresCase()
    {
        Assert.True(new Track { SourceSystem = "Plex" }.IsPlex);
        Assert.False(new Track { SourceSystem = "youtube" }.IsPlex);
    }
}

public class ResolveWindowTests
{
    public static CustomTrackQueueItem Placeholder(string title) =>
        CustomTrackQueueItem.Placeholder(new Track { Title = title, SourceSystem = "plex", PlaybackUrl = $"http://h/library/parts/{title.Length}/1/f.mp3" }, "tester");

    public static CustomTrackQueueItem Resolved(string title)
    {
        CustomTrackQueueItem item = Placeholder(title);
        item.Reference = new TrackReference(new LavalinkTrack { Identifier = title, Title = title, Author = "a", SourceName = "http" });
        return item;
    }

    [Fact]
    public void SelectWindow_TakesOnlyUnresolvedItemsWithinTheWindow()
    {
        CustomTrackQueueItem first = Placeholder("one");
        CustomTrackQueueItem done = Resolved("two");
        CustomTrackQueueItem failed = Placeholder("three");
        failed.ResolveFailed = true;
        CustomTrackQueueItem fourth = Placeholder("four");
        CustomTrackQueueItem beyond = Placeholder("five");

        List<CustomTrackQueueItem> window = QueueResolveService.SelectWindow([first, done, failed, fourth, beyond], 4);

        Assert.Equal([first, fourth], window);
    }

    [Fact]
    public void Placeholder_IsUnresolvedUntilATrackIsAttached()
    {
        Assert.False(Placeholder("x").IsResolved);
        Assert.True(Resolved("x").IsResolved);
    }
}
