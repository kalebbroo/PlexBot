using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.PlexApi;

/// <summary>Implements Plex Sonic features using real PMS endpoints for mood/genre filtering,
/// sonic adventure (computePath), and hub-based stations. Similar tracks and radio use
/// genre/mood matching since no dedicated PMS endpoints exist for those.</summary>
public class PlexSonicService(IPlexApiService plexApiService, IMemoryCache cache) : IPlexSonicService
{
    private static readonly MemoryCacheEntryOptions PermanentCacheOptions = new() { AbsoluteExpiration = DateTimeOffset.MaxValue };
    private static readonly MemoryCacheEntryOptions TagCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30) };
    private static readonly MemoryCacheEntryOptions TrackCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(5) };
    private static readonly MemoryCacheEntryOptions SimilarCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(10) };

    /// <inheritdoc />
    public async Task<string> GetMusicSectionIdAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "plex:sectionId";
        if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        Logs.Debug("Discovering music library section ID");
        try
        {
            string response = await plexApiService.PerformRequestAsync("/library/sections", cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
            JToken? directories = mediaContainer?["Directory"];
            if (directories is null)
                throw new PlexApiException("No library sections found on Plex server");

            foreach (JToken dir in directories)
            {
                string type = dir["type"]?.ToString() ?? "";
                if (type.Equals("artist", StringComparison.OrdinalIgnoreCase))
                {
                    string sectionId = dir["key"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(sectionId))
                    {
                        Logs.Info($"Found music library section: {dir["title"]} (ID: {sectionId})");
                        cache.Set(cacheKey, sectionId, PermanentCacheOptions);
                        return sectionId;
                    }
                }
            }
            throw new PlexApiException("No music library section found on Plex server");
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to discover music section: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<MoodTag>> GetAvailableMoodsAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "plex:moods";
        if (cache.TryGetValue(cacheKey, out List<MoodTag>? cached) && cached is not null)
            return cached;

        Logs.Debug("Fetching available mood tags");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/mood?type=10";
            Logs.Debug($"Requesting mood tags: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
            JToken? directories = mediaContainer?["Directory"];

            List<MoodTag> moods = [];
            if (directories is not null)
            {
                foreach (JToken dir in directories)
                {
                    string id = dir["key"]?.ToString() ?? "";
                    string name = dir["title"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        moods.Add(new MoodTag { Id = id, Name = name, FilterKey = $"/library/sections/{sectionId}/all?type=10&mood=" });
                    }
                }
            }

            cache.Set(cacheKey, moods, TagCacheOptions);
            Logs.Info($"Found {moods.Count} mood tags");
            return moods;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get mood tags: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<GenreTag>> GetAvailableGenresAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "plex:genres";
        if (cache.TryGetValue(cacheKey, out List<GenreTag>? cached) && cached is not null)
            return cached;

        Logs.Debug("Fetching available genre tags");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/genre?type=10";
            Logs.Debug($"Requesting genre tags: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
            JToken? directories = mediaContainer?["Directory"];

            List<GenreTag> genres = [];
            if (directories is not null)
            {
                foreach (JToken dir in directories)
                {
                    string id = dir["key"]?.ToString() ?? "";
                    string name = dir["title"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        genres.Add(new GenreTag { Id = id, Name = name, FilterKey = $"/library/sections/{sectionId}/all?type=10&genre=" });
                    }
                }
            }

            cache.Set(cacheKey, genres, TagCacheOptions);
            Logs.Info($"Found {genres.Count} genre tags");
            return genres;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get genre tags: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Track>> GetMoodTracksAsync(string moodId, int limit = 50, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"mood:{moodId}:{limit}";
        if (cache.TryGetValue(cacheKey, out List<Track>? cached) && cached is not null)
            return cached;

        Logs.Debug($"Fetching tracks for mood: {moodId}");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/all?type=10&mood={Uri.EscapeDataString(moodId)}&sort=random&limit={limit}";
            Logs.Debug($"Requesting mood tracks: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            cache.Set(cacheKey, tracks, TrackCacheOptions);
            Logs.Info($"Found {tracks.Count} tracks for mood '{moodId}'");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get mood tracks: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Track>> GetGenreTracksAsync(string genreId, int limit = 50, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"genre:{genreId}:{limit}";
        if (cache.TryGetValue(cacheKey, out List<Track>? cached) && cached is not null)
            return cached;

        Logs.Debug($"Fetching tracks for genre: {genreId}");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/all?type=10&genre={Uri.EscapeDataString(genreId)}&sort=random&limit={limit}";
            Logs.Debug($"Requesting genre tracks: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            cache.Set(cacheKey, tracks, TrackCacheOptions);
            Logs.Info($"Found {tracks.Count} tracks for genre '{genreId}'");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get genre tracks: {ex.Message}", ex);
        }
    }

    /// <summary>Finds tracks that share the same artist or genre as the seed track.
    /// No dedicated PMS endpoint exists for sonic similarity — this builds a
    /// similar-feeling list by pulling the seed track's metadata and querying
    /// for other tracks in the same genre, excluding the seed artist's tracks
    /// from the first batch to add variety.</summary>
    public async Task<List<Track>> GetSimilarTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"similar:{ratingKey}";
        if (cache.TryGetValue(cacheKey, out List<Track>? cached) && cached is not null)
            return cached;

        Logs.Debug($"Building similar tracks for: {ratingKey}");
        try
        {
            string metadataUri = $"/library/metadata/{ratingKey}";
            string metadataResponse = await plexApiService.PerformRequestAsync(metadataUri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(metadataResponse);
            JToken? metadata = mediaContainer?["Metadata"]?.First;

            if (metadata is null)
            {
                Logs.Warning($"Could not fetch metadata for track {ratingKey}");
                return [];
            }

            string seedArtist = metadata["grandparentTitle"]?.ToString() ?? "";
            JToken? genreTags = metadata["Genre"];
            List<Track> similarTracks = [];
            HashSet<string> seenKeys = [ratingKey];
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);

            // Pull tracks from matching genres (excluding the seed track itself)
            if (genreTags is not null)
            {
                foreach (JToken genreTag in genreTags)
                {
                    string genreName = genreTag["tag"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(genreName)) continue;

                    string uri = $"/library/sections/{sectionId}/all?type=10&genre={Uri.EscapeDataString(genreName)}&sort=random&limit={limit}";
                    Logs.Debug($"Fetching similar tracks via genre '{genreName}': {uri}");
                    string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
                    List<Track> genreTracks = ParseTracksFromResponse(response);

                    foreach (Track track in genreTracks)
                    {
                        if (seenKeys.Contains(track.Id)) continue;
                        seenKeys.Add(track.Id);
                        similarTracks.Add(track);
                        if (similarTracks.Count >= limit) break;
                    }
                    if (similarTracks.Count >= limit) break;
                }
            }

            // If genres didn't yield enough, add tracks from the same artist
            if (similarTracks.Count < limit && !string.IsNullOrEmpty(seedArtist))
            {
                string artistKey = metadata["grandparentKey"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(artistKey))
                {
                    string artistUri = $"{artistKey}/allLeaves";
                    Logs.Debug($"Padding similar tracks with same-artist tracks: {artistUri}");
                    string artistResponse = await plexApiService.PerformRequestAsync(artistUri, cancellationToken);
                    List<Track> artistTracks = ParseTracksFromResponse(artistResponse);

                    foreach (Track track in artistTracks)
                    {
                        if (seenKeys.Contains(track.Id)) continue;
                        seenKeys.Add(track.Id);
                        similarTracks.Add(track);
                        if (similarTracks.Count >= limit) break;
                    }
                }
            }

            cache.Set(cacheKey, similarTracks, SimilarCacheOptions);
            Logs.Info($"Built {similarTracks.Count} similar tracks for '{ratingKey}'");
            return similarTracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to build similar tracks: {ex.Message}", ex);
        }
    }

    /// <summary>Uses the real PMS computePath endpoint to build a sonic adventure
    /// between two tracks — requires Plex Pass and completed Sonic Analysis</summary>
    public async Task<List<Track>> GetSonicAdventureAsync(string startRatingKey, string endRatingKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Computing sonic adventure: {startRatingKey} → {endRatingKey}");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/computePath?startID={startRatingKey}&endID={endRatingKey}";
            Logs.Debug($"Requesting sonic adventure: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            Logs.Info($"Sonic adventure: {tracks.Count} tracks from {startRatingKey} to {endRatingKey}");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to compute sonic adventure: {ex.Message}", ex);
        }
    }

    /// <summary>Builds a radio-style track list by finding the seed track's genres,
    /// then pulling a randomized mix of tracks from those genres. No dedicated
    /// PMS radio endpoint exists — this mimics Plexamp's radio behavior using
    /// standard genre filtering with random sort.</summary>
    public async Task<List<Track>> GetRadioTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Building radio tracks seeded from: {ratingKey}");
        try
        {
            string metadataUri = $"/library/metadata/{ratingKey}";
            string metadataResponse = await plexApiService.PerformRequestAsync(metadataUri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(metadataResponse);
            JToken? metadata = mediaContainer?["Metadata"]?.First;

            if (metadata is null)
            {
                Logs.Warning($"Could not fetch metadata for track {ratingKey}");
                return [];
            }

            JToken? genreTags = metadata["Genre"];
            JToken? moodTags = metadata["Mood"];
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            List<Track> radioTracks = [];
            HashSet<string> seenKeys = [ratingKey];

            // Primary: pull from the seed track's genres (randomized)
            if (genreTags is not null)
            {
                foreach (JToken genreTag in genreTags)
                {
                    string genreName = genreTag["tag"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(genreName)) continue;

                    string uri = $"/library/sections/{sectionId}/all?type=10&genre={Uri.EscapeDataString(genreName)}&sort=random&limit={limit}";
                    Logs.Debug($"Radio: fetching genre '{genreName}' tracks");
                    string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
                    List<Track> genreTracks = ParseTracksFromResponse(response);

                    foreach (Track track in genreTracks)
                    {
                        if (seenKeys.Contains(track.Id)) continue;
                        seenKeys.Add(track.Id);
                        radioTracks.Add(track);
                    }
                    if (radioTracks.Count >= limit) break;
                }
            }

            // Secondary: if genres didn't fill the quota, try mood tags
            if (radioTracks.Count < limit && moodTags is not null)
            {
                foreach (JToken moodTag in moodTags)
                {
                    string moodName = moodTag["tag"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(moodName)) continue;

                    string uri = $"/library/sections/{sectionId}/all?type=10&mood={Uri.EscapeDataString(moodName)}&sort=random&limit={limit - radioTracks.Count}";
                    Logs.Debug($"Radio: padding with mood '{moodName}' tracks");
                    string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
                    List<Track> moodTracks = ParseTracksFromResponse(response);

                    foreach (Track track in moodTracks)
                    {
                        if (seenKeys.Contains(track.Id)) continue;
                        seenKeys.Add(track.Id);
                        radioTracks.Add(track);
                    }
                    if (radioTracks.Count >= limit) break;
                }
            }

            // Shuffle to avoid genre-clustered ordering
            Random rng = new();
            radioTracks = [.. radioTracks.OrderBy(_ => rng.Next())];
            if (radioTracks.Count > limit)
                radioTracks = radioTracks.Take(limit).ToList();

            Logs.Info($"Radio generated {radioTracks.Count} tracks from seed {ratingKey}");
            return radioTracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to build radio tracks: {ex.Message}", ex);
        }
    }

    /// <summary>Fetches hub listings from the real /hubs/sections endpoint and extracts
    /// items from the music stations hub (context == hub.music.stations)</summary>
    public async Task<List<RadioStation>> GetRadioStationsAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "plex:stations";
        if (cache.TryGetValue(cacheKey, out List<RadioStation>? cached) && cached is not null)
            return cached;

        Logs.Debug("Fetching radio stations from hubs");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/hubs/sections/{sectionId}?includeStations=1";
            Logs.Debug($"Requesting hubs: {uri}");
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);

            List<RadioStation> stations = [];
            JToken? hubs = mediaContainer?["Hub"];
            if (hubs is null)
            {
                Logs.Warning("No hubs found in stations response");
                return stations;
            }

            foreach (JToken hub in hubs)
            {
                string context = hub["context"]?.ToString() ?? "";

                // Stations appear as Playlist elements (not Metadata or Directory)
                JToken? items = hub["Playlist"] ?? hub["Metadata"];
                if (items is null || !items.Any()) continue;

                // Only parse the stations hub
                if (!context.Contains("station", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (JToken item in items)
                {
                    string title = item["title"]?.ToString() ?? "";
                    string key = item["key"]?.ToString() ?? "";
                    string guid = item["guid"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(title)) continue;

                    stations.Add(new RadioStation
                    {
                        Id = guid,
                        Title = title,
                        Description = item["summary"]?.ToString() ?? "",
                        ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString() ?? item["icon"]?.ToString()),
                        Type = "station",
                        SourceKey = key,
                        StationUri = key
                    });
                }
            }

            cache.Set(cacheKey, stations, TagCacheOptions);
            Logs.Info($"Found {stations.Count} radio stations");
            return stations;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get radio stations: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetStationTracksAsync(string stationKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Fetching station tracks: {stationKey}");
        try
        {
            return await plexApiService.PerformRequestAsync(stationKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get station tracks: {ex.Message}", ex);
        }
    }

    /// <summary>Unwraps a standard Plex response envelope and delegates track parsing to PlexJsonParser</summary>
    public List<Track> ParseTracksFromResponse(string response)
    {
        JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
        JToken? metadata = mediaContainer?["Metadata"];
        // Station responses may use Track elements directly under MediaContainer
        metadata ??= mediaContainer?["Track"];
        if (metadata is null) return [];
        return PlexJsonParser.ParseTracksFromMetadata(metadata, plexApiService);
    }
}
