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
            .WithTextDisplay(statusLine);
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
            .WithTextDisplay(statusLine);
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

    // Left cap emoji: 8 fill levels (empty → 1-6 → filled)
    private static readonly string[] LeftCapLevels =
    [
        "<:bar_left_empty:1478623138618150993>",
        "<:bar_left_filled_1:1478623139972780063>",
        "<:bar_left_filled_2:1478623140966957197>",
        "<:bar_left_filled_3:1478623141906350140>",
        "<:bar_left_filled_4:1478623142778896474>",
        "<:bar_left_filled_5:1478623143579877417>",
        "<:bar_left_filled_6:1478623144330661888>",
        "<:bar_left_filled:1478623139398418613>",
    ];

    // Middle segment emoji: 14 fill levels (empty → 1-12 → filled)
    private static readonly string[] MidLevels =
    [
        "<:bar_mid_empty:1478623145228505118>",
        "<:bar_filled_1:1478623122654629908>",
        "<:bar_filled_2:1478623123992612936>",
        "<:bar_filled_3:1478623124919685282>",
        "<:bar_filled_4:1478623125649358848>",
        "<:bar_filled_5:1478623126526103613>",
        "<:bar_filled_6:1478623127222091806>",
        "<:bar_filled_7:1478623128174465095>",
        "<:bar_filled_8:1478623128920785007>",
        "<:bar_filled_9:1478623135346462804>",
        "<:bar_filled_10:1478623136302764083>",
        "<:bar_filled_11:1478623137217380483>",
        "<:bar_filled_12:1478623137963704463>",
        "<:bar_mid_filled:1478623145970892861>",
    ];

    // Right cap emoji: 8 fill levels (empty → 1-6 → filled)
    private static readonly string[] RightCapLevels =
    [
        "<:bar_right_empty:1478623147224862811>",
        "<:bar_right_filled_1:1478623149070352476>",
        "<:bar_right_filled_2:1478623149753897034>",
        "<:bar_right_filled_3:1478623150404276376>",
        "<:bar_right_filled_4:1478623152631320679>",
        "<:bar_right_filled_5:1478623153709383762>",
        "<:bar_right_filled_6:1478623154321752144>",
        "<:bar_right_filled:1478623147975639123>",
    ];

    /// <summary>Builds a player status line showing the progress bar (volume/repeat are on the image)</summary>
    public static string BuildPlayerStatusLine(
        PlayerState state = PlayerState.NotPlaying,
        TimeSpan? position = null, TimeSpan? duration = null)
    {
        string progressLine = BuildProgressBar(state, position, duration);
        return progressLine;
    }

    /// <summary>Builds a smooth-fill progress bar using custom emoji with partial fill levels</summary>
    private static string BuildProgressBar(PlayerState state, TimeSpan? position, TimeSpan? duration)
    {
        const int middleSegments = 14;
        const int totalSegments = middleSegments + 2; // left cap + middle + right cap

        if (position == null || duration == null || duration.Value.TotalSeconds < 1)
        {
            string emptyBar = LeftCapLevels[0]
                + string.Concat(Enumerable.Repeat(MidLevels[0], middleSegments))
                + RightCapLevels[0];
            return $"` 0:00 `{emptyBar}` 0:00 `";
        }

        double progress = Math.Clamp(position.Value.TotalSeconds / duration.Value.TotalSeconds, 0, 1);

        // Map progress to a continuous position across all segments (0.0 to totalSegments)
        double fillPosition = progress * totalSegments;
        int activeSegment = (int)fillPosition;

        var bar = new System.Text.StringBuilder();
        for (int i = 0; i < totalSegments; i++)
        {
            // Pick the right emoji array for this segment
            string[] levels = i == 0 ? LeftCapLevels
                : i == totalSegments - 1 ? RightCapLevels
                : MidLevels;

            int maxLevel = levels.Length - 1;

            if (i < activeSegment)
            {
                // Fully filled — progress has passed this segment
                bar.Append(levels[maxLevel]);
            }
            else if (i == activeSegment)
            {
                // Active segment — partially filled based on fractional position
                double fraction = fillPosition - activeSegment;
                int level = (int)Math.Round(fraction * maxLevel);
                bar.Append(levels[Math.Clamp(level, 0, maxLevel)]);
            }
            else
            {
                // Empty — progress hasn't reached this segment
                bar.Append(levels[0]);
            }
        }

        string posStr = FormatTime(position.Value);
        string durStr = FormatTime(duration.Value);
        return $"` {posStr} `{bar}` {durStr} `";
    }

    /// <summary>Formats a TimeSpan as m:ss or h:mm:ss</summary>
    private static string FormatTime(TimeSpan ts)
    {
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
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
