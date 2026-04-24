namespace PlexBot.Core.Discord.Modals;

/// <summary>Modal for Sonic Adventure — collects the destination track name
/// to build a sonic path from the currently playing track</summary>
public class SonicAdventureModal : IModal
{
    public string Title => "Sonic Adventure";

    [InputLabel("Destination Track")]
    [ModalTextInput("destination", TextInputStyle.Short,
        placeholder: "Enter a track name to travel to...",
        maxLength: 200)]
    public string Destination { get; set; } = "";
}
