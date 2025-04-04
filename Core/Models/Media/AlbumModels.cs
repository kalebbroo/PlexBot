namespace PlexBot.Core.Models.Media;

/// <summary>
/// Represents a music album entity retrieved from Plex or other media sources.
/// Albums are collections of tracks with shared metadata and are a fundamental 
/// organizational unit in music libraries. This model normalizes album data across
/// different source systems.
/// </summary>
public class Album
{
    /// <summary>
    /// Gets or sets the unique identifier for the album.
    /// This ID should be globally unique across all content sources.
    /// For Plex content, this is normally derived from the Plex rating key.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the album.
    /// This is the primary display name that will be shown to users in the interface.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artist who created the album.
    /// For compilation albums, this may be "Various Artists" or similar.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the release date of the album.
    /// Stored as a string to accommodate various date formats from different sources.
    /// </summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the album's artwork/cover image.
    /// For Plex content, this is typically the thumb URL with the Plex token appended.
    /// </summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the album details in the source system.
    /// Used for retrieving additional information or for generating links.
    /// For Plex, this is the album's key that can be appended to the base URL.
    /// </summary>
    public string AlbumUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the artist's page/info.
    /// This is used when users want to navigate to see more content from the same artist.
    /// </summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the label/studio that published the album.
    /// May be empty if not available from the source.
    /// </summary>
    public string Studio { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary genre associated with the album.
    /// Used for filtering and display purposes.
    /// </summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a descriptive summary of the album.
    /// This may include editorial reviews or artist-provided descriptions.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original source system's key for this album.
    /// This is used when making follow-up API calls to the source system.
    /// For Plex, this is typically the metadata key without the base URL.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of tracks in the album.
    /// Used for display and for validating that all tracks were retrieved.
    /// </summary>
    public int TrackCount { get; set; }

    /// <summary>
    /// Gets or sets the collection of tracks that belong to this album.
    /// May be populated after the initial album retrieval with a separate call.
    /// </summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>
    /// Gets or sets the source system type this album was retrieved from.
    /// Helps determine how to handle the album for playback and metadata retrieval.
    /// </summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>
    /// Gets or sets the year the album was released as an integer.
    /// Extracted from ReleaseDate for easier sorting and filtering.
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// Creates a human-readable representation of the album primarily for debugging and logging.
    /// Includes the essential identifying information without sensitive details.
    /// </summary>
    /// <returns>A string containing the album title, artist and track count</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({TrackCount} tracks)";
    }

    /// <summary>
    /// Extracts the year from the ReleaseDate property and updates the Year property.
    /// Called when the album is created or when the ReleaseDate is modified.
    /// </summary>
    public void ExtractYear()
    {
        if (string.IsNullOrEmpty(ReleaseDate))
        {
            Year = 0;
            return;
        }

        // Try to parse the first 4 digits as a year
        string yearStr = new string(ReleaseDate.Where(char.IsDigit).Take(4).ToArray());
        if (int.TryParse(yearStr, out int year) && year >= 1000 && year <= 9999)
        {
            Year = year;
        }
        else
        {
            Year = 0;
        }
    }
}