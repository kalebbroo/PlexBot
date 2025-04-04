namespace PlexBot.Core.Models.Media;

/// <summary>
/// Represents a music artist entity retrieved from Plex or other media sources.
/// Artists are the creators of music content and serve as a primary organizational 
/// structure in music libraries. This model normalizes artist data across different 
/// source systems.
/// </summary>
public class Artist
{
    /// <summary>
    /// Gets or sets the unique identifier for the artist.
    /// This ID should be globally unique across all content sources.
    /// For Plex content, this is normally derived from the Plex rating key.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the artist.
    /// This is the primary display name that will be shown to users in the interface.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a descriptive summary of the artist.
    /// This may include biographical information or other relevant details.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the artist's artwork/image.
    /// For Plex content, this is typically the thumb URL with the Plex token appended.
    /// </summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL to the artist details in the source system.
    /// Used for retrieving additional information or for generating links.
    /// For Plex, this is the artist's key that can be appended to the base URL.
    /// </summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original source system's key for this artist.
    /// This is used when making follow-up API calls to the source system.
    /// For Plex, this is typically the metadata key without the base URL.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of albums by this artist.
    /// Used for display and for validating that all albums were retrieved.
    /// </summary>
    public int AlbumCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of tracks by this artist.
    /// This may be an estimate and is primarily used for display purposes.
    /// </summary>
    public int TrackCount { get; set; }

    /// <summary>
    /// Gets or sets the collection of albums by this artist.
    /// May be populated after the initial artist retrieval with a separate call.
    /// </summary>
    public List<Album> Albums { get; set; } = new();

    /// <summary>
    /// Gets or sets the primary genre associated with the artist.
    /// Used for filtering and display purposes.
    /// </summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source system type this artist was retrieved from.
    /// Helps determine how to handle the artist for metadata retrieval.
    /// </summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>
    /// Creates a human-readable representation of the artist primarily for debugging and logging.
    /// Includes the essential identifying information without sensitive details.
    /// </summary>
    /// <returns>A string containing the artist name and album count</returns>
    public override string ToString()
    {
        return $"{Name} ({AlbumCount} albums, {TrackCount} tracks)";
    }

    /// <summary>
    /// Gets a truncated summary for display in UI components with limited space.
    /// Ensures that the summary doesn't exceed the given length and adds an ellipsis if truncated.
    /// </summary>
    /// <param name="maxLength">The maximum length of the returned summary</param>
    /// <returns>A truncated summary</returns>
    public string GetTruncatedSummary(int maxLength = 100)
    {
        if (string.IsNullOrEmpty(Summary) || Summary.Length <= maxLength)
        {
            return Summary;
        }

        return Summary.Substring(0, maxLength - 3) + "...";
    }
}