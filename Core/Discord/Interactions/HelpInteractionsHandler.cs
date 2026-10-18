using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Help;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>The help menu and its pages. Each page replaces the one before in place, so /help stays one ephemeral message per user.</summary>
public class HelpInteractionsHandler(EmojiRegistry emojis) : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>A page was opened from the menu, or from the Previous and Next buttons</summary>
    [ComponentInteraction("help:page:*")]
    public async Task HandlePageAsync(string key)
    {
        await DeferAsync();
        HelpTopic? topic = HelpTopics.Find(key);
        if (topic is null)
        {
            await ShowAsync(ComponentV2Builder.BuildHelp(emojis), null);
            return;
        }
        FileAttachment? image = ImageFor(topic);
        await ShowAsync(ComponentV2Builder.BuildHelpTopic(topic, emojis, image is not null), image);
    }

    /// <summary>Back to the menu</summary>
    [ComponentInteraction(HelpTopics.HubId)]
    public async Task HandleHubAsync()
    {
        await DeferAsync();
        await ShowAsync(ComponentV2Builder.BuildHelp(emojis), null);
    }

    /// <summary>The page's screenshot, or null when it isn't installed; the page then shows only the text</summary>
    private static FileAttachment? ImageFor(HelpTopic topic)
    {
        if (topic.ImageFile is null)
            return null;
        string? path = AssetPaths.FindFile("Images", "Help", topic.ImageFile);
        if (path is null)
            Logs.Warning($"Help image not found: {topic.ImageFile}");
        return path is null ? null : new FileAttachment(path, topic.ImageFile);
    }

    private async Task ShowAsync(MessageComponent components, FileAttachment? image)
    {
        await Context.Interaction.ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Embed = null;
            msg.Attachments = image is null ? new List<FileAttachment>() : new[] { image.Value };
            msg.Flags = MessageFlags.ComponentsV2;
        });
    }
}
