using Discord;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Help;
using Xunit;

namespace PlexBot.Tests;

public class HelpNavigationTests
{
    [Fact]
    public void FirstPage_HasNoPrevious_LastPage_HasNoNext()
    {
        Assert.Null(HelpTopics.Previous(HelpTopics.All[0]));
        Assert.Null(HelpTopics.Next(HelpTopics.All[^1]));
    }

    [Fact]
    public void MiddlePage_PreviousAndNextAreNeighbours()
    {
        HelpTopic middle = HelpTopics.All[2];
        Assert.Equal(HelpTopics.All[1], HelpTopics.Previous(middle));
        Assert.Equal(HelpTopics.All[3], HelpTopics.Next(middle));
    }

    [Fact]
    public void PositionOf_CountsFromOne()
    {
        Assert.Equal(1, HelpTopics.PositionOf(HelpTopics.All[0]));
        Assert.Equal(HelpTopics.All.Count, HelpTopics.PositionOf(HelpTopics.All[^1]));
    }

    [Fact]
    public void PageIds_AreUniqueAndLeadBackToTheirPage()
    {
        var ids = HelpTopics.All.Select(topic => HelpTopics.PageId(topic.Key)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(HelpTopics.HubId, ids);
        foreach (HelpTopic topic in HelpTopics.All)
            Assert.Same(topic, HelpTopics.Find(topic.Key));
    }

    [Fact]
    public void EveryTopic_NamesAnEmojiInTheImageFolder()
    {
        string emojiFolder = Path.Combine(FindRepositoryRoot(), "Images", "Emoji");
        foreach (HelpTopic topic in HelpTopics.All)
        {
            Assert.StartsWith(EmojiRegistry.Prefix, topic.EmojiName);
            Assert.True(File.Exists(Path.Combine(emojiFolder, topic.EmojiName + ".png")), $"{topic.EmojiName}.png is missing");
        }
    }

    [Fact]
    public void WithoutASync_EveryTopicShowsItsUnicodeFallback()
    {
        var emojis = new EmojiRegistry();
        foreach (HelpTopic topic in HelpTopics.All)
            Assert.Equal(topic.FallbackEmoji, Assert.IsType<Emoji>(emojis.Resolve(topic.EmojiName, topic.FallbackEmoji)).Name);
    }

    [Fact]
    public void TheMenuAndEveryPage_BuildWithoutAnError()
    {
        var emojis = new EmojiRegistry();
        Assert.NotNull(ComponentV2Builder.BuildHelp(emojis));
        foreach (HelpTopic topic in HelpTopics.All)
        {
            Assert.NotNull(ComponentV2Builder.BuildHelpTopic(topic, emojis, hasImage: false));
            Assert.NotNull(ComponentV2Builder.BuildHelpTopic(topic, emojis, hasImage: true));
        }
    }

    /// <summary>Walks up from the test binaries to the folder that holds the Images directory</summary>
    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "Images", "Emoji")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("Images/Emoji was not found above the test output folder");
    }
}
