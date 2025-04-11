namespace PlexBot.Core.Models.Media;

/// <summary>Represents a music playlist entity that normalizes metadata across different source systems for consistent playback and display</summary>
public class Playlist
{
    /// <summary>Globally unique identifier for the playlist, typically derived from the source system's native ID (e.g., Plex rating key)</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Primary display name of the playlist shown to users in search results and player interfaces</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>User-provided or system-generated description of the playlist's contents or purpose</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>URL to the playlist's artwork image used for display in the player and search results</summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>URL to the playlist details in the source system, used for retrieving additional information or generating links</summary>
    public string PlaylistUrl { get; set; } = string.Empty;

    /// <summary>Number of tracks in the playlist, used for display and validation purposes</summary>
    public int TrackCount { get; set; }

    /// <summary>Collection of tracks that belong to this playlist, may be populated after the initial playlist retrieval</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>Original source system that provided this playlist data (e.g., "plex", "spotify", "custom")</summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>Username or identifier of the person who created the playlist, used for display and filtering</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Date and time when the playlist was first created, used for sorting and display purposes</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date and time when the playlist was last modified, used for sorting and determining recency</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Source-specific key or identifier used to retrieve this playlist's tracks from the source API</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Creates a human-readable representation of the playlist primarily for debugging and logging</summary>
    /// <returns>A string containing the playlist title and track count</returns>
    public override string ToString()
    {
        return $"{Title} ({TrackCount} tracks)";
    }

    /// <summary>Creates a new empty custom playlist with the specified title for use within the application</summary>
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