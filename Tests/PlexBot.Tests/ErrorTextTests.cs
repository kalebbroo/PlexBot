using System.Net;
using PlexBot.Core.Discord.Messages;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Services.LavaLink;
using Xunit;

namespace PlexBot.Tests;

public class ErrorTextTests
{
    private const string Fallback = "Could not finish that. Check the bot log for details.";

    [Fact]
    public void PlexBotException_ShowsItsUserMessage()
    {
        var ex = new PlexBotException("technical detail", "Shown to the user");
        Assert.Equal("Shown to the user", ErrorText.BodyFor(ex, Fallback));
    }

    [Fact]
    public void PlayerException_ShowsItsExplicitUserMessage()
    {
        var ex = new PlayerException("Queue add failed", "Connect", Notices.NotInVoice.Body);
        Assert.Equal(Notices.NotInVoice.Body, ErrorText.BodyFor(ex, Fallback));
    }

    [Fact]
    public void PlainException_GetsTheFallback_NotItsMessage()
    {
        var ex = new InvalidOperationException("request to https://plex.local/library?X-Plex-Token=secret failed");
        string body = ErrorText.BodyFor(ex, Fallback);
        Assert.Equal(Fallback, body);
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public void NoException_GetsTheFallback()
    {
        Assert.Equal(Fallback, ErrorText.BodyFor(null, Fallback));
    }

    [Fact]
    public void PlexBotException_WithEmptyUserMessage_GetsTheFallback()
    {
        var ex = new PlexBotException("technical detail", "");
        Assert.Equal(Fallback, ErrorText.BodyFor(ex, Fallback));
    }

    [Fact]
    public void PlexApiException_ShowsUserMessage_NotEndpointOrResponse()
    {
        var ex = new PlexApiException("Request failed", HttpStatusCode.Unauthorized,
            "https://plex.local/library?X-Plex-Token=secret", "{\"token\":\"secret\"}");

        string body = ErrorText.BodyFor(ex, Fallback);

        Assert.Contains("Plex token", body);
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("plex.local", body);
    }

    [Fact]
    public void PlexApiException_WithUserMessage_ShowsIt()
    {
        var ex = new PlexApiException("Invalid playlist details response",
            "Plex sent back playlist details that PlexBot could not read. Check the bot log for details.");
        Assert.Equal("Plex sent back playlist details that PlexBot could not read. Check the bot log for details.",
            ErrorText.BodyFor(ex, Fallback));
    }

    [Fact]
    public void TrackLoadFailure_LinksToTheTroubleshootingSection()
    {
        var failure = new TrackResolution(null, LoadOutcome.NotFound);
        Assert.Contains(DocsLinks.TrackLoadFailures, failure.FailureReason);
    }
}
