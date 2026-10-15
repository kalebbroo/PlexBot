using PlexBot.Core.Models.Players;
using Xunit;

namespace PlexBot.Tests;

public class StaticCardRuleTests
{
    [Fact]
    public void IsStaticCard_IsTrueOnlyForTheConfiguredChannelWithTheFlagOn()
    {
        Assert.True(VisualPlayerStateManager.IsStaticCard(true, 100, 100));
    }

    [Fact]
    public void IsStaticCard_IsFalseForAnotherChannel()
    {
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, 100, 200));
    }

    [Fact]
    public void IsStaticCard_IsFalseWhenThereIsNoCardChannel()
    {
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, 100, null));
    }

    [Fact]
    public void IsStaticCard_IsFalseWhenTheFlagIsOff()
    {
        Assert.False(VisualPlayerStateManager.IsStaticCard(false, 100, 100));
    }

    [Fact]
    public void IsStaticCard_IsFalseWhenTheConfiguredIdIsUnset()
    {
        // BotConfig.GetULong defaults to 0, so an unset id arrives as 0 rather than null
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, null, 100));
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, 0, 100));
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, 0, 0));
        Assert.False(VisualPlayerStateManager.IsStaticCard(true, null, null));
    }

    [Fact]
    public void HasStaticChannel_IsFalseWhenTheIdIsNullOrZero()
    {
        Assert.False(VisualPlayerStateManager.HasStaticChannel(null));
        Assert.False(VisualPlayerStateManager.HasStaticChannel(0));
    }

    [Fact]
    public void HasStaticChannel_IsTrueForAConfiguredId()
    {
        Assert.True(VisualPlayerStateManager.HasStaticChannel(100));
    }
}
