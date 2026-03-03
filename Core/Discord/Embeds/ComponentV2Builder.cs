using PlexBot.Utils;

using Color = Discord.Color;

namespace PlexBot.Core.Discord.Embeds;

/// <summary>Utility for creating standardized Components V2 layouts for Discord messages</summary>
public static class ComponentV2Builder
{
    private static readonly Color SuccessColor = new(0, 255, 127);
    private static readonly Color ErrorColor = new(255, 69, 0);
    private static readonly Color InfoColor = new(30, 144, 255);
    private static readonly Color WarningColor = new(255, 215, 0);
    private static readonly Color MusicColor = new(138, 43, 226);

    private const string SuccessEmoji = "\u2705";
    private const string ErrorEmoji = "\u274C";
    private const string InfoEmoji = "\u2139\uFE0F";
    private const string WarningEmoji = "\u26A0\uFE0F";

    /// <summary>Creates a success status message</summary>
    public static MessageComponent Success(string title, string description)
        => BuildStatusMessage(SuccessColor, SuccessEmoji, title, description);

    /// <summary>Creates an error status message</summary>
    public static MessageComponent Error(string title, string description)
        => BuildStatusMessage(ErrorColor, ErrorEmoji, title, description);

    /// <summary>Creates an info status message</summary>
    public static MessageComponent Info(string title, string description)
        => BuildStatusMessage(InfoColor, InfoEmoji, title, description);

    /// <summary>Creates a warning status message</summary>
    public static MessageComponent Warning(string title, string description)
        => BuildStatusMessage(WarningColor, WarningEmoji, title, description);

    /// <summary>Creates a command error message matching the existing error type handling</summary>
    public static MessageComponent CommandError(InteractionCommandError? errorType, string errorReason)
    {
        string title;
        string description;
        if (errorType.HasValue)
        {
            switch (errorType.Value)
            {
                case InteractionCommandError.UnmetPrecondition:
                    title = "Permission Denied";
                    description = $"You don't have permission to use this command: {errorReason}";
                    break;
                case InteractionCommandError.UnknownCommand:
                    title = "Unknown Command";
                    description = "This command is not recognized. It may have been removed or updated.";
                    break;
                case InteractionCommandError.BadArgs:
                    title = "Invalid Arguments";
                    description = "The command arguments were invalid. Please check your input and try again.";
                    break;
                case InteractionCommandError.Exception:
                    title = "Command Error";
                    description = "An error occurred while processing your command. Please try again later.";
                    Logs.Error($"Command exception: {errorReason}");
                    break;
                default:
                    title = "Unknown Error";
                    description = "An unknown error occurred. Please try again later.";
                    break;
            }
        }
        else
        {
            title = "Unknown Error";
            description = "An unknown error occurred. Please try again later.";
        }
        return Error(title, description);
    }

    /// <summary>Creates an info message with additional interactive components (select menus, buttons)</summary>
    public static MessageComponent InfoWithComponents(string title, string description, ComponentBuilder additionalComponents)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(InfoColor)
            .WithTextDisplay($"## {InfoEmoji} {title}\n{description}")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, additionalComponents);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the modern visual player layout with the ImageSharp-generated image</summary>
    public static MessageComponent BuildModernPlayer(string statusLine, ComponentBuilder buttons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(MusicColor)
            .WithMediaGallery(new MediaGalleryBuilder().AddItem("attachment://playerImage.png"))
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
            .WithTextDisplay($"-# {statusLine}");
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the classic visual player layout with a thumbnail accessory</summary>
    public static MessageComponent BuildClassicPlayer(
        string trackInfo, string? artworkUrl, string statusLine, ComponentBuilder buttons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(MusicColor);

        if (!string.IsNullOrEmpty(artworkUrl))
        {
            container.WithSection(new SectionBuilder()
                .AddComponent(new TextDisplayBuilder(trackInfo))
                .WithAccessory(new ThumbnailBuilder()
                    .WithMedia(new UnfurledMediaItemProperties(artworkUrl))));
        }
        else
        {
            container.WithTextDisplay(trackInfo);
        }

        container.WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
            .WithTextDisplay($"-# {statusLine}");
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds search results layout with select menus inside the container</summary>
    public static MessageComponent BuildSearchResults(string query, string summary, ComponentBuilder selectMenus)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(InfoColor)
            .WithTextDisplay($"## \U0001F50D Search Results for: {query}")
            .WithTextDisplay(summary)
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, selectMenus);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the queue display layout with pagination</summary>
    public static MessageComponent BuildQueueDisplay(
        string? nowPlayingLine, string queueText, string footerLine, ComponentBuilder paginationButtons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(MusicColor)
            .WithTextDisplay("## \U0001F4CB Current Music Queue");

        if (!string.IsNullOrEmpty(nowPlayingLine))
            container.WithTextDisplay(nowPlayingLine);

        container.WithSeparator(SeparatorSpacingSize.Small, isDivider: true);

        if (!string.IsNullOrEmpty(queueText))
            container.WithTextDisplay(queueText);

        container.WithTextDisplay($"-# {footerLine}");
        AddActionRows(container, paginationButtons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the help command display</summary>
    public static MessageComponent BuildHelp()
    {
        return new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder()
                .WithAccentColor(InfoColor)
                .WithTextDisplay("## \U0001F4FB PlexBot Music Player")
                .WithTextDisplay("Play music from your Plex library directly in Discord voice channels.")
                .WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
                .WithTextDisplay(
                    "**`/search [query] [source]`**\nSearch for music in your Plex library or other sources.\n\n" +
                    "**`/playlist [playlist] [shuffle]`**\nPlay a Plex playlist, optionally shuffled.\n\n" +
                    "**`/play [query]`**\nQuickly play music that matches your search.")
                .WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
                .WithTextDisplay(
                    "-# **Player Controls:** Use the buttons on the player to Pause, Skip, view Queue, set Repeat, adjust Volume, or Kill playback.")
            ).Build();
    }

    /// <summary>Builds the idle player display for static channel initialization</summary>
    public static MessageComponent BuildIdlePlayer(ComponentBuilder buttons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(MusicColor)
            .WithTextDisplay("## \U0001F3B5 PlexBot Music Player")
            .WithTextDisplay("No track is currently playing. Use `/play` to start!")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
            .WithTextDisplay("-# The player will appear here when music begins playing");
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds a player status line from current player state</summary>
    public static string BuildPlayerStatusLine(float volume, TrackRepeatMode repeatMode)
    {
        string repeatStr = repeatMode switch
        {
            TrackRepeatMode.Track => "\U0001F502 Track",
            TrackRepeatMode.Queue => "\U0001F501 Queue",
            _ => "Repeat: Off"
        };
        return $"\U0001F50A Volume: {volume * 100:F0}% | {repeatStr}";
    }

    private static MessageComponent BuildStatusMessage(Color color, string emoji, string title, string description)
    {
        return new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder()
                .WithAccentColor(color)
                .WithTextDisplay($"## {emoji} {title}\n{description}")
            ).Build();
    }

    private static void AddActionRows(ContainerBuilder container, ComponentBuilder? components)
    {
        if (components?.ActionRows == null) return;
        foreach (ActionRowBuilder row in components.ActionRows)
            container.AddComponent(row);
    }
}
