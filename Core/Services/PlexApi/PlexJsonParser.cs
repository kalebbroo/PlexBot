using Newtonsoft.Json.Linq;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services;
using PlexBot.Utils;

namespace PlexBot.Core.Services.PlexApi;

/// <summary>Shared JSON parsing utilities for converting Plex API responses into domain models.
/// Used by both PlexMusicService and PlexSonicService to avoid duplication.</summary>
public static class PlexJsonParser
{
    /// <summary>Unwraps the standard Plex API envelope to get the inner content object</summary>
    public static JToken? ParseMediaContainer(string json)
    {
        JObject jObject = JObject.Parse(json);
        return jObject["MediaContainer"];
    }

    /// <summary>Converts a single Plex metadata item into a Track, resolving playback URLs and formatting duration</summary>
    public static Track ParseTrack(JToken item, IPlexApiService plexApiService)
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
            DurationDisplay = FormatHelper.FormatDuration(duration),
            Studio = item["studio"]?.ToString() ?? "N/A",
            SourceKey = item["key"]?.ToString() ?? "",
            SourceSystem = "plex"
        };
    }

    /// <summary>Converts a Plex metadata item into an Album, extracting year from release date if not directly available</summary>
    public static Album ParseAlbum(JToken item, IPlexApiService plexApiService)
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

    /// <summary>Converts a Plex metadata item into an Artist with genre and artwork resolution</summary>
    public static Artist ParseArtist(JToken item, IPlexApiService plexApiService)
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

    /// <summary>Converts a Plex metadata item into a Playlist, parsing track count and timestamps from Plex's custom fields</summary>
    public static Playlist ParsePlaylist(JToken item, IPlexApiService plexApiService)
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

    /// <summary>Joins the Genre tag array into a comma-separated display string, returning empty if none exist</summary>
    public static string GetGenresFromItem(JToken item)
    {
        JToken? genres = item["Genre"];
        if (genres is null || !genres.Any())
        {
            return "";
        }
        IEnumerable<string?> genreNames = genres.Select(g => g["tag"]?.ToString()).Where(g => !string.IsNullOrEmpty(g));
        return string.Join(", ", genreNames);
    }

    /// <summary>Pulls the numeric ID segment after "metadata" from paths like /library/metadata/12345/children</summary>
    public static string ExtractRatingKey(string sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey)) return "";
        // Source keys look like /library/metadata/12345 or /library/metadata/12345/children
        string[] segments = sourceKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Find the segment after "metadata"
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("metadata", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }
        return "";
    }

    /// <summary>Filters a Metadata array to only type=track items, skipping albums/artists that may appear in mixed results</summary>
    public static List<Track> ParseTracksFromMetadata(JToken metadata, IPlexApiService plexApiService)
    {
        List<Track> tracks = [];
        foreach (JToken item in metadata)
        {
            string type = item["type"]?.ToString() ?? "";
            if (type == "track")
            {
                tracks.Add(ParseTrack(item, plexApiService));
            }
        }
        return tracks;
    }
}
