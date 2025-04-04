namespace PlexBot.Services;

/// <summary>
/// Custom implementation of ITrackQueueItem with extended Plex metadata.
/// This class enriches the standard Lavalink track queue item with additional
/// metadata from Plex, allowing for a richer playback experience with detailed
/// track information displayed in the player interface.
/// </summary>
public class CustomTrackQueueItem : ITrackQueueItem
{
    /// <summary>
    /// Gets or sets the Lavalink track reference for playback.
    /// This is the core reference that Lavalink uses to stream the audio.
    /// </summary>
    public TrackReference Reference { get; set; }

    /// <summary>
    /// Gets the Lavalink track from the reference.
    /// Required implementation of ITrackQueueItem interface.
    /// </summary>
    LavalinkTrack? ITrackQueueItem.Track => Reference.Track;

    /// <summary>
    /// Gets or sets the title of the track.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the artist of the track.
    /// </summary>
    public string? Artist { get; set; }

    /// <summary>
    /// Gets or sets the album of the track.
    /// </summary>
    public string? Album { get; set; }

    /// <summary>
    /// Gets or sets the release date of the track.
    /// </summary>
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the artwork URL for the track.
    /// </summary>
    public string? Artwork { get; set; }

    /// <summary>
    /// Gets or sets the playback URL for the track.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the URL for the artist.
    /// </summary>
    public string? ArtistUrl { get; set; }

    /// <summary>
    /// Gets or sets the duration of the track in a human-readable format.
    /// </summary>
    public string? Duration { get; set; }

    /// <summary>
    /// Gets or sets the studio that produced the track.
    /// </summary>
    public string? Studio { get; set; }

    /// <summary>
    /// Gets or sets the user who requested this track.
    /// </summary>
    public string? RequestedBy { get; set; }

    /// <summary>
    /// Generic type conversion method required by the ITrackQueueItem interface.
    /// Allows casting to other compatible queue item types.
    /// </summary>
    /// <typeparam name="T">The type to cast to</typeparam>
    /// <returns>This instance as type T, or null if not compatible</returns>
    public T? As<T>() where T : class, ITrackQueueItem => this as T;

    /// <summary>
    /// Creates a human-readable representation of the queue item.
    /// Useful for debugging and logging.
    /// </summary>
    /// <returns>A string representation of the queue item</returns>
    public override string ToString()
    {
        return $"{Title} by {Artist} ({Duration})";
    }
}