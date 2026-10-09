using PlexBot.Core.Discord.Help;
using Xunit;

namespace PlexBot.Tests;

public class HelpTopicTests
{
    [Fact]
    public void EveryBody_FitsInOneTextDisplay()
    {
        // Discord limits a text display to 4000 characters; a longer topic would fail to post
        Assert.All(HelpTopics.All, topic => Assert.True(topic.Body.Length <= 4000, $"{topic.Key} is {topic.Body.Length} characters"));
    }

}
