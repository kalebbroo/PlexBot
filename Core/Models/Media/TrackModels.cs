namespace PlexBot.Core.Models.Media;

/// <summary>
/// Represents a music track entity retrieved from Plex or other media sources.
/// This model serves as the normalized representation of tracks across different source systems,
/// allowing for consistent handling regardless of the original source format.
/// </summary>
public class Track
{
    /// <summary>
    /// Gets or sets the unique identifier for the track.
    /// This ID should be globally unique across all content sources.
    /// For Plex content, this is normally derived from the Plex rating key.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the track.
    /// This is the primary display name that will be shown to users in the interface.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist who created the track.
    /// For multiple artists, this typically contains the primary/first artist.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the album the track belongs to.
    /// Will be empty for standalone tracks not associated with an album.
    /// </summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release date of the track.
    /// Stored as a string to accommodate various date formats from different sources.
    /// </summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the track's artwork/cover image.
    /// For Plex content, this is typically the thumb URL with the Plex token appended.
    /// </summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct playback URL for the track.
    /// This is the actual URL that will be passed to Lavalink for streaming.
    /// For Plex content, this typically includes the Plex token for authorization.
    /// </summary>
    public string PlaybackUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the artist's page/info.
    /// This is used when users want to navigate to see more content from the same artist.
    /// </summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the duration of the track in milliseconds.
    /// This is used for calculating playback time and progress displays.
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the formatted human-readable duration string.
    /// Calculated from DurationMs but stored for efficiency.
    /// Format is typically "mm:ss" or "hh:mm:ss" for longer tracks.
    /// </summary>
    public string DurationDisplay { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the label/studio that published the track.
    /// May be empty if not available from the source.
    /// </summary>
    public string Studio { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original source system's key for this track.
    /// This is used when making follow-up API calls to the source system.
    /// For Plex, this is typically the metadata key without the base URL.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source system type this track was retrieved from.
    /// Helps determine how to handle the track for playback and metadata retrieval.
    /// </summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>
    /// Creates a human-readable representation of the track primarily for debugging and logging.
    /// Includes the essential identifying information without sensitive details.
    /// </summary>
    /// <returns>A string containing the track title, artist and duration</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({DurationDisplay})";
    }

    /// <summary>
    /// Creates a new instance of a Track from a playback URL directly.
    /// Used primarily for quick creation of tracks from non-Plex sources like YouTube.
    /// </summary>
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