namespace PlexBot.Core.Discord.Messages;

/// <summary>A title and body shown in a status card. Use with the Notice overloads on ComponentV2Builder.</summary>
public readonly record struct Notice(string Title, string Body);

/// <summary>Status messages that appear in more than one place. Keeping them here means the wording stays the same
/// everywhere. Messages that include a dynamic value (a track name, a query) stay inline at the call site.</summary>
public static class Notices
{
    /// <summary>A button was pressed again before its short cooldown ended</summary>
    public static readonly Notice Cooldown = new("Cooldown", "Please wait a moment before clicking again.");

    /// <summary>A control was used but no player exists for the guild</summary>
    public static readonly Notice NoPlayer = new("No Player", "No active player found.");

    /// <summary>A player control needs a track, and none is playing</summary>
    public static readonly Notice NoTrack = new("No Track", "No track is currently playing.");

    /// <summary>A radio or sonic action returned nothing because the track has no sonic analysis</summary>
    public static readonly Notice NoRadioTracks = new("No Tracks", "No radio tracks were returned. This track may not have sonic analysis data.");
}
