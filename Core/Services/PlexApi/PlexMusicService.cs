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

            // Side-populate individual caches so subsequent detail fetches are hits
            foreach (Track track in results.Tracks)
            {
                if (!string.IsNullOrEmpty(track.SourceKey))
                    cache.Set($"track:{track.SourceKey}", track, ListCacheOptions);
            }
            foreach (Album album in results.Albums)
            {
                if (!string.IsNullOrEmpty(album.SourceKey))
                    cache.Set($"albums:{album.SourceKey}", new List<Album> { album }, ListCacheOptions);
            }
            foreach (Artist artist in results.Artists)
            {
                if (!string.IsNullOrEmpty(artist.SourceKey))
                    cache.Set($"artist:{artist.SourceKey}", artist, ListCacheOptions);
            }

            return results;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            Logs.Error($"Error searching Plex library: {ex.Message}");
            throw new PlexApiException($"Failed to search Plex library: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"track:{trackKey}";
        if (cache.TryGetValue(cacheKey, out Track? cached) && cached != null)
        {
            Logs.Debug($"Track cache hit for: {trackKey}");
            return cached;
        }
        Logs.Debug($"Getting track details for key: {trackKey}");
        try
        {
            string response = await plexApiService.PerformRequestAsync(trackKey, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
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
            Track track = PlexJsonParser.ParseTrack(metadataItems.First(), plexApiService);
            cache.Set(cacheKey, track, ListCacheOptions);
            Logs.Debug($"Retrieved track details: {track.Title} by {track.Artist}");
            return track;
        }
        catch (Exception ex) when (ex is not PlexApiException)
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
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
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
            List<Track> tracks = PlexJsonParser.ParseTracksFromMetadata(metadataItems, plexApiService);
            Logs.Debug($"Retrieved {tracks.Count} tracks");
            cache.Set(cacheKey, tracks, ListCacheOptions);
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
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
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
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
                albums.Add(PlexJsonParser.ParseAlbum(item, plexApiService));
            }
            Logs.Debug($"Retrieved {albums.Count} albums");
            cache.Set(cacheKey, albums, ListCacheOptions);
            return albums;
        }
        catch (Exception ex) when (ex is not PlexApiException)
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
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
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
                playlists.Add(PlexJsonParser.ParsePlaylist(item, plexApiService));
            }
            Logs.Debug($"Retrieved {playlists.Count} playlists");
            cache.Set(cacheKey, playlists, PlaylistListCacheOptions);
            return playlists;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            Logs.Error($"Error getting playlists: {ex.Message}");
            throw new PlexApiException($"Failed to get playlists: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<Playlist> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"playlist:{playlistKey}";
        if (cache.TryGetValue(cacheKey, out Playlist? cached) && cached != null)
        {
            Logs.Debug($"Playlist details cache hit for: {playlistKey}");
            return cached;
        }
        Logs.Debug($"Getting playlist details: {playlistKey}");
        try
        {
            string response = await plexApiService.PerformRequestAsync(playlistKey, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
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
            cache.Set(cacheKey, playlist, ListCacheOptions);
            Logs.Debug($"Retrieved playlist details: {playlist.Title} with {playlist.Tracks.Count} tracks");
            return playlist;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            Logs.Error($"Error getting playlist details: {ex.Message}");
            throw new PlexApiException($"Failed to get playlist details: {ex.Message}", ex);
        }
    }

    /// <summary>Parses tracks from playlist metadata</summary>
    private List<Track> ParsePlaylistTracks(JToken metadata)
        => PlexJsonParser.ParseTracksFromMetadata(metadata, plexApiService);

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
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(jsonResponse);
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
                            results.Artists.Add(PlexJsonParser.ParseArtist(item, plexApiService));
                        }
                        break;
                    case "album":
                        foreach (JToken item in metadataItems)
                        {
                            results.Albums.Add(PlexJsonParser.ParseAlbum(item, plexApiService));
                        }
                        break;
                    case "track":
                        foreach (JToken item in metadataItems)
                        {
                            results.Tracks.Add(PlexJsonParser.ParseTrack(item, plexApiService));
                        }
                        break;
                    case "playlist":
                        foreach (JToken item in metadataItems)
                        {
                            string playlistType = item["playlistType"]?.ToString() ?? "";
                            if (playlistType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                            {
                                results.Playlists.Add(PlexJsonParser.ParsePlaylist(item, plexApiService));
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

    /// <inheritdoc />
    public async Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken cancellationToken = default)
    {
        List<Album> albums = await GetAlbumsAsync(artistKey, cancellationToken);
        if (albums.Count == 0) return [];

        // Fetch tracks from each album in parallel with bounded concurrency
        using SemaphoreSlim semaphore = new(4);
        Task<List<Track>>[] tasks = albums.Select(async album =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await GetTracksAsync(album.SourceKey, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        List<Track>[] results = await Task.WhenAll(tasks);
        List<Track> allTracks = results.SelectMany(tracks => tracks).ToList();
        Logs.Debug($"Retrieved {allTracks.Count} total tracks for artist from {albums.Count} albums");
        return allTracks;
    }

}
