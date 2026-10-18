namespace PlexBot.Core.Discord.Help;

/// <summary>One help page. <see cref="EmojiName"/> is the bot's application emoji; <see cref="FallbackEmoji"/> shows when it isn't synced.
/// <see cref="ImageFile"/> is a screenshot in Images/Help, or null for a text-only page.</summary>
public sealed record HelpTopic(string Key, string Label, string EmojiName, string FallbackEmoji, string Summary, string Title, string Body, string? ImageFile);

/// <summary>The help content. Edit the wording here; the menu and the pages are built from this list in its order.</summary>
public static class HelpTopics
{
    public static readonly IReadOnlyList<HelpTopic> All =
    [
        new("start", "Start playing", "pb_play", "▶️", "Play a track, a playlist or a search result",
            "Start playing",
            "**Step 1.** Type `/play` and a name, or paste a link. The track joins the queue.\n" +
            "**Step 2.** The player appears in the channel. Its buttons control playback from there.\n\n" +
            "**`/playlist [playlist] [shuffle]`** queues a whole Plex playlist. The first track starts at once; the rest load in the background.\n" +
            "**`/search [mode] [query]`** finds music. Pick a result from the menu to play it.\n\n" +
            "New tracks queue behind whatever is playing. Turn on the **next** option to play them after the current track instead.",
            "help-start.png"),

        new("controls", "Player controls", "pb_pause", "⏯️", "What each button on the player does",
            "Player controls",
            "**Pause / Resume** toggles playback.\n" +
            "**Back** goes to the track played before this one.\n" +
            "**Skip** moves to the next track.\n" +
            "**Repeat** cycles Off, Repeat All, Repeat 1.\n" +
            "**Shuffle** shuffles the queue.\n" +
            "**Queue** shows what's coming up, with shuffle and clear.\n" +
            "**Vol - / Vol +** change the volume by 10%.\n" +
            "**Radio, Similar, Adventure** use Plex's sonic analysis (see Radio and sonic).\n" +
            "**Stop** ends playback, clears the queue and leaves voice.\n\n" +
            "The line under the progress bar shows the volume and repeat mode.",
            "help-controls.png"),

        new("search", "Search and discovery", "pb_search", "🔍", "Find music by name, mood or genre",
            "Search and discovery",
            "**Step 1.** Press **Search** on the static card, or type `/search`.\n" +
            "**Step 2.** Pick a mode. **Plex Library** searches your library by name.\n" +
            "**Find by Mood** and **Find by Genre** use Plex's sonic tags. They need sonic analysis on your Plex server.\n" +
            "**Radio Station** lists stations built from a track.\n" +
            "**Step 3.** Pick a result from the menu to play it. Results over 25 page with Previous and Next.\n\n" +
            "Other sources, such as YouTube, appear here when their extension is installed.",
            "help-search.png"),

        new("queue", "Queue", "pb_queue", "📋", "See, shuffle and clear what's coming up",
            "Queue",
            "**Step 1.** Press **Queue** on the player to see what's coming up, 10 tracks a page.\n" +
            "**Step 2.** Use the page buttons to move through the list.\n\n" +
            "⏳ marks a track that is still loading from Plex. It plays as soon as it's ready.\n" +
            "**Shuffle** mixes the queue. Its result appears only to you, so the player stays where it is.\n" +
            "**Clear** removes the queued tracks. The track playing now keeps playing.",
            "help-queue.png"),

        new("sonic", "Radio and sonic", "pb_radio", "📻", "Radio, similar tracks and sonic adventures",
            "Radio and sonic",
            "These need a Plex track playing and sonic analysis on your server.\n\n" +
            "**Radio** offers Replace Queue, Add to Queue, or Similar Tracks.\n" +
            "**Similar** shows 25 sonically similar tracks at a time, with Play All Similar.\n" +
            "**Adventure** asks where the path should end, then builds a path from the current track to it.\n\n" +
            "On the static card with nothing playing, **Radio** and **Adventure** ask for a starting track first.",
            "help-sonic.png"),

        new("trouble", "Troubleshooting", "pb_warning", "⚠️", "What the messages mean and what to do",
            "Troubleshooting",
            "**\"No active player\"**: nothing is playing. Start it with `/play`.\n" +
            "**\"Please wait a moment\"**: buttons have a short cooldown, per person. Wait, then press again.\n" +
            "**\"Playback stopped\"**: the player was stopped. Use `/play` or `/playlist` to start again.\n" +
            "**\"Plex couldn't find a playable file\"**: that track was skipped because Plex has no file for it.\n" +
            "**Plain symbols instead of the bot's emoji**: the emoji haven't synced yet. The bot shows them once the sync finishes.",
            "help-trouble.png"),
    ];

    /// <summary>Button id that opens the menu</summary>
    public const string HubId = "help:hub";

    /// <summary>Button id that opens the page with this key</summary>
    public static string PageId(string key) => $"help:page:{key}";

    /// <summary>The topic with this key, or null</summary>
    public static HelpTopic? Find(string key) => All.FirstOrDefault(topic => topic.Key == key);

    /// <summary>The topic before this one in the list, or null on the first</summary>
    public static HelpTopic? Previous(HelpTopic topic)
    {
        int index = IndexOf(topic);
        return index > 0 ? All[index - 1] : null;
    }

    /// <summary>The topic after this one in the list, or null on the last</summary>
    public static HelpTopic? Next(HelpTopic topic)
    {
        int index = IndexOf(topic);
        return index >= 0 && index < All.Count - 1 ? All[index + 1] : null;
    }

    /// <summary>Where a topic sits in the list, from 1, for the "Page 2 of 6" line</summary>
    public static int PositionOf(HelpTopic topic) => IndexOf(topic) + 1;

    private static int IndexOf(HelpTopic topic) => All.ToList().FindIndex(candidate => candidate.Key == topic.Key);
}
