using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Models.Players;

/// <summary>
/// Represents an item in the music queue for a player.
/// Acts as a wrapper around a Track with additional metadata used for
/// playback management and tracking request information. This model bridges
/// the domain Track model with the Lavalink playback system.
/// </summary>
public class QueueItem : ITrackQueueItem
{
    /// <summary>
    /// Gets or sets the track information.
    /// Contains all metadata about the media to be played.
    /// </summary>
    public Track Track { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Lavalink track reference.
    /// This is the reference used by Lavalink for actual playback and control.
    /// </summary>
    public TrackReference LavalinkReference { get; set; }

    /// <summary>
    /// Gets or sets the Discord user ID who requested this track.
    /// Used for display and for permission checking on queue operations.
    /// </summary>
    public ulong RequestedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the Discord username who requested this track.
    /// Stored for display without having to look up the user again.
    /// </summary>
    public string RequestedByUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timestamp when the track was added to the queue.
    /// Used for sorting and display purposes.
    /// </summary>
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the track from the Lavalink reference.
    /// This is required by the ITrackQueueItem interface.
    /// </summary>
    LavalinkTrack? ITrackQueueItem.Track => LavalinkReference.Track;

    public TrackReference Reference => throw new NotImplementedException();

    /// <summary>
    /// Creates a human-readable representation of the queue item primarily for debugging and logging.
    /// Includes the essential information about the track and who requested it.
    /// </summary>
    /// <returns>A string containing track details and requester info</returns>
    public override string ToString()
    {
        return $"{Track.Title} by {Track.Artist} (requested by {RequestedByUsername})";
    }

    /// <summary>
    /// Generic conversion method required by Lavalink for queue handling.
    /// Allows casting this queue item to other compatible queue item types.
    /// </summary>
    /// <typeparam name="T">The type to convert to</typeparam>
    /// <returns>This instance as the requested type, or null if not compatible</returns>
    public T? As<T>() where T : class, ITrackQueueItem
    {
        return this as T;
    }

    /// <summary>
    /// Creates a new queue item from a track and requester information.
    /// This is the main factory method used when adding tracks to the queue.
    /// It handles setting up all the necessary Lavalink integration.
    /// </summary>
    /// <param name="track">The track to queue</param>
    /// <param name="lavalinkTrack">The corresponding Lavalink track</param>
    /// <param name="userId">The ID of the user who requested the track</param>
    /// <param name="username">The username of the user who requested the track</param>
    /// <returns>A fully configured QueueItem ready for playback</returns>
    public static QueueItem Create(Track track, LavalinkTrack lavalinkTrack, ulong userId, string username)
    {
        return new QueueItem
        {
            Track = track,
            LavalinkReference = new TrackReference(lavalinkTrack),
            RequestedByUserId = userId,
            RequestedByUsername = username,
            AddedAt = DateTimeOffset.UtcNow
        };
    }
}