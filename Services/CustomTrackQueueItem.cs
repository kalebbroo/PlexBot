namespace PlexBot.Services;

/// <summary>Enhanced track queue item that extends the standard Lavalink interface with rich Plex metadata for improved display and user experience</summary>
public class CustomTrackQueueItem : ITrackQueueItem
{
    /// <summary>Contains the core Lavalink track reference needed for audio streaming and playback control</summary>
    public TrackReference Reference { get; set; }

    /// <summary>Provides access to the underlying Lavalink track object through the interface implementation</summary>
    LavalinkTrack? ITrackQueueItem.Track => Reference.Track;

    /// <summary>Stores the track's title as retrieved from Plex metadata</summary>
    public string? Title { get; set; }

    /// <summary>Stores the track's artist name as retrieved from Plex metadata</summary>
    public string? Artist { get; set; }

    /// <summary>Stores the album name containing this track as retrieved from Plex metadata</summary>
    public string? Album { get; set; }

    /// <summary>Stores the track's release date to show age/recency information to users</summary>
    public string? ReleaseDate { get; set; }

    /// <summary>URL to the album/track artwork for embedding in Discord messages and UI</summary>
    public string? Artwork { get; set; }

    /// <summary>Direct URL to the track's playback source for reference and linking</summary>
    public string? Url { get; set; }

    /// <summary>URL to the artist's page for linking in the UI and providing additional context</summary>
    public string? ArtistUrl { get; set; }

    /// <summary>Human-readable duration string formatted for display in player embeds</summary>
    public string? Duration { get; set; }

    /// <summary>Recording studio information to provide additional context about the track's origin</summary>
    public string? Studio { get; set; }

    /// <summary>Username of the Discord user who requested this track for attribution and permission management</summary>
    public string? RequestedBy { get; set; }

    /// <summary>Implementation of the interface's type conversion method to support Lavalink's player architecture</summary>
    /// <typeparam name="T">The target type to convert to within the Lavalink player system</typeparam>
    /// <returns>This instance as the requested type if compatible, otherwise null</returns>
    public T? As<T>() where T : class, ITrackQueueItem => this as T;

    /// <summary>Generates a user-friendly string representation of this track for logging and debugging purposes</summary>
    /// <returns>A formatted string containing essential track information</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({Duration})";
    }
}