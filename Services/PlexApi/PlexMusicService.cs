using PlexBot.Core.Exceptions;
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Services.PlexApi;

/// <summary>
/// Service for interacting with music content in a Plex server.
/// This service provides methods specifically for working with music media types
/// (tracks, albums, artists, playlists), handling the unique aspects of music
/// metadata and organization in Plex libraries.
/// </summary>
public class PlexMusicService : IPlexMusicService
{
    private readonly IPlexApiService _plexApiService;
    private readonly string _plexUrl;
    private readonly string _plexToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexMusicService"/> class.
    /// Sets up the service with necessary dependencies and configuration.
    /// </summary>
    /// <param name="plexApiService">Service for making Plex API requests</param>
    public PlexMusicService(IPlexApiService plexApiService)
    {
        _plexApiService = plexApiService ?? throw new ArgumentNullException(nameof(plexApiService));

        // For efficiency, keep the URL and token in member variables
        // These are used for building image URLs and other non-request scenarios
        _plexUrl = Environment.GetEnvironmentVariable("PLEX_URL") ?? "";
        _plexToken = Environment.GetEnvironmentVariable("PLEX_TOKEN") ?? "";

        Logs.Init("PlexMusicService initialized");
    }

    /// <inheritdoc />
    public async Task<SearchResults> SearchLibraryAsync(string query, CancellationToken cancellationToken = default)
    {
        Logs.Info($"Searching Plex library for: {query}");

        try
        {
            // URL encode the query
            string encodedQuery = HttpUtility.UrlEncode(query);

            // Construct the search URL
            string uri = $"/hubs/search?query={encodedQuery}&limit=100";

            // Perform the request
            string response = await _plexApiService.PerformRequestAsync(uri, cancellationToken);

            // Parse the results
            SearchResults results = ParseSearchResults(response, query, cancellationToken);

            Logs.Info($"Search complete. Found {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks, {results.Playlists.Count} playlists");
            return results;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error during search: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error searching Plex library: {ex.Message}");
            throw new PlexApiException($"Failed to search Plex library: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting track details for key: {trackKey}");

        try
        {
            // Perform the request to get track details
            string response = await _plexApiService.PerformRequestAsync(trackKey, cancellationToken);

            // Parse the response
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];

            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the track details response");
                return null;
            }

            JToken? metadataItems = mediaContainer["Metadata"];

            if (metadataItems == null || !metadataItems.Any())
            {
                Logs.Warning("No metadata items found in track details response");
                return null;
            }

            // Get the first (and should be only) item
            JToken item = metadataItems.First();

            // Get the playback URL
            string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "";
            string playableUrl = _plexApiService.GetPlaybackUrl(partKey);

            // Create the track object
            Track track = new()
            {
                Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                Title = item["title"]?.ToString() ?? "Unknown Title",
                Artist = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
                Album = item["parentTitle"]?.ToString() ?? "Unknown Album",
                ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
                ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
                PlaybackUrl = playableUrl,
                ArtistUrl = item["grandparentKey"]?.ToString() ?? "",
                DurationMs = long.TryParse(item["duration"]?.ToString(), out long duration) ? duration : 0,
                DurationDisplay = FormatDuration(duration),
                Studio = item["studio"]?.ToString() ?? "N/A",
                SourceKey = item["key"]?.ToString() ?? "",
                SourceSystem = "plex"
            };

            Logs.Debug($"Retrieved track details: {track.Title} by {track.Artist}");
            return track;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error getting track details: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting track details: {ex.Message}");
            throw new PlexApiException($"Failed to get track details: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting tracks for container: {containerKey}");

        try
        {
            // Perform the request
            string response = await _plexApiService.PerformRequestAsync(containerKey, cancellationToken);

            // Parse the response
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];

            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the tracks response");
                return [];
            }

            JToken? metadataItems = mediaContainer["Metadata"];

            if (metadataItems == null)
            {
                Logs.Warning("No metadata items found in tracks response");
                return [];
            }

            // Process each track
            List<Track> tracks = [];

            foreach (JToken item in metadataItems)
            {
                // Skip non-track items
                string type = item["type"]?.ToString() ?? "";
                if (type != "track")
                {
                    continue;
                }

                // Get the playback URL
                string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "";
                string playableUrl = _plexApiService.GetPlaybackUrl(partKey);

                // Create the track object
                Track track = new()
                {
                    Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                    Title = item["title"]?.ToString() ?? "Unknown Title",
                    Artist = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
                    Album = item["parentTitle"]?.ToString() ?? "Unknown Album",
                    ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
                    ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
                    PlaybackUrl = playableUrl,
                    ArtistUrl = item["grandparentKey"]?.ToString() ?? "",
                    DurationMs = long.TryParse(item["duration"]?.ToString(), out long duration) ? duration : 0,
                    DurationDisplay = FormatDuration(duration),
                    Studio = item["studio"]?.ToString() ?? "N/A",
                    SourceKey = item["key"]?.ToString() ?? "",
                    SourceSystem = "plex"
                };

                tracks.Add(track);
            }

            Logs.Debug($"Retrieved {tracks.Count} tracks");
            return tracks;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error getting tracks: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting tracks: {ex.Message}");
            throw new PlexApiException($"Failed to get tracks: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting albums for artist: {artistKey}");
        try
        {
            // Perform the request
            string response = await _plexApiService.PerformRequestAsync(artistKey, cancellationToken);

            // Parse the response
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];

            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the albums response");
                return [];
            }

            JToken? metadataItems = mediaContainer["Metadata"];

            if (metadataItems == null)
            {
                Logs.Warning("No metadata items found in albums response");
                return [];
            }

            // Process each album
            List<Album> albums = [];

            foreach (JToken item in metadataItems)
            {
                // Skip non-album items
                string type = item["type"]?.ToString() ?? "";
                if (type != "album")
                {
                    continue;
                }

                // Create the album object
                Album album = new()
                {
                    Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                    Title = item["title"]?.ToString() ?? "Unknown Album",
                    Artist = item["parentTitle"]?.ToString() ?? "Unknown Artist",
                    ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
                    ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
                    AlbumUrl = item["key"]?.ToString() ?? "",
                    ArtistUrl = item["parentKey"]?.ToString() ?? "",
                    Studio = item["studio"]?.ToString() ?? "N/A",
                    Genre = GetGenresFromItem(item),
                    Summary = item["summary"]?.ToString() ?? "",
                    SourceKey = item["key"]?.ToString() ?? "",
                    SourceSystem = "plex"
                };

                // Try to parse the year
                if (int.TryParse(item["year"]?.ToString(), out int year))
                {
                    album.Year = year;
                }
                else
                {
                    album.ExtractYear();
                }

                albums.Add(album);
            }

            Logs.Debug($"Retrieved {albums.Count} albums");
            return albums;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error getting albums: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting albums: {ex.Message}");
            throw new PlexApiException($"Failed to get albums: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        Logs.Debug("Getting audio playlists");

        try
        {
            // Perform the request
            string uri = "/playlists?playlistType=audio";
            string response = await _plexApiService.PerformRequestAsync(uri, cancellationToken);

            // Parse the response
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];

            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the playlists response");
                return [];
            }

            JToken? metadataItems = mediaContainer["Metadata"];

            if (metadataItems == null)
            {
                Logs.Warning("No metadata items found in playlists response");
                return [];
            }

            // Process each playlist
            List<Playlist> playlists = [];

            foreach (JToken item in metadataItems)
            {
                // Create the playlist object
                Playlist playlist = new()
                {
                    Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                    Title = item["title"]?.ToString() ?? "Unknown Playlist",
                    Description = item["summary"]?.ToString() ?? "",
                    ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
                    PlaylistUrl = item["key"]?.ToString() ?? "",
                    SourceKey = item["key"]?.ToString() ?? "",
                    SourceSystem = "plex"
                };

                // Try to parse track count
                if (int.TryParse(item["leafCount"]?.ToString(), out int trackCount))
                {
                    playlist.TrackCount = trackCount;
                }

                // Try to parse creation date
                if (DateTimeOffset.TryParse(item["createdAt"]?.ToString(), out DateTimeOffset createdAt))
                {
                    playlist.CreatedAt = createdAt;
                }

                // Try to parse update date
                if (DateTimeOffset.TryParse(item["updatedAt"]?.ToString(), out DateTimeOffset updatedAt))
                {
                    playlist.UpdatedAt = updatedAt;
                }

                playlists.Add(playlist);
            }

            Logs.Debug($"Retrieved {playlists.Count} playlists");
            return playlists;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error getting playlists: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting playlists: {ex.Message}");
            throw new PlexApiException($"Failed to get playlists: {ex.Message}", ex);
        }
    }

    /// <summary>Retrieves detailed information about a specific playlist.
    /// Gets complete details for a playlist, including all tracks it contains
    /// and any additional metadata.</summary>
    /// <param name="playlistKey">The source key of the playlist</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A Playlist object with complete details including tracks</returns>
    /// <exception cref="PlexApiException">Thrown when retrieval fails</exception>
    public async Task<Playlist> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting playlist details: {playlistKey}");
        try
        {
            // First get the playlist metadata
            string response = await _plexApiService.PerformRequestAsync(playlistKey, cancellationToken);
            // Parse the response for playlist details
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];
            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the playlist details response");
                throw new PlexApiException("Invalid playlist details response");
            }
            // Get the metadata array
            JToken? metadata = mediaContainer["Metadata"];
            if (metadata == null || !metadata.Any())
            {
                Logs.Warning("No metadata found in playlist details response");
                throw new PlexApiException("Invalid playlist details response");
            }
            // Create the playlist object
            Playlist playlist = new()
            {
                Id = mediaContainer["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                Title = mediaContainer["title"]?.ToString() ?? "Unknown Playlist",
                Description = mediaContainer["summary"]?.ToString() ?? "",
                ArtworkUrl = FormatArtworkUrl(mediaContainer["thumb"]?.ToString()),
                PlaylistUrl = playlistKey,
                SourceKey = playlistKey,
                SourceSystem = "plex"
            };
            // Try to parse track count
            if (int.TryParse(mediaContainer["leafCount"]?.ToString(), out int trackCount))
            {
                playlist.TrackCount = trackCount;
            }
            // Try to parse creation date
            if (DateTimeOffset.TryParse(mediaContainer["createdAt"]?.ToString(), out DateTimeOffset createdAt))
            {
                playlist.CreatedAt = createdAt;
            }
            // Try to parse update date
            if (DateTimeOffset.TryParse(mediaContainer["updatedAt"]?.ToString(), out DateTimeOffset updatedAt))
            {
                playlist.UpdatedAt = updatedAt;
            }
            // Extract tracks from the same response - MediaContainer.Metadata contains the tracks
            playlist.Tracks = ParsePlaylistTracks(metadata);
            Logs.Debug($"Retrieved playlist details: {playlist.Title} with {playlist.Tracks.Count} tracks");
            return playlist;
        }
        catch (PlexApiException ex)
        {
            Logs.Error($"Plex API error getting playlist details: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting playlist details: {ex.Message}");
            throw new PlexApiException($"Failed to get playlist details: {ex.Message}", ex);
        }
    }

    /// <summary>Parses tracks from playlist metadata.
    /// Extracts track information directly from the metadata array in the playlist response.</summary>
    /// <param name="metadata">The metadata array from the Plex API response</param>
    /// <returns>A list of Track objects</returns>
    private List<Track> ParsePlaylistTracks(JToken metadata)
    {
        List<Track> tracks = [];

        foreach (JToken item in metadata)
        {
            // Only process items that are tracks
            string type = item["type"]?.ToString() ?? "";
            if (type != "track")
            {
                continue;
            }
            // Get the playback URL
            string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "";
            string playableUrl = _plexApiService.GetPlaybackUrl(partKey);
            // Parse duration
            long.TryParse(item["duration"]?.ToString(), out long duration);
            Track track = new()
            {
                Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                Title = item["title"]?.ToString() ?? "Unknown Title",
                Artist = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
                Album = item["parentTitle"]?.ToString() ?? "Unknown Album",
                ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
                ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
                PlaybackUrl = playableUrl,
                ArtistUrl = item["grandparentKey"]?.ToString() ?? "",
                DurationMs = duration,
                DurationDisplay = FormatDuration(duration),
                Studio = item["studio"]?.ToString() ?? "N/A",
                SourceKey = item["key"]?.ToString() ?? "",
                SourceSystem = "plex"
            };
            tracks.Add(track);
        }
        return tracks;
    }

    /// <summary>Parses a playlist from a JToken.
    /// Extracts playlist metadata from the Plex API response into a structured object.</summary>
    /// <param name="item">The JToken containing playlist metadata</param>
    /// <returns>A Playlist object with the extracted metadata</returns>
    private Playlist ParsePlaylist(JToken item) // TODO: Check if this is actually needed could be redundant
    {
        Playlist playlist = new()
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Playlist",
            Description = item["summary"]?.ToString() ?? "",
            ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
            PlaylistUrl = item["key"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
        // Try to parse track count
        if (int.TryParse(item["leafCount"]?.ToString(), out int trackCount))
        {
            playlist.TrackCount = trackCount;
        }
        // Try to parse creation date
        if (DateTimeOffset.TryParse(item["createdAt"]?.ToString(), out DateTimeOffset createdAt))
        {
            playlist.CreatedAt = createdAt;
        }
        // Try to parse update date
        if (DateTimeOffset.TryParse(item["updatedAt"]?.ToString(), out DateTimeOffset updatedAt))
        {
            playlist.UpdatedAt = updatedAt;
        }
        return playlist;
    }

    /// <summary>
    /// Parses search results from a JSON response.
    /// Extracts and organizes search results from the Plex API response into a
    /// structured object with categorized results.
    /// </summary>
    /// <param name="jsonResponse">The JSON response from the Plex API</param>
    /// <param name="query">The original search query</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A SearchResults object containing all matching content</returns>
    private SearchResults ParseSearchResults(string jsonResponse, string query, CancellationToken cancellationToken)
    {
        SearchResults results = new()
        {
            Query = query,
            SourceSystem = "plex"
        };

        try
        {
            JObject jObject = JObject.Parse(jsonResponse);
            JToken? mediaContainer = jObject["MediaContainer"];

            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the search response");
                return results;
            }

            JToken? hubs = mediaContainer["Hub"];

            if (hubs == null)
            {
                Logs.Warning("Hubs are null in the MediaContainer");
                return results;
            }

            // Process each hub
            foreach (JToken hub in hubs)
            {
                string hubType = hub["type"]?.ToString() ?? "unknown";
                string hubTitle = hub["title"]?.ToString() ?? "Unknown";

                Logs.Debug($"Processing hub: {hubTitle} ({hubType})");

                // Skip hubs without metadata
                JToken? metadataItems = hub["Metadata"];
                if (metadataItems == null || !metadataItems.Any())
                {
                    continue;
                }

                // Process items by hub type
                switch (hubType.ToLower())
                {
                    case "artist":
                        foreach (JToken item in metadataItems)
                        {
                            Artist artist = ParseArtist(item);
                            results.Artists.Add(artist);
                        }
                        break;

                    case "album":
                        foreach (JToken item in metadataItems)
                        {
                            Album album = ParseAlbum(item);
                            results.Albums.Add(album);
                        }
                        break;

                    case "track":
                        foreach (JToken item in metadataItems)
                        {
                            Track track = ParseTrack(item);
                            results.Tracks.Add(track);
                        }
                        break;

                    case "playlist":
                        foreach (JToken item in metadataItems)
                        {
                            // Only include audio playlists
                            string playlistType = item["playlistType"]?.ToString() ?? "";
                            if (playlistType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                            {
                                Playlist playlist = ParsePlaylist(item);
                                results.Playlists.Add(playlist);
                            }
                        }
                        break;
                }
            }

            Logs.Debug($"Parsed search results: {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks, {results.Playlists.Count} playlists");
            return results;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error parsing search results: {ex.Message}");
            throw new PlexApiException($"Failed to parse search results: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses an artist from a JToken.
    /// Extracts artist metadata from the Plex API response into a structured object.
    /// </summary>
    /// <param name="item">The JToken containing artist metadata</param>
    /// <returns>An Artist object with the extracted metadata</returns>
    private Artist ParseArtist(JToken item)
    {
        return new Artist
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Name = item["title"]?.ToString() ?? "Unknown Artist",
            Summary = item["summary"]?.ToString() ?? "",
            ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
            ArtistUrl = item["key"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex",
            Genre = GetGenresFromItem(item)
        };
    }

    /// <summary>
    /// Parses an album from a JToken.
    /// Extracts album metadata from the Plex API response into a structured object.
    /// </summary>
    /// <param name="item">The JToken containing album metadata</param>
    /// <returns>An Album object with the extracted metadata</returns>
    private Album ParseAlbum(JToken item)
    {
        Album album = new()
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Album",
            Artist = item["parentTitle"]?.ToString() ?? "Unknown Artist",
            ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
            ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
            AlbumUrl = item["key"]?.ToString() ?? "",
            ArtistUrl = item["parentKey"]?.ToString() ?? "",
            Studio = item["studio"]?.ToString() ?? "N/A",
            Genre = GetGenresFromItem(item),
            Summary = item["summary"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };

        // Try to parse the year
        if (int.TryParse(item["year"]?.ToString(), out int year))
        {
            album.Year = year;
        }
        else
        {
            album.ExtractYear();
        }

        return album;
    }

    /// <summary>
    /// Parses a track from a JToken.
    /// Extracts track metadata from the Plex API response into a structured object.
    /// </summary>
    /// <param name="item">The JToken containing track metadata</param>
    /// <returns>A Track object with the extracted metadata</returns>
    private Track ParseTrack(JToken item)
    {
        // Get the playback URL
        string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "";
        string playableUrl = _plexApiService.GetPlaybackUrl(partKey);

        long.TryParse(item["duration"]?.ToString(), out long duration);

        return new Track
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Title",
            Artist = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
            Album = item["parentTitle"]?.ToString() ?? "Unknown Album",
            ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
            ArtworkUrl = FormatArtworkUrl(item["thumb"]?.ToString()),
            PlaybackUrl = playableUrl,
            ArtistUrl = item["grandparentKey"]?.ToString() ?? "",
            DurationMs = duration,
            DurationDisplay = FormatDuration(duration),
            Studio = item["studio"]?.ToString() ?? "N/A",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
    }

    /// <summary>
    /// Formats an artwork URL to include the server URL and token.
    /// Converts a relative artwork path from Plex into a full URL that can be used
    /// to display the artwork.
    /// </summary>
    /// <param name="artworkPath">The relative artwork path from Plex</param>
    /// <returns>A full URL to the artwork</returns>
    private string FormatArtworkUrl(string? artworkPath)
    {
        if (string.IsNullOrEmpty(artworkPath))
        {
            return ""; // No artwork available
        }

        // If it's already a full URL, just return it
        if (artworkPath.StartsWith("http"))
        {
            return artworkPath;
        }

        // Otherwise, prepend the Plex server URL and append the token
        return $"{_plexUrl}{artworkPath}?X-Plex-Token={_plexToken}";
    }

    /// <summary>
    /// Formats a duration in milliseconds as a human-readable string.
    /// Converts a raw duration value into a formatted string like "3:45" or "1:23:45".
    /// </summary>
    /// <param name="durationMs">The duration in milliseconds</param>
    /// <returns>A formatted duration string</returns>
    private static string FormatDuration(long durationMs)
    {
        TimeSpan timeSpan = TimeSpan.FromMilliseconds(durationMs);
        // Format as mm:ss or hh:mm:ss depending on length
        return timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
            : $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    /// <summary>
    /// Extracts genre information from a JToken.
    /// Parses the Genre array from Plex metadata and returns a comma-separated list.
    /// </summary>
    /// <param name="item">The JToken containing genre metadata</param>
    /// <returns>A comma-separated list of genres</returns>
    private static string GetGenresFromItem(JToken item)
    {
        JToken? genres = item["Genre"];

        if (genres == null || !genres.Any())
        {
            return "";
        }

        // Extract genre names and join them with commas
        var genreNames = genres.Select(g => g["tag"]?.ToString()).Where(g => !string.IsNullOrEmpty(g));
        return string.Join(", ", genreNames);
    }
}