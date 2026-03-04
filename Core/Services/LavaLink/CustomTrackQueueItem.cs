using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Enhanced track queue item that holds a reference to the source Track model and exposes metadata through convenience properties for UI compatibility</summary>
public class CustomTrackQueueItem : ITrackQueueItem
{
    /// <summary>The Lavalink track reference for audio streaming. Null until resolved.</summary>
    public TrackReference Reference { get; set; }

    /// <summary>Provides access to the underlying Lavalink track object through the interface implementation</summary>
    LavalinkTrack? ITrackQueueItem.Track => Reference.Track;

    /// <summary>The source track metadata from Plex/YouTube/etc.</summary>
    public Track SourceTrack { get; init; } = new();

    /// <summary>The Discord user who requested this track</summary>
    public string? RequestedBy { get; init; }

    // Convenience accessors for backward compatibility with UI code (ImageBuilder, VisualPlayer, DiscordEmbedBuilder)
    public string? Title => SourceTrack.Title;
    public string? Artist => SourceTrack.Artist;
    public string? Album => SourceTrack.Album;
    public string? ReleaseDate => SourceTrack.ReleaseDate;
    public string? Artwork => SourceTrack.ArtworkUrl;
    public string? Url => SourceTrack.PlaybackUrl;
    public string? ArtistUrl => SourceTrack.ArtistUrl;
    public string? Duration => SourceTrack.DurationDisplay;
    public string? Studio => SourceTrack.Studio;

    /// <summary>Implementation of the interface's type conversion method to support Lavalink's player architecture</summary>
    public T? As<T>() where T : class, ITrackQueueItem => this as T;

    /// <summary>Generates a user-friendly string representation of this track for logging and debugging</summary>
    public override string ToString() => $"{Title} by {Artist} ({Duration})";
}
