namespace PlexBot.Core.Models.Media;

/// <summary>Represents a mood tag from the Plex library sonic analysis (e.g., "Energetic", "Melancholy")</summary>
public class MoodTag
{
    /// <summary>The Plex filter ID used in API queries</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name of the mood</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The filter key path for API queries</summary>
    public string FilterKey { get; set; } = string.Empty;
}

/// <summary>Represents a genre tag from the Plex library</summary>
public class GenreTag
{
    /// <summary>The Plex filter ID used in API queries</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name of the genre</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The filter key path for API queries</summary>
    public string FilterKey { get; set; } = string.Empty;
}

/// <summary>Represents a Plex radio station (dynamic playlist generated from sonic analysis)</summary>
public class RadioStation
{
    /// <summary>Unique identifier for the station</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display title of the station</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Station description</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>URL to the station's artwork</summary>
    public string ArtworkUrl { get; set; } = string.Empty;

    /// <summary>The URI used to create a PlayQueue from this station</summary>
    public string StationUri { get; set; } = string.Empty;

    /// <summary>Type of station (mood, genre, artist, track, etc.)</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Source-specific key for API operations</summary>
    public string SourceKey { get; set; } = string.Empty;
}
