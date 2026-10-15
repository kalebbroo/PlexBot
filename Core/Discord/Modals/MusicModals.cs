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

/// <summary>Modal for the Search button and the provider search buttons: the query to search for. The provider modals
/// are built from the label and placeholder below, with their own title.</summary>
public class SearchQueryModal : IModal
{
    public const string QueryLabel = "What should I search for?";
    public const string QueryPlaceholder = "e.g. Bohemian Rhapsody";

    public string Title => "Search Plex";

    [InputLabel(QueryLabel)]
    [ModalTextInput("query", TextInputStyle.Short,
        placeholder: QueryPlaceholder,
        maxLength: 200)]
    public string Query { get; set; } = "";
}

/// <summary>Modal for the Play button: a song, artist, album or link, played the way /play plays it</summary>
public class EntryPlayModal : IModal
{
    public string Title => "Play";

    [InputLabel("What should I play?")]
    [ModalTextInput("query", TextInputStyle.Short,
        placeholder: "Song, artist, album, or a link",
        maxLength: 500)]
    public string Query { get; set; } = "";
}

/// <summary>Modal for the idle card's Adventure button: the track the path starts from, and the track it ends on</summary>
public class IdleAdventureModal : IModal
{
    public string Title => "Sonic Adventure";

    [InputLabel("Start the path from which track?")]
    [ModalTextInput("start", TextInputStyle.Short,
        placeholder: "e.g. Bohemian Rhapsody",
        maxLength: 200)]
    public string Start { get; set; } = "";

    [InputLabel("Where should the path end?")]
    [ModalTextInput("destination", TextInputStyle.Short,
        placeholder: "e.g. Hotel California",
        maxLength: 200)]
    public string Destination { get; set; } = "";
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
