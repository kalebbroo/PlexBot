namespace PlexBot.Core.Discord.Modals;

/// <summary>Modal for Sonic Adventure — collects the destination track name
/// to build a sonic path from the currently playing track</summary>
public class SonicAdventureModal : IModal
{
    public string Title => "Sonic Adventure";

    [InputLabel("Where should the path end?")]
    [ModalTextInput("destination", TextInputStyle.Short,
        placeholder: "e.g. Bohemian Rhapsody",
        maxLength: 200)]
    public string Destination { get; set; } = "";
}

/// <summary>Modal for the idle card's Search button: the query to search the Plex library for</summary>
public class IdleSearchModal : IModal
{
    public string Title => "Search Plex";

    [InputLabel("What should I search for?")]
    [ModalTextInput("query", TextInputStyle.Short,
        placeholder: "e.g. Bohemian Rhapsody",
        maxLength: 200)]
    public string Query { get; set; } = "";
}

/// <summary>Modal for the idle card's Radio button: the track that radio starts from</summary>
public class IdleRadioModal : IModal
{
    public string Title => "Start Radio";

    [InputLabel("Start radio from which track?")]
    [ModalTextInput("query", TextInputStyle.Short,
        placeholder: "e.g. Bohemian Rhapsody",
        maxLength: 200)]
    public string Query { get; set; } = "";
}
