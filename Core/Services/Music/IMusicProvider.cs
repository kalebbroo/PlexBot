using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.Music;

/// <summary>Defines a pluggable music source that can search, browse, and provide tracks.
/// Extensions implement this interface to add new music providers.</summary>
public interface IMusicProvider
{
    /// <summary>Unique identifier for this provider (e.g., "plex", "youtube", "soundcloud")</summary>
    string Id { get; }

    /// <summary>Display name shown in autocomplete and UI (e.g., "Plex", "YouTube")</summary>
    string DisplayName { get; }

    /// <summary>Whether this provider is currently available and configured</summary>
    bool IsAvailable { get; }

    /// <summary>Priority for ordering in autocomplete (lower = higher priority)</summary>
    int Priority { get; }

    /// <summary>Capabilities this provider supports</summary>
    MusicProviderCapabilities Capabilities { get; }

    /// <summary>Search for music across this provider's catalog</summary>
    Task<SearchResults> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Get track details by provider-specific key. Returns null if not supported.</summary>
    Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default);

    /// <summary>Get all tracks in a container (album, playlist, etc). Returns empty if not supported.</summary>
    Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default);

    /// <summary>Get albums for an artist. Returns empty if not supported.</summary>
    Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default);

    /// <summary>Get all tracks for an artist. Returns empty if not supported.</summary>
    Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken cancellationToken = default);

    /// <summary>Get playlists from this provider. Returns empty if not supported.</summary>
    Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Get playlist details including tracks. Returns null if not supported.</summary>
    Task<Playlist?> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default);

    /// <summary>Check if this provider can handle a given URL (e.g., YouTube claims youtube.com URLs).
    /// Providers that return true should implement ResolveUrlAsync. Default: false.</summary>
    bool CanHandleUrl(Uri uri) => false;

    /// <summary>Resolve a URL into playable tracks (single track or playlist).
    /// Only called if CanHandleUrl returned true. Default: empty list.</summary>
    Task<List<Track>> ResolveUrlAsync(string url, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<Track>());
}

/// <summary>Flags indicating which capabilities a music provider supports</summary>
[Flags]
public enum MusicProviderCapabilities
{
    None = 0,
    Search = 1 << 0,
    TrackDetails = 1 << 1,
    Albums = 1 << 2,
    Playlists = 1 << 3,
    ArtistBrowse = 1 << 4,
    UrlPlayback = 1 << 5
}
