using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;

namespace PlexBot.Services;

/// <summary>Defines a music-specific interface for interacting with Plex libraries, abstracting the complexities of metadata retrieval and search</summary>
public interface IPlexMusicService
{
    /// <summary>Performs a comprehensive search across the Plex music library and returns categorized results for display in user interfaces</summary>
    /// <param name="query">The user's search terms, which will be matched against titles, artists, albums, and other metadata</param>
    /// <param name="cancellationToken">Optional token to cancel long-running searches if the user abandons the request</param>
    /// <returns>A structured container with matching artists, albums, tracks and playlists ready for display in the UI</returns>
    /// <exception cref="PlexApiException">Thrown when the Plex server returns an error or is unavailable during the search operation</exception>
    Task<SearchResults> SearchLibraryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Retrieves comprehensive track details needed for playback, including direct stream URLs, artwork, and extended metadata</summary>
    /// <param name="trackKey">The unique Plex identifier for the track, usually obtained from search results or other browsing operations</param>
    /// <param name="cancellationToken">Optional token to cancel the request if the user navigates away</param>
    /// <returns>A fully-populated Track object if found, or null if the track doesn't exist or is unavailable</returns>
    /// <exception cref="PlexApiException">Thrown when the Plex server encounters an error fulfilling the metadata request</exception>
    Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default);

    /// <summary>Fetches all tracks within a container (album, artist, or playlist) to enable complete playback or queue operations</summary>
    /// <param name="containerKey">The Plex identifier for the container, which can represent an album, artist discography, or user playlist</param>
    /// <param name="cancellationToken">Optional token to cancel large retrievals if the user changes their mind</param>
    /// <returns>A chronologically ordered list of tracks with sufficient metadata for display and queuing</returns>
    /// <exception cref="PlexApiException">Thrown when the Plex server fails to return the collection's contents or the key is invalid</exception>
    Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default);

    /// <summary>Retrieves an artist's album collection for browsing and playback, supporting discography navigation in the user interface</summary>
    /// <param name="artistKey">The unique Plex identifier for the artist, typically obtained from search results</param>
    /// <param name="cancellationToken">Optional token to cancel the operation if the user navigates elsewhere</param>
    /// <returns>A list of albums by the artist, usually sorted by release date, with cover art and basic metadata</returns>
    /// <exception cref="PlexApiException">Thrown when the Plex server cannot retrieve the artist's albums or the artist doesn't exist</exception>
    Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default);

    /// <summary>Obtains all available music playlists from the Plex server for display in playlist browsing interfaces</summary>
    /// <param name="cancellationToken">Optional token to cancel the request if it takes too long</param>
    /// <returns>A collection of all music playlists on the server, including both smart playlists and user-created collections</returns>
    /// <exception cref="PlexApiException">Thrown when the Plex server cannot fulfill the playlist listing request</exception>
    Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves a complete playlist with all of its tracks, enabling both inspection and playback of the entire collection</summary>
    /// <param name="playlistKey">The unique Plex identifier for the playlist, usually obtained from GetPlaylistsAsync or search results</param>
    /// <param name="cancellationToken">Optional token to cancel retrieval of especially large playlists</param>
    /// <returns>A fully populated Playlist object containing all tracks and associated playlist metadata</returns>
    /// <exception cref="PlexApiException">Thrown when the playlist cannot be found or the server encounters an error retrieving its contents</exception>
    Task<Playlist> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default);
}