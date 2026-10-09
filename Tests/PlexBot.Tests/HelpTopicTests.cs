using PlexBot.Core.Discord.Help;
using Xunit;

namespace PlexBot.Tests;

public class HelpTopicTests
{
    [Fact]
    public void TopicKeys_AreUniqueAndNotEmpty()
    {
        Assert.All(HelpTopics.All, topic => Assert.False(string.IsNullOrWhiteSpace(topic.Key)));
        Assert.Equal(HelpTopics.All.Count, HelpTopics.All.Select(topic => topic.Key).Distinct().Count());
    }

    [Fact]
    public void EveryBody_FitsInOneTextDisplay()
    {
        // Discord limits a text display to 4000 characters; a longer topic would fail to post
        Assert.All(HelpTopics.All, topic => Assert.True(topic.Body.Length <= 4000, $"{topic.Key} is {topic.Body.Length} characters"));
    }

    [Fact]
    public void EveryTopic_HasAScreenshotNameInTheHelpFolder()
    {
        Assert.All(HelpTopics.All, topic => Assert.EndsWith(".png", topic.ImageFile));
        Assert.Equal(HelpTopics.All.Count, HelpTopics.All.Select(topic => topic.ImageFile).Distinct().Count());
    }

    [Fact]
    public void Find_ReturnsTheTopicOrNull()
    {
        Assert.Equal("Troubleshooting", HelpTopics.Find("trouble")?.Title);
        Assert.Null(HelpTopics.Find("nope"));
    }
}
