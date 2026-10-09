using Discord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Models.Players;
using PlexBot.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PlexBot.Tests;

public class PlayerButtonTests
{
    [Fact]
    public void PauseLook_ShowsResumeOnlyWhilePaused()
    {
        var pause = DiscordButtonBuilder.PauseLook(false);
        Assert.Equal("Pause", pause.Label);
        Assert.Equal("pb_pause", pause.EmojiName);
        Assert.Equal("pause_resume:pause", pause.Action);
        Assert.Equal("Resume", DiscordButtonBuilder.PauseLook(true).Label);
        Assert.Equal("pause_resume:resume", DiscordButtonBuilder.PauseLook(true).Action);
    }

    [Theory]
    [InlineData(TrackRepeatMode.None, "Repeat", ButtonStyle.Secondary)]
    [InlineData(TrackRepeatMode.Track, "Repeat 1", ButtonStyle.Primary)]
    [InlineData(TrackRepeatMode.Queue, "Repeat All", ButtonStyle.Primary)]
    public void RepeatLook_LabelsEachModeAndHighlightsActiveOnes(TrackRepeatMode mode, string label, ButtonStyle style)
    {
        var look = DiscordButtonBuilder.RepeatLook(mode);
        Assert.Equal(label, look.Label);
        Assert.Equal(style, look.Style);
    }

    [Fact]
    public void RepeatLook_UsesTheRepeatOneEmojiOnlyForTrackRepeat()
    {
        Assert.Equal("pb_repeat_track", DiscordButtonBuilder.RepeatLook(TrackRepeatMode.Track).EmojiName);
        Assert.Equal("pb_repeat", DiscordButtonBuilder.RepeatLook(TrackRepeatMode.Queue).EmojiName);
    }
}

public class StatusLineTests
{
    [Fact]
    public void StatusLine_AddsVolumeAndRepeatAsText()
    {
        string line = ComponentV2Builder.BuildPlayerStatusLine(PlayerState.Playing, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3), 45, TrackRepeatMode.Queue);
        Assert.EndsWith("-# Volume 45% · Repeat all", line);
    }

    [Fact]
    public void StatusLine_WithoutVolumeKeepsOnlyTheProgressBar()
    {
        string line = ComponentV2Builder.BuildPlayerStatusLine(PlayerState.Playing, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(3));
        Assert.DoesNotContain("Volume", line);
    }

    [Theory]
    [InlineData(TrackRepeatMode.None, "off")]
    [InlineData(TrackRepeatMode.Track, "one")]
    [InlineData(TrackRepeatMode.Queue, "all")]
    public void RepeatText_NamesEachMode(TrackRepeatMode mode, string text)
    {
        Assert.Equal(text, ComponentV2Builder.RepeatText(mode));
    }
}

public class CornerMaskTests
{
    [Fact]
    public void CornerMask_IsRoundedAtEveryCornerAndSolidInTheMiddle()
    {
        using Image<Rgba32> mask = ImageBuilder.BuildCornerMask(800, 400, 24f);

        // The corners are cut off: the very corner pixel and its near neighbours sit outside the 24px arc
        Assert.Equal(0, mask[0, 0].A);
        Assert.Equal(0, mask[799, 0].A);
        Assert.Equal(0, mask[0, 399].A);
        Assert.Equal(0, mask[799, 399].A);
        Assert.Equal(0, mask[1, 1].A);

        // Along the edges, away from the corners, the mask is solid
        Assert.Equal(255, mask[400, 0].A);
        Assert.Equal(255, mask[400, 399].A);
        Assert.Equal(255, mask[0, 200].A);
        Assert.Equal(255, mask[400, 200].A);
    }
}
