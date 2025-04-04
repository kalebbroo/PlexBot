using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;

namespace PlexBot.Services;

/// <summary>
/// Defines the contract for services that interact with music content in Plex.
/// This interface abstracts the specific operations related to music media
/// (tracks, albums, artists, playlists), providing structured access to the
/// music library regardless of the underlying implementation.
/// </summary>
public interface IPlexMusicService
{
    /// <summary>
    /// Searches the Plex music library with the given query.
    /// Performs a comprehensive search across all music-related content types
    /// (artists, albums, tracks, playlists) and returns structured results that
    /// can be presented to users or used for further operations.
    /// </summary>
    /// <param name="query">The search string to query for</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>
    /// A SearchResults object containing all matching content categorized by type
    /// </returns>
    /// <exception cref="PlexApiException">Thrown when the search operation fails</exception>
    Task<SearchResults> SearchLibraryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detailed information about a specific track.
    /// Uses the track's source key (from search results or other listings) to fetch
    /// complete metadata including playback information.
    /// </summary>
    /// <param name="trackKey">The source key of the track to retrieve</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>
    /// A Track object with complete details if found; otherwise, null
    /// </returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tracks for an album, artist, or playlist.
    /// Retrieves the complete list of tracks based on the provided container key,
    /// which may represent different types of containers depending on the context.
    /// </summary>
    /// <param name="containerKey">
    /// The source key of the container (album, artist, or playlist) to retrieve tracks from
    /// </param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A list of Track objects with complete details</returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all albums by a specific artist.
    /// Uses the artist's source key (from search results or other listings) to fetch
    /// a list of all albums associated with that artist.
    /// </summary>
    /// <param name="artistKey">The source key of the artist</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A list of Album objects with complete details</returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all playlists from the Plex server.
    /// Gets a comprehensive list of all audio playlists available on the server,
    /// which may include both system-generated and user-created playlists.
    /// </summary>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A list of Playlist objects with basic details</returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detailed information about a specific playlist.
    /// Gets complete details for a playlist, including all tracks it contains
    /// and any additional metadata.
    /// </summary>
    /// <param name="playlistKey">The source key of the playlist</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A Playlist object with complete details including tracks</returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    Task<Playlist> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default);
}