using System.Diagnostics;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class PlexLoadRetryPolicyTests
{
    [Fact]
    public void ParseDelays_ReadsCommaSeparatedSeconds()
    {
        List<TimeSpan> delays = PlexLoadRetryPolicy.ParseDelays("2, 5, 15");
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], delays);
    }

    [Fact]
    public void ParseDelays_SkipsInvalidAndNegativeEntries()
    {
        List<TimeSpan> delays = PlexLoadRetryPolicy.ParseDelays("1.5, abc, -3, 4");
        Assert.Equal([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4)], delays);
    }

    [Fact]
    public void EmptySchedule_FallsBackToDefaults()
    {
        PlexLoadRetryPolicy policy = new(PlexLoadRetryPolicy.ParseDelays(""));
        Assert.Equal(PlexLoadRetryPolicy.DefaultDelays, policy.Delays);
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

public class PlexStreamGateTests
{
    [Fact]
    public async Task RunAsync_NeverExceedsTheLimit()
    {
        PlexStreamGate gate = new(2);
        int running = 0, peak = 0;

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
    public async Task Cooldown_HoldsBackOtherCallers()
    {
        PlexStreamGate gate = new(4);
        gate.Cooldown(TimeSpan.FromMilliseconds(300));

        Stopwatch sw = Stopwatch.StartNew();
        await gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds >= 250, $"waited only {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Cooldown_KeepsTheLongerOfTwo()
    {
        PlexStreamGate gate = new(1);
        gate.Cooldown(TimeSpan.FromSeconds(10));
        gate.Cooldown(TimeSpan.FromSeconds(1));
        Assert.True(gate.CooldownRemaining > TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Limit_IsAtLeastOne()
    {
        Assert.Equal(1, new PlexStreamGate(0).MaxConcurrentLoads);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}

public class ResolveCacheKeyTests
{
    private const string Url = "http://plex.local:32400/library/parts/25631/1659312000/file.mp3?X-Plex-Token=secret123";

    [Fact]
    public void CacheKey_UsesPartKeyWithoutToken()
    {
        Track track = new() { PlaybackUrl = Url, PartKey = "/library/parts/25631/1659312000/file.mp3" };
        string key = TrackResolverService.CacheKey(track);
        Assert.Equal("plex:/library/parts/25631/1659312000/file.mp3", key);
        Assert.DoesNotContain("secret123", key);
    }

    [Fact]
    public void CacheKey_StripsTokenWhenPartKeyIsMissing()
    {
        Track track = new() { PlaybackUrl = Url };
        string key = TrackResolverService.CacheKey(track);
        Assert.DoesNotContain("X-Plex-Token", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret123", key);
    }

    [Fact]
    public void StripToken_KeepsOtherQueryParameters()
    {
        Assert.Equal("http://h/a?b=1", TrackResolverService.StripToken("http://h/a?b=1&X-Plex-Token=t"));
    }

    [Fact]
    public void PartId_ReadsThePartNumberOnly()
    {
        Assert.Equal("part 25631", TrackResolverService.PartId(new Track { PlaybackUrl = Url }));
        Assert.Equal("no part id", TrackResolverService.PartId(new Track { PlaybackUrl = "https://youtube.com/watch?v=x", SourceSystem = "youtube" }));
    }
}
