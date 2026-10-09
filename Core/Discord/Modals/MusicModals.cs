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
