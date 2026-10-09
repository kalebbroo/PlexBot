using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Help;
using PlexBot.Core.Discord.Messages;
using PlexBot.Utils;

using Color = Discord.Color;

namespace PlexBot.Core.Discord.Embeds;

/// <summary>Utility for creating standardized Components V2 layouts for Discord messages</summary>
public static class ComponentV2Builder
{

    private const string SuccessEmoji = "\u2705";
    private const string ErrorEmoji = "\u274C";
    private const string InfoEmoji = "\u2139\uFE0F";
    private const string WarningEmoji = "\u26A0\uFE0F";

    /// <summary>Creates a success status message</summary>
    public static MessageComponent Success(string title, string description)
        => BuildStatusMessage(DesignTokens.Success, SuccessEmoji, title, description);

    /// <summary>Creates an error status message</summary>
    public static MessageComponent Error(string title, string description)
        => BuildStatusMessage(DesignTokens.Error, ErrorEmoji, title, description);

    /// <summary>Creates an info status message</summary>
    public static MessageComponent Info(string title, string description)
        => BuildStatusMessage(DesignTokens.Info, InfoEmoji, title, description);

    /// <summary>Creates a warning status message</summary>
    public static MessageComponent Warning(string title, string description)
        => BuildStatusMessage(DesignTokens.Warning, WarningEmoji, title, description);

    /// <summary>Creates a success status message from a shared notice</summary>
    public static MessageComponent Success(Notice notice) => Success(notice.Title, notice.Body);

    /// <summary>Creates an error status message from a shared notice</summary>
    public static MessageComponent Error(Notice notice) => Error(notice.Title, notice.Body);

    /// <summary>Creates an info status message from a shared notice</summary>
    public static MessageComponent Info(Notice notice) => Info(notice.Title, notice.Body);

    /// <summary>Creates a warning status message from a shared notice</summary>
    public static MessageComponent Warning(Notice notice) => Warning(notice.Title, notice.Body);

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
            .WithAccentColor(DesignTokens.Info)
            .WithTextDisplay($"## {InfoEmoji} {title}\n{description}")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, additionalComponents);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the modern visual player layout with the ImageSharp-generated image</summary>
    public static MessageComponent BuildModernPlayer(string? statusLine, ComponentBuilder buttons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithMediaGallery(new MediaGalleryBuilder().AddItem("attachment://playerImage.png"));
        if (statusLine != null)
        {
            container.WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
                .WithTextDisplay(statusLine);
        }
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the classic visual player layout with a thumbnail accessory</summary>
    public static MessageComponent BuildClassicPlayer(
        string trackInfo, string? artworkUrl, string? statusLine, ComponentBuilder buttons)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music);

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

        if (statusLine != null)
        {
            container.WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
                .WithTextDisplay(statusLine);
        }
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds search results layout with select menus inside the container</summary>
    public static MessageComponent BuildSearchResults(string query, string summary, ComponentBuilder selectMenus, string? emoji = null)
    {
        string icon = emoji ?? "\U0001F50D";
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Info)
            .WithTextDisplay($"## {icon} Search Results for: {query}")
            .WithTextDisplay(summary)
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, selectMenus);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the ephemeral queue options panel with now-playing info and action buttons</summary>
    public static MessageComponent BuildQueueOptions(
        string nowPlaying, int queueCount, ComponentBuilder actionButtons, string? emoji = null)
    {
        string icon = emoji ?? "\U0001F4CB";
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithTextDisplay($"## {icon} Queue Options")
            .WithTextDisplay($"\u25B6\uFE0F **Now Playing:** {nowPlaying}")
            .WithTextDisplay($"{queueCount} tracks in queue")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, actionButtons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds the queue display layout with pagination</summary>
    public static MessageComponent BuildQueueDisplay(
        string? nowPlayingLine, string queueText, string footerLine, ComponentBuilder paginationButtons, string? emoji = null)
    {
        string icon = emoji ?? "\U0001F4CB";
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithTextDisplay($"## {icon} Current Music Queue");

        if (!string.IsNullOrEmpty(nowPlayingLine))
            container.WithTextDisplay(nowPlayingLine);

        container.WithSeparator(SeparatorSpacingSize.Small, isDivider: true);

        if (!string.IsNullOrEmpty(queueText))
            container.WithTextDisplay(queueText);

        container.WithTextDisplay($"-# {footerLine}");
        AddActionRows(container, paginationButtons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>The help hub: an introduction and a menu of topics. Each topic opens its own card.</summary>
    public static MessageComponent BuildHelp()
    {
        ComponentBuilder topics = new();
        SelectMenuBuilder topicMenu = new SelectMenuBuilder()
            .WithCustomId("help:select")
            .WithPlaceholder("Choose a topic")
            .WithMaxValues(1);
        foreach (HelpTopic topic in HelpTopics.All)
            topicMenu.AddOption(topic.Label, topic.Key, topic.Summary, emote: new Emoji(topic.Emoji));
        topics.WithSelectMenu(topicMenu);

        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithTextDisplay("## \U0001F4FB PlexBot Music Player")
            .WithTextDisplay("Play music from your Plex library in voice channels. Pick a topic to see how it works.")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, topics);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>One help topic: its title, its screenshot when there is one, the text, and a way back to the hub</summary>
    public static MessageComponent BuildHelpTopic(HelpTopic topic, bool hasImage)
    {
        ComponentBuilder back = new();
        back.WithButton("Back to topics", "help:hub", ButtonStyle.Secondary);

        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Info)
            .WithTextDisplay($"## {topic.Emoji} {topic.Title}");
        if (hasImage && topic.ImageFile is not null)
            container.WithMediaGallery(new MediaGalleryBuilder().AddItem($"attachment://{topic.ImageFile}"));
        container
            .WithTextDisplay(topic.Body)
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, back);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Replaces a player message after its player disconnects. It has no controls, so an old player can't be used.</summary>
    public static MessageComponent BuildStoppedPlayer()
    {
        return new ComponentBuilderV2()
            .WithContainer(new ContainerBuilder()
                .WithAccentColor(DesignTokens.Info)
                .WithTextDisplay("## ⏹️ Playback stopped")
                .WithTextDisplay("Use `/play` or `/playlist` to start music again.")
            ).Build();
    }

    /// <summary>Builds the idle player display for static channel initialization</summary>
    /// <summary>The banner is attached to the message by the caller; withBanner points the card at it</summary>
    public static MessageComponent BuildIdlePlayer(ComponentBuilder buttons, bool withBanner = false)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music);
        if (withBanner)
            container.WithMediaGallery(new MediaGalleryBuilder().AddItem($"attachment://{BannerFileName}"));
        container
            .WithTextDisplay("## \U0001F3B5 PlexBot Music Player")
            .WithTextDisplay("No track is currently playing. Use `/play` to start!")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true)
            .WithTextDisplay("-# The player will appear here when music begins playing");
        AddActionRows(container, buttons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    // Emoji names for each segment type (order: empty → partial fills → fully filled)
    // These match the image filenames in /Images/Icons/progress/
    private static readonly string[] LeftCapNames =
        ["bar_left_empty", "bar_left_filled_1", "bar_left_filled_2", "bar_left_filled_3",
         "bar_left_filled_4", "bar_left_filled_5", "bar_left_filled_6", "bar_left_filled"];

    private static readonly string[] MidNames =
        ["bar_mid_empty", "bar_filled_1", "bar_filled_2", "bar_filled_3",
         "bar_filled_4", "bar_filled_5", "bar_filled_6", "bar_filled_7",
         "bar_filled_8", "bar_filled_9", "bar_filled_10", "bar_filled_11",
         "bar_filled_12", "bar_mid_filled"];

    private static readonly string[] RightCapNames =
        ["bar_right_empty", "bar_right_filled_1", "bar_right_filled_2", "bar_right_filled_3",
         "bar_right_filled_4", "bar_right_filled_5", "bar_right_filled_6", "bar_right_filled"];

    private const string EmojiConfigPrefix = "visualPlayer.progressBar.emoji.";

    // Resolved emoji arrays (custom Discord emoji or null if falling back to unicode)
    private static readonly string[]? LeftCapLevels;
    private static readonly string[]? MidLevels;
    private static readonly string[]? RightCapLevels;
    private static readonly bool UseCustomEmoji;
    private static readonly int MiddleSegmentCount;

    static ComponentV2Builder()
    {
        // Progress bar size from config
        string size = BotConfig.GetString("visualPlayer.progressBar.size").ToLowerInvariant();
        MiddleSegmentCount = size switch
        {
            "small" => 8,
            "large" => 20,
            _ => 14, // medium (default)
        };
        Logs.Info($"Progress bar size: {size switch { "small" => "small (10 segments)", "large" => "large (22 segments)", _ => "medium (16 segments)" }}");

        // All 30 emoji names across the three groups
        string[][] allGroups = [LeftCapNames, MidNames, RightCapNames];
        string[] allNames = allGroups.SelectMany(g => g).ToArray();

        // Check if ANY emoji ID is configured — if so, we attempt custom mode
        bool anyConfigured = allNames.Any(name =>
            !string.IsNullOrWhiteSpace(BotConfig.GetString($"{EmojiConfigPrefix}{name}")));

        if (!anyConfigured)
        {
            Logs.Info("Progress bar: Using unicode fallback (no custom emoji IDs configured)");
            UseCustomEmoji = false;
            return;
        }

        // Try to load all three groups — every ID must be present and valid
        LeftCapLevels = TryLoadEmojiGroup(LeftCapNames);
        MidLevels = TryLoadEmojiGroup(MidNames);
        RightCapLevels = TryLoadEmojiGroup(RightCapNames);
        UseCustomEmoji = LeftCapLevels != null && MidLevels != null && RightCapLevels != null;

        if (UseCustomEmoji)
            Logs.Info("Progress bar: Using custom Discord emoji (all 30 IDs loaded)");
        else
            Logs.Warning("Progress bar: Some emoji IDs are missing or invalid, falling back to unicode");
    }

    /// <summary>Loads a group of emoji by reading each named key from config</summary>
    private static string[]? TryLoadEmojiGroup(string[] names)
    {
        string[] result = new string[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string id = BotConfig.GetString($"{EmojiConfigPrefix}{names[i]}");
            if (string.IsNullOrWhiteSpace(id))
            {
                Logs.Warning($"Progress bar emoji missing: {names[i]}");
                return null;
            }
            if (!ulong.TryParse(id, out _))
            {
                Logs.Warning($"Progress bar emoji '{names[i]}' has invalid ID: '{id}' (must be numeric)");
                return null;
            }
            result[i] = $"<:{names[i]}:{id}>";
        }
        return result;
    }

    /// <summary>Builds a player status line showing the progress bar (volume/repeat are on the image)</summary>
    /// <remarks>Volume and repeat are added as text so they read without the image.</remarks>
    public static string BuildPlayerStatusLine(
        PlayerState state = PlayerState.NotPlaying,
        TimeSpan? position = null, TimeSpan? duration = null,
        int? volumePercent = null, TrackRepeatMode repeat = TrackRepeatMode.None)
    {
        string progressLine = BuildProgressBar(state, position, duration);
        if (volumePercent is null)
            return progressLine;
        return $"{progressLine}\n-# Volume {volumePercent}% \u00B7 Repeat {RepeatText(repeat)}";
    }

    /// <summary>File name the idle banner is attached under, and referenced by the card</summary>
    public const string BannerFileName = "PlexBotBanner.png";

    /// <summary>Note added to a result list that was cut to the select menu's 25 options</summary>
    public static string ShowingNote(int total, int shown) =>
        total > shown ? $" Showing the first {shown}." : string.Empty;

    /// <summary>Plain-language name of a repeat mode, as shown in the status line</summary>
    public static string RepeatText(TrackRepeatMode mode) => mode switch
    {
        TrackRepeatMode.Track => "one",
        TrackRepeatMode.Queue => "all",
        _ => "off",
    };

    /// <summary>Builds a progress bar using custom emoji (smooth partial fill) or unicode fallback</summary>
    private static string BuildProgressBar(PlayerState state, TimeSpan? position, TimeSpan? duration)
    {
        if (UseCustomEmoji)
            return BuildCustomEmojiBar(position, duration);
        return BuildUnicodeBar(position, duration);
    }

    /// <summary>Smooth-fill progress bar using custom Discord emoji with partial fill levels</summary>
    private static string BuildCustomEmojiBar(TimeSpan? position, TimeSpan? duration)
    {
        int middleSegments = MiddleSegmentCount;
        int totalSegments = middleSegments + 2; // left cap + middle + right cap

        if (position == null || duration == null || duration.Value.TotalSeconds < 1)
        {
            string emptyBar = LeftCapLevels![0]
                + string.Concat(Enumerable.Repeat(MidLevels![0], middleSegments))
                + RightCapLevels![0];
            return $"` 0:00 `{emptyBar}` 0:00 `";
        }

        double progress = Math.Clamp(position.Value.TotalSeconds / duration.Value.TotalSeconds, 0, 1);
        double fillPosition = progress * totalSegments;
        int activeSegment = (int)fillPosition;

        var bar = new System.Text.StringBuilder();
        for (int i = 0; i < totalSegments; i++)
        {
            string[] levels = i == 0 ? LeftCapLevels!
                : i == totalSegments - 1 ? RightCapLevels!
                : MidLevels!;

            int maxLevel = levels.Length - 1;

            if (i < activeSegment)
                bar.Append(levels[maxLevel]);
            else if (i == activeSegment)
            {
                double fraction = fillPosition - activeSegment;
                int level = (int)Math.Round(fraction * maxLevel);
                bar.Append(levels[Math.Clamp(level, 0, maxLevel)]);
            }
            else
                bar.Append(levels[0]);
        }

        string posStr = FormatTime(position.Value);
        string durStr = FormatTime(duration.Value);
        return $"` {posStr} `{bar}` {durStr} `";
    }

    /// <summary>Simple unicode block-character progress bar fallback</summary>
    private static string BuildUnicodeBar(TimeSpan? position, TimeSpan? duration)
    {
        int barLength = MiddleSegmentCount + 2;
        const char filled = '\u2593'; // ▓
        const char empty = '\u2591';  // ░

        if (position == null || duration == null || duration.Value.TotalSeconds < 1)
        {
            string emptyBar = new(empty, barLength);
            return $"` 0:00 `{emptyBar}` 0:00 `";
        }

        double progress = Math.Clamp(position.Value.TotalSeconds / duration.Value.TotalSeconds, 0, 1);
        int filledCount = (int)Math.Round(progress * barLength);

        string bar = new string(filled, filledCount) + new string(empty, barLength - filledCount);
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

    /// <summary>Builds the radio options panel shown when the Radio button is clicked on the visual player</summary>
    public static MessageComponent BuildRadioOptions(
        string trackTitle, string trackArtist, ComponentBuilder actionButtons, string? emoji = null)
    {
        string icon = emoji ?? "\uD83D\uDCFB";
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithTextDisplay($"## {icon} Start Radio")
            .WithTextDisplay($"Seed track: **{trackArtist}** - {trackTitle}")
            .WithTextDisplay("Generate a radio station based on this track's sonic profile.")
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, actionButtons);
        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    /// <summary>Builds a sonic results display for mood/genre/similar/radio track results</summary>
    public static MessageComponent BuildSonicResults(
        string title, string description, ComponentBuilder selectMenus, string? emoji = null)
    {
        string icon = emoji ?? "\uD83C\uDFB5";
        var container = new ContainerBuilder()
            .WithAccentColor(DesignTokens.Music)
            .WithTextDisplay($"## {icon} {title}")
            .WithTextDisplay(description)
            .WithSeparator(SeparatorSpacingSize.Small, isDivider: true);
        AddActionRows(container, selectMenus);
        return new ComponentBuilderV2().WithContainer(container).Build();
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
