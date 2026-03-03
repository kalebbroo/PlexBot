using Microsoft.Extensions.Caching.Memory;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services;
using PlexBot.Utils;

namespace PlexBot.Core.Services.PlexApi;

/// <summary>Provides a specialized interface for interacting with music content in Plex libraries, handling the unique metadata and hierarchical structure of music media</summary>
/// <param name="plexApiService">The underlying API service that handles authentication and raw HTTP communication with the Plex server</param>
/// <param name="cache">Memory cache for reducing redundant Plex API calls</param>
public class PlexMusicService(IPlexApiService plexApiService, IMemoryCache cache) : IPlexMusicService
{
    private static readonly MemoryCacheEntryOptions SearchCacheOptions = new() { SlidingExpiration = TimeSpan.FromSeconds(60) };
    private static readonly MemoryCacheEntryOptions ListCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(2) };
    private static readonly MemoryCacheEntryOptions PlaylistListCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(5) };

    /// <inheritdoc />
    public async Task<SearchResults> SearchLibraryAsync(string query, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"search:{query.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out SearchResults? cached) && cached != null)
        {
            Logs.Debug($"Search cache hit for: {query}");
            return cached;
        }
        Logs.Info($"Searching Plex library for: {query}");
        try
        {
            string encodedQuery = HttpUtility.UrlEncode(query);
            string uri = $"/hubs/search?query={encodedQuery}&limit=100";
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            SearchResults results = ParseSearchResults(response, query);
            Logs.Info($"Search complete. Found {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks, {results.Playlists.Count} playlists");
            cache.Set(cacheKey, results, SearchCacheOptions);
            return results;
        }
        catch (PlexApiException)
        {
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
            string response = await plexApiService.PerformRequestAsync(trackKey, cancellationToken);
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
            Track track = ParseTrack(metadataItems.First());
            Logs.Debug($"Retrieved track details: {track.Title} by {track.Artist}");
            return track;
        }
        catch (PlexApiException)
        {
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
        string cacheKey = $"tracks:{containerKey}";
        if (cache.TryGetValue(cacheKey, out List<Track>? cached) && cached != null)
        {
            Logs.Debug($"Tracks cache hit for: {containerKey}");
            return cached;
        }
        Logs.Debug($"Getting tracks for container: {containerKey}");
        try
        {
            string response = await plexApiService.PerformRequestAsync(containerKey, cancellationToken);
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
            List<Track> tracks = [];
            foreach (JToken item in metadataItems)
            {
                string type = item["type"]?.ToString() ?? "";
                if (type != "track")
                {
                    continue;
                }
                tracks.Add(ParseTrack(item));
            }
            Logs.Debug($"Retrieved {tracks.Count} tracks");
            cache.Set(cacheKey, tracks, ListCacheOptions);
            return tracks;
        }
        catch (PlexApiException)
        {
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
        string cacheKey = $"albums:{artistKey}";
        if (cache.TryGetValue(cacheKey, out List<Album>? cached) && cached != null)
        {
            Logs.Debug($"Albums cache hit for: {artistKey}");
            return cached;
        }
        Logs.Debug($"Getting albums for artist: {artistKey}");
        try
        {
            string response = await plexApiService.PerformRequestAsync(artistKey, cancellationToken);
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
            List<Album> albums = [];
            foreach (JToken item in metadataItems)
            {
                string type = item["type"]?.ToString() ?? "";
                if (type != "album")
                {
                    continue;
                }
                albums.Add(ParseAlbum(item));
            }
            Logs.Debug($"Retrieved {albums.Count} albums");
            cache.Set(cacheKey, albums, ListCacheOptions);
            return albums;
        }
        catch (PlexApiException)
        {
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
        const string cacheKey = "playlists:audio";
        if (cache.TryGetValue(cacheKey, out List<Playlist>? cached) && cached != null)
        {
            Logs.Debug("Playlists cache hit");
            return cached;
        }
        Logs.Debug("Getting audio playlists");
        try
        {
            string uri = "/playlists?playlistType=audio";
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
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
            List<Playlist> playlists = [];
            foreach (JToken item in metadataItems)
            {
                playlists.Add(ParsePlaylist(item));
            }
            Logs.Debug($"Retrieved {playlists.Count} playlists");
            cache.Set(cacheKey, playlists, PlaylistListCacheOptions);
            return playlists;
        }
        catch (PlexApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting playlists: {ex.Message}");
            throw new PlexApiException($"Failed to get playlists: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Playlist> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting playlist details: {playlistKey}");
        try
        {
            string response = await plexApiService.PerformRequestAsync(playlistKey, cancellationToken);
            JObject jObject = JObject.Parse(response);
            JToken? mediaContainer = jObject["MediaContainer"];
            if (mediaContainer == null)
            {
                Logs.Warning("MediaContainer is null in the playlist details response");
                throw new PlexApiException("Invalid playlist details response");
            }
            JToken? metadata = mediaContainer["Metadata"];
            if (metadata == null || !metadata.Any())
            {
                Logs.Warning("No metadata found in playlist details response");
                throw new PlexApiException("Invalid playlist details response");
            }
            // Playlist details come from MediaContainer level, not individual Metadata items
            Playlist playlist = new()
            {
                Id = mediaContainer["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
                Title = mediaContainer["title"]?.ToString() ?? "Unknown Playlist",
                Description = mediaContainer["summary"]?.ToString() ?? "",
                ArtworkUrl = plexApiService.GetArtworkUrl(mediaContainer["thumb"]?.ToString()),
                PlaylistUrl = playlistKey,
                SourceKey = playlistKey,
                SourceSystem = "plex"
            };
            if (int.TryParse(mediaContainer["leafCount"]?.ToString(), out int trackCount))
            {
                playlist.TrackCount = trackCount;
            }
            if (DateTimeOffset.TryParse(mediaContainer["createdAt"]?.ToString(), out DateTimeOffset createdAt))
            {
                playlist.CreatedAt = createdAt;
            }
            if (DateTimeOffset.TryParse(mediaContainer["updatedAt"]?.ToString(), out DateTimeOffset updatedAt))
            {
                playlist.UpdatedAt = updatedAt;
            }
            // Extract tracks from Metadata array
            playlist.Tracks = ParsePlaylistTracks(metadata);
            Logs.Debug($"Retrieved playlist details: {playlist.Title} with {playlist.Tracks.Count} tracks");
            return playlist;
        }
        catch (PlexApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting playlist details: {ex.Message}");
            throw new PlexApiException($"Failed to get playlist details: {ex.Message}", ex);
        }
    }

    /// <summary>Parses tracks from playlist metadata</summary>
    private List<Track> ParsePlaylistTracks(JToken metadata)
    {
        List<Track> tracks = [];
        foreach (JToken item in metadata)
        {
            string type = item["type"]?.ToString() ?? "";
            if (type != "track")
            {
                continue;
            }
            tracks.Add(ParseTrack(item));
        }
        return tracks;
    }

    /// <summary>Parses a playlist from a JToken</summary>
    private Playlist ParsePlaylist(JToken item)
    {
        Playlist playlist = new()
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Playlist",
            Description = item["summary"]?.ToString() ?? "",
            ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString()),
            PlaylistUrl = item["key"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
        if (int.TryParse(item["leafCount"]?.ToString(), out int trackCount))
        {
            playlist.TrackCount = trackCount;
        }
        if (DateTimeOffset.TryParse(item["createdAt"]?.ToString(), out DateTimeOffset createdAt))
        {
            playlist.CreatedAt = createdAt;
        }
        if (DateTimeOffset.TryParse(item["updatedAt"]?.ToString(), out DateTimeOffset updatedAt))
        {
            playlist.UpdatedAt = updatedAt;
        }
        return playlist;
    }

    /// <summary>Parses search results from a JSON response</summary>
    private SearchResults ParseSearchResults(string jsonResponse, string query)
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
            foreach (JToken hub in hubs)
            {
                string hubType = hub["type"]?.ToString() ?? "unknown";
                string hubTitle = hub["title"]?.ToString() ?? "Unknown";
                Logs.Debug($"Processing hub: {hubTitle} ({hubType})");
                JToken? metadataItems = hub["Metadata"];
                if (metadataItems == null || !metadataItems.Any())
                {
                    continue;
                }
                switch (hubType.ToLower())
                {
                    case "artist":
                        foreach (JToken item in metadataItems)
                        {
                            results.Artists.Add(ParseArtist(item));
                        }
                        break;
                    case "album":
                        foreach (JToken item in metadataItems)
                        {
                            results.Albums.Add(ParseAlbum(item));
                        }
                        break;
                    case "track":
                        foreach (JToken item in metadataItems)
                        {
                            results.Tracks.Add(ParseTrack(item));
                        }
                        break;
                    case "playlist":
                        foreach (JToken item in metadataItems)
                        {
                            string playlistType = item["playlistType"]?.ToString() ?? "";
                            if (playlistType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                            {
                                results.Playlists.Add(ParsePlaylist(item));
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

    /// <summary>Parses an artist from a JToken</summary>
    private Artist ParseArtist(JToken item)
    {
        return new Artist
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Name = item["title"]?.ToString() ?? "Unknown Artist",
            Summary = item["summary"]?.ToString() ?? "",
            ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString()),
            ArtistUrl = item["key"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex",
            Genre = GetGenresFromItem(item)
        };
    }

    /// <summary>Parses an album from a JToken</summary>
    private Album ParseAlbum(JToken item)
    {
        Album album = new()
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Album",
            Artist = item["parentTitle"]?.ToString() ?? "Unknown Artist",
            ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
            ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString()),
            AlbumUrl = item["key"]?.ToString() ?? "",
            ArtistUrl = item["parentKey"]?.ToString() ?? "",
            Studio = item["studio"]?.ToString() ?? "N/A",
            Genre = GetGenresFromItem(item),
            Summary = item["summary"]?.ToString() ?? "",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
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

    /// <summary>Parses a track from a JToken</summary>
    private Track ParseTrack(JToken item)
    {
        string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "";
        string playableUrl = plexApiService.GetPlaybackUrl(partKey);
        long.TryParse(item["duration"]?.ToString(), out long duration);
        return new Track
        {
            Id = item["ratingKey"]?.ToString() ?? Guid.NewGuid().ToString(),
            Title = item["title"]?.ToString() ?? "Unknown Title",
            Artist = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
            Album = item["parentTitle"]?.ToString() ?? "Unknown Album",
            ReleaseDate = item["originallyAvailableAt"]?.ToString() ?? "N/A",
            ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString()),
            PlaybackUrl = playableUrl,
            ArtistUrl = item["grandparentKey"]?.ToString() ?? "",
            DurationMs = duration,
            DurationDisplay = FormatDuration(duration),
            Studio = item["studio"]?.ToString() ?? "N/A",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
    }

    /// <summary>Formats a duration in milliseconds as a human-readable string</summary>
    private static string FormatDuration(long durationMs)
    {
        TimeSpan timeSpan = TimeSpan.FromMilliseconds(durationMs);
        return timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
            : $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
    }

    /// <summary>Extracts genre information from a JToken</summary>
    private static string GetGenresFromItem(JToken item)
    {
        JToken? genres = item["Genre"];
        if (genres == null || !genres.Any())
        {
            return "";
        }
        IEnumerable<string?> genreNames = genres.Select(g => g["tag"]?.ToString()).Where(g => !string.IsNullOrEmpty(g));
        return string.Join(", ", genreNames);
    }
}
