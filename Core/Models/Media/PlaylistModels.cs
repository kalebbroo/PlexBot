namespace PlexBot.Core.Models.Media;

/// <summary>
/// Represents a music playlist entity retrieved from Plex or other media sources.
/// Playlists are user-defined collections of tracks that can be played in sequence.
/// This model normalizes playlist data across different source systems.
/// </summary>
public class Playlist
{
    /// <summary>
    /// Gets or sets the unique identifier for the playlist.
    /// This ID should be globally unique across all content sources.
    /// For Plex content, this is normally derived from the Plex rating key.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the playlist.
    /// This is the primary display name that will be shown to users in the interface.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the playlist.
    /// This may be user-provided or system-generated.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the playlist's artwork/cover image.
    /// For Plex content, this is typically the thumb URL with the Plex token appended.
    /// For playlists without a specific artwork, this may be derived from the first track.
    /// </summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the playlist details in the source system.
    /// Used for retrieving additional information or for generating links.
    /// For Plex, this is the playlist's key that can be appended to the base URL.
    /// </summary>
    public string PlaylistUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of tracks in the playlist.
    /// Used for display and for validating that all tracks were retrieved.
    /// </summary>
    public int TrackCount { get; set; }

    /// <summary>
    /// Gets or sets the collection of tracks that belong to this playlist.
    /// May be populated after the initial playlist retrieval with a separate call.
    /// </summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>
    /// Gets or sets the source system type this playlist was retrieved from.
    /// Helps determine how to handle the playlist for playback and metadata retrieval.
    /// </summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>
    /// Gets or sets the username of the playlist creator.
    /// Used for display and filtering purposes.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation date of the playlist.
    /// Used for sorting and display purposes.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date when the playlist was last updated.
    /// Used for sorting and display purposes.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the original source system's key for this playlist.
    /// This is used when making follow-up API calls to the source system.
    /// For Plex, this is typically the metadata key without the base URL.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Creates a human-readable representation of the playlist primarily for debugging and logging.
    /// Includes the essential identifying information without sensitive details.
    /// </summary>
    /// <returns>A string containing the playlist title and track count</returns>
    public override string ToString()
    {
        return $"{Title} ({TrackCount} tracks)";
    }

    /// <summary>
    /// Creates a new empty playlist with the specified title.
    /// Used primarily for creating custom playlists within the application.
    /// </summary>
    /// <param name="title">The title for the new playlist</param>
    /// <param name="description">Optional description for the playlist</param>
    /// <param name="createdBy">The user who created the playlist</param>
    /// <returns>A new Playlist instance with the provided properties</returns>
    public static Playlist CreateNew(string title, string description = "", string createdBy = "")
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return new Playlist
        {
            Id = $"custom_{Guid.NewGuid()}",
            Title = title,
            Description = description,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            SourceSystem = "custom"
        };
    }
}