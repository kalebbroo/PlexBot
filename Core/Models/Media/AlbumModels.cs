namespace PlexBot.Core.Models.Media;

/// <summary>Represents a music album entity that normalizes metadata across different source systems for consistent display and playback</summary>
public class Album
{
    /// <summary>Globally unique identifier for the album, typically derived from the source system's native ID (e.g., Plex rating key)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary display name of the album shown to users in search results and player interfaces</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Name of the artist or group who created the album, may be "Various Artists" for compilations</summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>Release date of the album stored as a string to accommodate various date formats from different sources</summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>URL to the album artwork image used for display in the player and search results</summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>Primary genre associated with this album, used for filtering and display purposes</summary>
    public string Genre { get; set; } = string.Empty;

    /// <summary>Name of the record label or studio that published the album</summary>
    public string Studio { get; set; } = string.Empty;

    /// <summary>Descriptive summary of the album, may include editorial reviews or artist-provided descriptions</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Number of tracks contained in the album</summary>
    public int TrackCount { get; set; }

    /// <summary>Collection of tracks that belong to this album, may be populated after the initial album retrieval</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>Original source system that provided this album data (e.g., "plex", "spotify")</summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>Source-specific key or identifier used to retrieve this album's tracks from the source API</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>URL to the album details in the source system, used for retrieving additional information or generating links</summary>
    public string AlbumUrl { get; set; } = string.Empty;

    /// <summary>URL to the artist's page/info, used when users want to navigate to see more content from the same artist</summary>
    public string ArtistUrl { get; set; } = string.Empty;

    /// <summary>Year the album was released as an integer, extracted from ReleaseDate for easier sorting and filtering</summary>
    public int Year { get; set; }

    /// <summary>Creates a human-readable representation of the album primarily for debugging and logging</summary>
    /// <returns>A string containing the album title, artist and track count</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({TrackCount} tracks)";
    }

    /// <summary>Extracts the year from the ReleaseDate property and updates the Year property</summary>
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