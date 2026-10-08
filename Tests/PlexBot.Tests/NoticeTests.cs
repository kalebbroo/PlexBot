using PlexBot.Core.Discord.Messages;
using Xunit;

namespace PlexBot.Tests;

public class NoticeTests
{
    [Fact]
    public void EveryNotice_HasATitleAndBody()
    {
        foreach (Notice notice in new[] { Notices.Cooldown, Notices.NoPlayer, Notices.NoTrack, Notices.NoRadioTracks })
        {
            Assert.False(string.IsNullOrWhiteSpace(notice.Title));
            Assert.False(string.IsNullOrWhiteSpace(notice.Body));
        }
    }

    [Fact]
    public void SharedWording_MatchesWhatUsersAlreadySee()
    {
        // Changing these strings changes what every user sees, so a change should be deliberate
        Assert.Equal(new Notice("Cooldown", "Please wait a moment before clicking again."), Notices.Cooldown);
        Assert.Equal(new Notice("No Player", "No active player found."), Notices.NoPlayer);
        Assert.Equal(new Notice("No Track", "No track is currently playing."), Notices.NoTrack);
        Assert.Equal(new Notice("No Tracks", "No radio tracks were returned. This track may not have sonic analysis data."), Notices.NoRadioTracks);
    }
}
