using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Core.Services.PlexApi;

/// <summary>Implements Plex Sonic features: radio stations, mood/genre browsing,
/// sonically similar tracks, and sonic adventure paths</summary>
public class PlexSonicService(IPlexApiService plexApiService, IMemoryCache cache) : IPlexSonicService
{
    private static readonly MemoryCacheEntryOptions PermanentCacheOptions = new() { AbsoluteExpiration = DateTimeOffset.MaxValue };
    private static readonly MemoryCacheEntryOptions TagCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(30) };
    private static readonly MemoryCacheEntryOptions RadioCacheOptions = new() { SlidingExpiration = TimeSpan.FromMinutes(5) };
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
            List<MoodTag> moods = await GetFilterTagsAsync<MoodTag>(
                sectionId, "mood", (id, name, key) => new MoodTag { Id = id, Name = name, FilterKey = key },
                cancellationToken);

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
            List<GenreTag> genres = await GetFilterTagsAsync<GenreTag>(
                sectionId, "genre", (id, name, key) => new GenreTag { Id = id, Name = name, FilterKey = key },
                cancellationToken);

            cache.Set(cacheKey, genres, TagCacheOptions);
            Logs.Info($"Found {genres.Count} genre tags");
            return genres;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get genre tags: {ex.Message}", ex);
        }
    }

    /// <summary>Walks the section details response to find filter tag values, trying both FieldType→Filter
    /// and Type→Field→SubType paths since Plex server versions structure this differently</summary>
    public async Task<List<T>> GetFilterTagsAsync<T>(
        string sectionId, string filterType, Func<string, string, string, T> factory,
        CancellationToken cancellationToken)
    {
        string filterUri = $"/library/sections/{sectionId}/all?type=10&{filterType}=";
        string detailsUri = $"/library/sections/{sectionId}?includeDetails=1";
        string response = await plexApiService.PerformRequestAsync(detailsUri, cancellationToken);
        JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);

        List<T> tags = [];

        JToken? fieldTypes = mediaContainer?["FieldType"];
        if (fieldTypes is not null)
        {
            foreach (JToken fieldType in fieldTypes)
            {
                string type = fieldType["type"]?.ToString() ?? "";
                if (!type.Equals("tag", StringComparison.OrdinalIgnoreCase))
                    continue;

                JToken? filters = fieldType["Filter"];
                if (filters is null) continue;

                foreach (JToken filter in filters)
                {
                    string fFilter = filter["filter"]?.ToString() ?? "";
                    if (!fFilter.Equals(filterType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string filterKey = filter["key"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(filterKey)) continue;

                    string tagResponse = await plexApiService.PerformRequestAsync(filterKey, cancellationToken);
                    JToken? tagContainer = PlexJsonParser.ParseMediaContainer(tagResponse);
                    JToken? directories = tagContainer?["Directory"];
                    if (directories is null) continue;

                    foreach (JToken dir in directories)
                    {
                        string id = dir["key"]?.ToString() ?? "";
                        string name = dir["title"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            tags.Add(factory(id, name, filterKey));
                        }
                    }
                    return tags;
                }
            }
        }

        // Fallback: try the Type array approach
        JToken? types = mediaContainer?["Type"];
        if (types is not null)
        {
            foreach (JToken typeEntry in types)
            {
                // Look for track type (type=10)
                if (typeEntry["type"]?.ToString() != "10") continue;

                JToken? fieldEntries = typeEntry["Field"];
                if (fieldEntries is null) continue;

                foreach (JToken field in fieldEntries)
                {
                    string key = field["key"]?.ToString() ?? "";
                    if (!key.Equals(filterType, StringComparison.OrdinalIgnoreCase)) continue;

                    // The subtype entries contain the values
                    JToken? subTypes = field["SubType"];
                    if (subTypes is null) continue;

                    foreach (JToken sub in subTypes)
                    {
                        string id = sub["key"]?.ToString() ?? "";
                        string name = sub["title"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            tags.Add(factory(id, name, $"/library/sections/{sectionId}/all?type=10&{filterType}="));
                        }
                    }
                    return tags;
                }
            }
        }

        return tags;
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
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            cache.Set(cacheKey, tracks, RadioCacheOptions);
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
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            cache.Set(cacheKey, tracks, RadioCacheOptions);
            Logs.Info($"Found {tracks.Count} tracks for genre '{genreId}'");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get genre tracks: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Track>> GetSimilarTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"similar:{ratingKey}";
        if (cache.TryGetValue(cacheKey, out List<Track>? cached) && cached is not null)
            return cached;

        Logs.Debug($"Fetching similar tracks for: {ratingKey}");
        try
        {
            string uri = $"/library/metadata/{ratingKey}/nearest?limit={limit}&maxDistance=0.25";
            string response = await plexApiService.PerformRequestAsync(uri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(response);

            cache.Set(cacheKey, tracks, SimilarCacheOptions);
            Logs.Info($"Found {tracks.Count} similar tracks for '{ratingKey}'");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get similar tracks: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<List<Track>> GetSonicAdventureAsync(string startRatingKey, string endRatingKey, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Computing sonic adventure: {startRatingKey} → {endRatingKey}");
        try
        {
            string sectionId = await GetMusicSectionIdAsync(cancellationToken);
            string uri = $"/library/sections/{sectionId}/computePath?startID={startRatingKey}&endID={endRatingKey}";
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

    /// <inheritdoc />
    public async Task<List<Track>> GetRadioTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default)
    {
        Logs.Debug($"Getting radio tracks seeded from: {ratingKey}");
        try
        {
            string metadataUri = $"/library/metadata/{ratingKey}?includeStations=1";
            string metadataResponse = await plexApiService.PerformRequestAsync(metadataUri, cancellationToken);
            JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(metadataResponse);

            string? stationKey = FindStationKey(mediaContainer);
            if (string.IsNullOrEmpty(stationKey))
            {
                Logs.Warning($"No radio station found for item {ratingKey}");
                return [];
            }

            // PlayQueue creation requires the full server://machineId/... URI format
            string machineId = await plexApiService.GetMachineIdentifierAsync(cancellationToken);
            string stationUri = $"server://{machineId}/com.plexapp.plugins.library{stationKey}";

            string playQueueUri = $"/playQueues?type=audio&uri={Uri.EscapeDataString(stationUri)}&repeat=0&shuffle=1&limit={limit}";
            string playQueueResponse = await plexApiService.PerformPostRequestAsync(playQueueUri, cancellationToken);
            List<Track> tracks = ParseTracksFromResponse(playQueueResponse);

            Logs.Info($"Radio generated {tracks.Count} tracks from seed {ratingKey}");
            return tracks;
        }
        catch (Exception ex) when (ex is not PlexApiException)
        {
            throw new PlexApiException($"Failed to get radio tracks: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
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
                string hubTitle = hub["title"]?.ToString() ?? "";

                JToken? metadata = hub["Metadata"];
                if (metadata is null || !metadata.Any()) continue;

                foreach (JToken item in metadata)
                {
                    string type = item["type"]?.ToString() ?? "";
                    string ratingKey = item["ratingKey"]?.ToString() ?? "";
                    string title = item["title"]?.ToString() ?? hubTitle;

                    if (string.IsNullOrEmpty(ratingKey)) continue;

                    stations.Add(new RadioStation
                    {
                        Id = ratingKey,
                        Title = title,
                        Description = item["summary"]?.ToString() ?? "",
                        ArtworkUrl = plexApiService.GetArtworkUrl(item["thumb"]?.ToString()),
                        Type = context.Contains("station", StringComparison.OrdinalIgnoreCase) ? "station" : type,
                        SourceKey = ratingKey
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

    /// <summary>Unwraps a standard Plex response envelope and delegates track parsing to PlexJsonParser</summary>
    public List<Track> ParseTracksFromResponse(string response)
    {
        JToken? mediaContainer = PlexJsonParser.ParseMediaContainer(response);
        JToken? metadata = mediaContainer?["Metadata"];
        if (metadata is null) return [];
        return PlexJsonParser.ParseTracksFromMetadata(metadata, plexApiService);
    }

    /// <summary>Searches multiple locations in the response for a station key since Plex stores it
    /// under Metadata[].Station, Metadata[].Stations, or directly at the container level</summary>
    public static string? FindStationKey(JToken? mediaContainer)
    {
        if (mediaContainer is null) return null;

        JToken? metadata = mediaContainer["Metadata"];
        if (metadata is null) return null;

        foreach (JToken item in metadata)
        {
            JToken? stations = item["Station"];
            if (stations is not null && stations.Any())
            {
                string? key = stations.First?["key"]?.ToString();
                if (!string.IsNullOrEmpty(key)) return key;
            }

            JToken? stationsPlural = item["Stations"];
            if (stationsPlural is not null && stationsPlural.Any())
            {
                string? key = stationsPlural.First?["key"]?.ToString();
                if (!string.IsNullOrEmpty(key)) return key;
            }
        }

        JToken? containerStations = mediaContainer["Station"] ?? mediaContainer["Stations"];
        if (containerStations is not null && containerStations.Any())
        {
            return containerStations.First?["key"]?.ToString();
        }

        return null;
    }
}
