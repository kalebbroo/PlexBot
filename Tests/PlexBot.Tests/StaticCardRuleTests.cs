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

    [Fact]
    public void ResolveCardChannel_IsTheStaticChannelForAStaticCard()
    {
        Assert.Equal((ulong?)100, VisualPlayerStateManager.ResolveCardChannel(true, 100, 200));
    }

    [Fact]
    public void ResolveCardChannel_IsNullForAStaticCardWithoutAnId()
    {
        // A static card never falls back to the channel a /play moved to
        Assert.Null(VisualPlayerStateManager.ResolveCardChannel(true, 0, 200));
        Assert.Null(VisualPlayerStateManager.ResolveCardChannel(true, null, 200));
    }

    [Fact]
    public void ResolveCardChannel_IsTheLastChannelForACardThatIsNotStatic()
    {
        Assert.Equal((ulong?)200, VisualPlayerStateManager.ResolveCardChannel(false, 100, 200));
        Assert.Null(VisualPlayerStateManager.ResolveCardChannel(false, 100, null));
    }

    [Fact]
    public void CardChannelFor_IsTheMessageChannelWhenThereIsOne()
    {
        Assert.Equal((ulong?)300, VisualPlayerStateManager.CardChannelFor(300, 100));
        Assert.Equal((ulong?)200, VisualPlayerStateManager.CardChannelFor(200, null));
    }

    [Fact]
    public void CardChannelFor_IsTheStaticChannelForANullMessage()
    {
        // A lost card is recreated in the static channel, not in the channel a /play moved GetChannel to
        Assert.Equal((ulong?)100, VisualPlayerStateManager.CardChannelFor(null, 100));
    }

    [Fact]
    public void CardChannelFor_IsNullForANullMessageWithoutAStaticChannel()
    {
        Assert.Null(VisualPlayerStateManager.CardChannelFor(null, null));
        // BotConfig.GetULong defaults to 0, so an unset id arrives as 0 rather than null
        Assert.Null(VisualPlayerStateManager.CardChannelFor(null, 0));
    }

    [Fact]
    public void ParseStaticChannelIds_ReadsTheSingleIdThenTheList()
    {
        Assert.Equal(new ulong[] { 100, 200, 300 }, VisualPlayerStateManager.ParseStaticChannelIds(100, "200, 300"));
    }

    [Fact]
    public void ParseStaticChannelIds_IsEmptyWhenNothingIsSet()
    {
        Assert.Empty(VisualPlayerStateManager.ParseStaticChannelIds(0, ""));
    }

    [Fact]
    public void ParseStaticChannelIds_SkipsZeroDuplicateAndNonNumericEntries()
    {
        Assert.Equal(new ulong[] { 100, 200 }, VisualPlayerStateManager.ParseStaticChannelIds(100, "0, 100, abc, 200,"));
    }
}
