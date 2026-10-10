using Discord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Models.Players;
using PlexBot.Utils;
using SkiaSharp;
using Xunit;

namespace PlexBot.Tests;

public class StatusLineTests
{
    [Fact]
    public void StatusLine_AddsVolumeAndRepeatAsText()
    {
        string line = ComponentV2Builder.BuildPlayerStatusLine(PlayerState.Playing, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), 45, TrackRepeatMode.Queue);
        Assert.EndsWith("-# Volume 45% · Repeat all", line);
    }

}

public class CornerMaskTests
{
    [Fact]
    public void CornerMask_IsRoundedAtEveryCornerAndSolidInTheMiddle()
    {
        using SKBitmap mask = ImageBuilder.BuildCornerMask(800, 400, 24f);

        // The corners are cut off: the very corner pixel and its near neighbours sit outside the 24px arc
        Assert.Equal(0, mask.GetPixel(0, 0).Alpha);
        Assert.Equal(0, mask.GetPixel(799, 0).Alpha);
        Assert.Equal(0, mask.GetPixel(0, 399).Alpha);
        Assert.Equal(0, mask.GetPixel(799, 399).Alpha);
        Assert.Equal(0, mask.GetPixel(1, 1).Alpha);

        // Along the edges, away from the corners, the mask is solid
        Assert.Equal(255, mask.GetPixel(400, 0).Alpha);
        Assert.Equal(255, mask.GetPixel(400, 399).Alpha);
        Assert.Equal(255, mask.GetPixel(0, 200).Alpha);
        Assert.Equal(255, mask.GetPixel(400, 200).Alpha);
    }
}

public class PanelTextTests
{
    [Fact]
    public void ShowingNote_OnlyAppearsWhenTheListWasCut()
    {
        Assert.Equal(" Showing the first 25.", ComponentV2Builder.ShowingNote(30, 25));
        Assert.Equal(string.Empty, ComponentV2Builder.ShowingNote(25, 25));
        Assert.Equal(string.Empty, ComponentV2Builder.ShowingNote(3, 25));
    }

}
