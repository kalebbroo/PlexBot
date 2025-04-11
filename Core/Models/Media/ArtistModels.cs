namespace PlexBot.Core.Models.Media;

/// <summary>Represents a music artist entity that normalizes metadata across different source systems for consistent display and navigation</summary>
public class Artist
{
    /// <summary>Globally unique identifier for the artist, typically derived from the source system's native ID (e.g., Plex rating key)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary display name of the artist shown to users in search results and player interfaces</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Biographical information or other relevant details about the artist for display in detailed views</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>URL to the artist's profile image used for display in the player and search results</summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>URL to the artist details in the source system, used for retrieving additional information or generating links</summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>Source-specific key or identifier used to retrieve this artist's albums from the source API</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Number of albums created by this artist, used for display and validation purposes</summary>
    public int AlbumCount { get; set; }

    /// <summary>Total number of tracks by this artist, may be an estimate primarily used for display purposes</summary>
    public int TrackCount { get; set; }

    /// <summary>Collection of albums by this artist, may be populated after the initial artist retrieval</summary>
    public List<Album> Albums { get; set; } = new();

    /// <summary>Primary musical genre associated with this artist, used for filtering and display purposes</summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>Original source system that provided this artist data (e.g., "plex", "spotify")</summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>Creates a human-readable representation of the artist primarily for debugging and logging</summary>
    /// <returns>A string containing the artist name and album count</returns>
    public override string ToString()
    {
        return $"{Name} ({AlbumCount} albums, {TrackCount} tracks)";
    }

    /// <summary>Provides a truncated summary for display in UI components with limited space</summary>
    /// <param name="maxLength">The maximum length of the returned summary</param>
    /// <returns>A truncated summary with ellipsis if needed</returns>
    public string GetTruncatedSummary(int maxLength = 100)
    {
        if (string.IsNullOrEmpty(Summary) || Summary.Length <= maxLength)
        {
            return Summary;
        }

        return Summary.Substring(0, maxLength - 3) + "...";
    }
}