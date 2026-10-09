namespace PlexBot.Core.Discord.Help;

/// <summary>One topic in the help hub: a menu entry, a card title and body, and an optional screenshot in Images/Help</summary>
public sealed record HelpTopic(string Key, string Label, string Emoji, string Summary, string Title, string Body, string? ImageFile);

/// <summary>The help content. Edit the wording here; the hub and the topic cards are built from this list.</summary>
public static class HelpTopics
{
    public static readonly IReadOnlyList<HelpTopic> All =
    [
        new("start", "Start playing", "▶️", "Play a track, a playlist or a search result",
            "Start playing",
            "**`/play [query]`** plays one track. Type a name or paste a link.\n" +
            "**`/playlist [playlist] [shuffle]`** queues a whole Plex playlist. The first track starts at once; the rest load in the background.\n" +
            "**`/search [mode] [query]`** finds music. Pick a result from the menu to play it.\n\n" +
            "New tracks queue behind whatever is playing. The player appears in the channel, and its buttons control it.",
            "help-start.png"),

        new("controls", "Player controls", "⏯️", "What each button on the player does",
            "Player controls",
            "**Pause / Resume** toggles playback.\n" +
            "**Skip** moves to the next track.\n" +
            "**Repeat** cycles Off, Repeat All, Repeat 1.\n" +
            "**Shuffle** shuffles the queue.\n" +
            "**Queue** shows what's coming up, with shuffle and clear.\n" +
            "**Vol - / Vol +** change the volume by 10%.\n" +
            "**Radio, Similar, Adventure** use Plex's sonic analysis (see Radio and sonic).\n" +
            "**Stop** ends playback, clears the queue and leaves voice.\n\n" +
            "The line under the progress bar shows the volume and repeat mode.",
            "help-controls.png"),

        new("search", "Search and discovery", "\U0001F50D", "Find music by name, mood or genre",
            "Search and discovery",
            "**Plex Library** searches your library by name.\n" +
            "**Find by Mood** and **Find by Genre** use Plex's sonic tags. They need sonic analysis on your Plex server.\n" +
            "**Radio Station** lists stations built from a track.\n\n" +
            "Pick a result from the menu to play it. Results over 25 page with Previous and Next.",
            "help-search.png"),

        new("queue", "Queue", "\U0001F4CB", "See, shuffle and clear what's coming up",
            "Queue",
            "Press **Queue** on the player to see what's coming up, 10 tracks a page.\n" +
            "⏳ marks a track that is still loading from Plex. It plays as soon as it's ready.\n" +
            "**Shuffle** mixes the queue. **Clear** removes the queued tracks.",
            "help-queue.png"),

        new("sonic", "Radio and sonic", "\U0001F4FB", "Radio, similar tracks and sonic adventures",
            "Radio and sonic",
            "These need a Plex track playing and sonic analysis on your server.\n\n" +
            "**Radio** offers Replace Queue, Add to Queue, or Similar Tracks.\n" +
            "**Similar** shows 25 sonically similar tracks at a time, with Play All Similar.\n" +
            "**Adventure** asks where the path should end, then builds a path from the current track to it.",
            "help-sonic.png"),

        new("trouble", "Troubleshooting", "\U0001F6E0️", "What the messages mean and what to do",
            "Troubleshooting",
            "**\"No active player\"**: nothing is playing. Start it with `/play`.\n" +
            "**\"Please wait a moment\"**: buttons have a two-second cooldown. Wait, then press again.\n" +
            "**\"Playback stopped\"**: the player was stopped. Use `/play` or `/playlist` to start again.\n" +
            "**\"Plex couldn't find a playable file\"**: that track was skipped because Plex has no file for it.\n" +
            "**Plain symbols instead of the bot's emoji**: the emoji haven't synced. See the Custom Emoji guide.",
            "help-trouble.png"),
    ];

    /// <summary>The topic with this key, or null</summary>
    public static HelpTopic? Find(string key) => All.FirstOrDefault(topic => topic.Key == key);
}
