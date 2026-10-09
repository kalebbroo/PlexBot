using Microsoft.Extensions.Time.Testing;
using PlexBot.Core.Services;
using Xunit;

namespace PlexBot.Tests;

public class ResultPagingTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(100, 4)]
    public void PageCount_RoundsUpAndIsAtLeastOne(int total, int pages)
    {
        Assert.Equal(pages, ResultPaging.PageCount(total));
    }

    [Fact]
    public void ClampPage_KeepsTheRequestInsideTheRealPages()
    {
        Assert.Equal(1, ResultPaging.ClampPage(0, 100));
        Assert.Equal(4, ResultPaging.ClampPage(9, 100));
        Assert.Equal(2, ResultPaging.ClampPage(2, 100));
    }

    [Fact]
    public void DisplayRange_ShowsTheNumbersOnTheLastPage()
    {
        Assert.Equal((1, 25), ResultPaging.DisplayRange(1, 100));
        Assert.Equal((26, 50), ResultPaging.DisplayRange(2, 100));
        Assert.Equal((76, 100), ResultPaging.DisplayRange(4, 100));
        Assert.Equal((76, 90), ResultPaging.DisplayRange(4, 90));
    }
}

public class ResultPageStoreTests
{
    [Fact]
    public void SavedList_IsReturnedUntilItExpires()
    {
        FakeTimeProvider time = new();
        ResultPageStore<string> store = new(time, TimeSpan.FromMinutes(10));
        string id = store.Save("tracks");

        time.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal("tracks", store.Get(id));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(store.Get(id));
    }

    [Fact]
    public void UnknownId_ReturnsNothing()
    {
        ResultPageStore<string> store = new(new FakeTimeProvider(), TimeSpan.FromMinutes(10));
        Assert.Null(store.Get("nope"));
    }
}
