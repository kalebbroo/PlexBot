namespace PlexBot.Core.Models.Media;

/// <summary>Represents a music track entity that normalizes metadata across different source systems for consistent playback and display</summary>
public class Track
{
    /// <summary>Globally unique identifier for the track, typically derived from the source system's native ID (e.g., Plex rating key)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary display name of the track shown to users in search results and player interfaces</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Name of the artist or group who created the track, typically contains the primary/first artist for collaborations</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>Name of the album the track belongs to, may be empty for standalone tracks not associated with an album</summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>Release date of the track stored as a string to accommodate various date formats from different sources</summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>URL to the track's artwork image used for display in the player and search results</summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>Direct streaming URL that will be passed to the audio player for playback, includes authentication tokens if needed</summary>
    public string PlaybackUrl { get; set; } = string.Empty;

    /// <summary>URL to the artist's page/info, used for navigation to see more content from the same artist</summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>Duration of the track in milliseconds, used for calculating playback time and progress displays</summary>
    public long DurationMs { get; set; }

    /// <summary>Formatted human-readable duration string (e.g., "3:45") calculated from DurationMs but stored for efficiency</summary>
    public string DurationDisplay { get; set; } = string.Empty;

    /// <summary>Name of the record label or studio that published the track, may be empty if not available from the source</summary>
    public string Studio { get; set; } = string.Empty;

    /// <summary>Source-specific key or identifier used to retrieve this track from the source API for playback</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Original source system that provided this track data (e.g., "plex", "spotify", "youtube")</summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>Creates a human-readable representation of the track primarily for debugging and logging</summary>
    /// <returns>A string containing the track title, artist and duration</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({DurationDisplay})";
    }

    /// <summary>Creates a new Track instance from a direct playback URL for quick creation from external sources like YouTube</summary>
    /// <param name="title">Title of the track</param>
    /// <param name="artist">Artist name</param>
    /// <param name="playbackUrl">Direct URL for playback</param>
    /// <param name="artworkUrl">URL to artwork/thumbnail</param>
    /// <param name="sourceSystem">The source system (default: "external")</param>
    /// <returns>A new Track instance with the provided properties</returns>
    public static Track CreateFromUrl(string title, string artist, string playbackUrl, string artworkUrl, string sourceSystem = "external")
    {
        return new Track
        {
            Id = $"{sourceSystem}_{Guid.NewGuid()}",
            Title = title,
            Artist = artist,
            PlaybackUrl = playbackUrl,
            ArtworkUrl = artworkUrl,
            SourceSystem = sourceSystem
        };
    }
}