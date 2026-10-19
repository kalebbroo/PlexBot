using PlexBot.Core.Discord.Messages;
using Xunit;

namespace PlexBot.Tests;

/// <summary>Pins the shared wording in Notices. A change to a notice changes what every user sees, so the test changes with it.</summary>
public class NoticeTests
{
    [Fact]
    public void NotInVoice_SaysWhatToDo()
    {
        Assert.Equal("Not In Voice", Notices.NotInVoice.Title);
        Assert.Equal("Join a voice channel in this server, then try again.", Notices.NotInVoice.Body);
    }

    [Fact]
    public void Cooldown_Wording()
    {
        Assert.Equal("Cooldown", Notices.Cooldown.Title);
        Assert.Equal("Please wait a moment before clicking again.", Notices.Cooldown.Body);
    }

    [Fact]
    public void NoPlayer_Wording()
    {
        Assert.Equal("No Player", Notices.NoPlayer.Title);
        Assert.Equal("No active player found.", Notices.NoPlayer.Body);
    }

    [Fact]
    public void NoTrack_Wording()
    {
        Assert.Equal("No Track", Notices.NoTrack.Title);
        Assert.Equal("No track is currently playing.", Notices.NoTrack.Body);
    }

    [Fact]
    public void NoRadioTracks_Wording()
    {
        Assert.Equal("No Tracks", Notices.NoRadioTracks.Title);
        Assert.Equal("No radio tracks were returned. This track may not have sonic analysis data.", Notices.NoRadioTracks.Body);
    }
}
