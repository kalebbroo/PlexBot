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

    /// <summary>A queue change or a control that opens a modal was used by someone who is not in a voice channel</summary>
    public static readonly Notice NotInVoice = new("Not In Voice", "Join a voice channel in this server, then try again.");

    /// <summary>A radio or sonic action returned nothing because the track has no sonic analysis</summary>
    public static readonly Notice NoRadioTracks = new("No Tracks", "No radio tracks were returned. This track may not have sonic analysis data.");
}

/// <summary>Links into the troubleshooting guide, for bodies that point the user at the fix. Each anchor is the slug GitHub
/// makes from a heading in Docs/Guides/Troubleshooting.md; NoticeTests checks every one against that file.</summary>
public static class DocsLinks
{
    /// <summary>The troubleshooting guide on GitHub</summary>
    public const string BaseUrl = "https://github.com/kalebbroo/PlexBot/blob/main/Docs/Guides/Troubleshooting.md";

    /// <summary>The "Unable to connect to Lavalink node" heading: the audio server is down or misconfigured</summary>
    public const string LavalinkConnection = BaseUrl + "#unable-to-connect-to-lavalink-node";

    /// <summary>The "Track Loading Failures" section: any track that Plex could not supply, covering both outcomes below</summary>
    public const string TrackLoadFailures = BaseUrl + "#track-loading-failures";

    /// <summary>The "Track Skipped" heading: Plex did not return the file after several tries</summary>
    public const string TrackSkipped = BaseUrl + "#track-skipped--plex-didnt-return-the-file";

    /// <summary>The "All Tracks Fail" heading: Plex cannot be reached, or PLEX_URL or PLEX_TOKEN is wrong</summary>
    public const string PlexServer = BaseUrl + "#all-tracks-fail";
}
