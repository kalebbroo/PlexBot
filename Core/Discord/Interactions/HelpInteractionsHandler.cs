using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Help;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>The help hub. Topics open in place, so /help stays one ephemeral message per user.</summary>
public class HelpInteractionsHandler : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>A topic was chosen from the menu</summary>
    [ComponentInteraction("help:select")]
    public async Task HandleTopicSelectAsync(string[] selected)
    {
        await DeferAsync();
        HelpTopic? topic = selected.Length > 0 ? HelpTopics.Find(selected[0]) : null;
        if (topic is null)
        {
            await ShowAsync(ComponentV2Builder.BuildHelp(), null);
            return;
        }
        FileAttachment? image = ImageFor(topic);
        await ShowAsync(ComponentV2Builder.BuildHelpTopic(topic, image is not null), image);
    }

    /// <summary>Back to the topic list</summary>
    [ComponentInteraction("help:hub")]
    public async Task HandleHubAsync()
    {
        await DeferAsync();
        await ShowAsync(ComponentV2Builder.BuildHelp(), null);
    }

    /// <summary>The topic's screenshot, or null when it isn't installed; the card then shows only the text</summary>
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
