using PlexBot.Core.Discord.Embeds;
using Xunit;

namespace PlexBot.Tests;

public class StaticChannelSetupTests
{
    [Fact]
    public void FirstPerServer_KeepsTheFirstConfiguredChannelOfEachServer()
    {
        var chosen = StaticChannelSetup.FirstPerServer(new (ulong, ulong)[] { (1, 10), (2, 10), (3, 20) });
        Assert.Equal(new ulong[] { 1, 3 }, chosen);
    }

    [Fact]
    public void FirstPerServer_IsEmptyForNoChannels()
    {
        Assert.Empty(StaticChannelSetup.FirstPerServer(Array.Empty<(ulong, ulong)>()));
    }
}
