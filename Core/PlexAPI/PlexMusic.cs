using System;

namespace PlexBot.Core.PlexAPI;

public class PlexMusic(ILogger<PlexMusic> logger) : PlexCore(logger)
{
    // Public method to search the music library
    public async Task<Dictionary<string, List<Dictionary<string, string>>>> SearchLibraryAsync(string query)
    {
        string encodedQuery = HttpUtility.UrlEncode(query);
        string uri = $"{plexUrl}/hubs/search?query={encodedQuery}&limit=100";
        logger.LogDebug("Performing request to URI: {uri}", uri);
        string? response = await PerformRequestAsync(uri);
        if (string.IsNullOrEmpty(response))
        {
            logger.LogError("No results found.");
            throw new Exception("No results found.");
        }
        logger.LogDebug("Response received: {Length} characters", response.Length);
        Dictionary<string, List<Dictionary<string, string>>> results = await ParseResults(response);
        logger.LogDebug("Search results: {JsonSerializeObject}", JsonConvert.SerializeObject(results));
        return results;
    }

    public async Task<Dictionary<string, List<Dictionary<string, string>>>> ParseResults(string jsonResponse)
    {
        Dictionary<string, List<Dictionary<string, string>>> results = new()
    {
        { "Artists", new List<Dictionary<string, string>>() },
        { "Albums", new List<Dictionary<string, string>>() },
        { "Tracks", new List<Dictionary<string, string>>() }
    };
        JObject jObject = JObject.Parse(jsonResponse);
        logger.LogDebug("Parsed JSON response: {JsonResponse}", jsonResponse); // Debugging
        JToken? mediaContainer = jObject["MediaContainer"];
        if (mediaContainer == null)
        {
            logger.LogWarning("MediaContainer is null in the JSON response");
            return results;
        }
        JToken? hubs = mediaContainer["Hub"];
        if (hubs == null)
        {
            logger.LogWarning("Hubs are null in the MediaContainer");
            return results;
        }
        foreach (JToken hub in hubs)
        {
            string hubType = hub["type"]?.ToString() ?? "unknown";
            logger.LogDebug("Processing hub of type: {HubType}", hubType); // Debugging
            JToken? metadataItems = hub["Metadata"];
            if (metadataItems == null)
            {
                logger.LogWarning("Metadata items are null in the Hub");
                continue;
            }
            foreach (JToken item in metadataItems)
            {
                Dictionary<string, string> details = [];
                string type = item["type"]?.ToString() ?? "unknown";
                details["Title"] = item["title"]?.ToString() ?? "Unknown Title";
                details["Type"] = type;
                details["Description"] = type switch
                {
                    "track" => item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
                    "album" => $"Album by {(item["parentTitle"]?.ToString() ?? "Unknown Artist")}",
                    "artist" => item["summary"]?.ToString() ?? "No description available.",
                    _ => "No description available."
                };
                details["Url"] = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "Url Missing in ParseResults";
                details["TrackKey"] = item["key"]?.ToString() ?? "TrackKey Missing in ParseResults";
                logger.LogDebug("Processed item: {Title}, Type: {Type}, Url: {Url}, TrackKey: {TrackKey}", details["Title"], details["Type"], details["Url"], details["TrackKey"]);
                switch (hubType)
                {
                    case "artist":
                        results["Artists"].Add(details);
                        try
                        {
                            List<Dictionary<string, string>> albumDetails = await GetAlbums(details["TrackKey"]);
                            details["AlbumCount"] = albumDetails.Count.ToString();
                            int trackCount = 0;
                            foreach (Dictionary<string, string> album in albumDetails)
                            {
                                List<Dictionary<string, string>> tracks = await GetTracks(album["TrackKey"]);
                                trackCount += tracks.Count;
                            }
                            details["TrackCount"] = trackCount.ToString();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to retrieve album or track details: {Message}", ex.Message);
                            details["AlbumCount"] = "Error";
                            details["TrackCount"] = "Error";
                        }
                        break;
                    case "album":
                        results["Albums"].Add(details);
                        try
                        {
                            List<Dictionary<string, string>> tracks = await GetTracks(details["TrackKey"]);
                            int trackCount = tracks.Count;
                            details["TrackCount"] = trackCount.ToString();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to retrieve tracks: {Message}", ex.Message);
                            details["TrackCount"] = "Error retrieving track count";
                        }
                        break;
                    case "track":
                        results["Tracks"].Add(details);
                        break;
                }
            }
        }
        logger.LogDebug($"Results came back with no errors in parse results");
        return results;
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> ParseSearchResults(string jsonResponse, string type)
    {
        Dictionary<string, Dictionary<string, string>> results = [];
        JObject jObject = JObject.Parse(jsonResponse);
        int id = 0;
        foreach (JToken item in jObject["MediaContainer"]?["Metadata"] ?? Enumerable.Empty<JToken>())
        {
            Dictionary<string, string> details = [];
            switch (type.ToLower())
            {
                case "track":
                    details["Title"] = item["title"]?.ToString() ?? "Unknown Title";
                    details["Artist"] = item["grandparentTitle"]?.ToString() ?? "Unknown Artist";
                    details["Album"] = item["parentTitle"]?.ToString() ?? "Unknown Album";
                    details["ReleaseDate"] = item["originallyAvailableAt"]?.ToString() ?? "N/A";
                    details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                    details["Url"] = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "N/A";
                    details["ArtistUrl"] = item["grandparentKey"]?.ToString() ?? "N/A";
                    details["Duration"] = item["duration"]?.ToString() ?? "N/A";
                    details["Studio"] = item["studio"]?.ToString() ?? "N/A";
                    details["TrackKey"] = item["key"]?.ToString() ?? "N/A";
                    List<Dictionary<string, string>> queue = [details];
                    break;
                case "artist":
                    details["Title"] = item["title"]?.ToString() ?? "Unknown Artist";
                    details["Summary"] = item["summary"]?.ToString() ?? "No description available.";
                    details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                    details["Url"] = item["key"]?.ToString() ?? "N/A";
                    details["TrackKey"] = item["key"]?.ToString() ?? "N/A";
                    try
                    {
                        List<Dictionary<string, string>> albumDetails = await GetAlbums(details["Url"]);
                        details["AlbumCount"] = albumDetails.Count.ToString();
                        int trackCount = 0;
                        foreach (Dictionary<string, string> album in albumDetails)
                        {
                            List<Dictionary<string, string>> tracks = await GetTracks(album["Url"]);
                            trackCount += tracks.Count;
                        }
                        details["TrackCount"] = trackCount.ToString();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to retrieve album or track details: {Message}", ex.Message);
                        details["AlbumCount"] = "Error";
                        details["TrackCount"] = "Error";
                    }
                    break;
                case "album":
                    details["Title"] = item["title"]?.ToString() ?? "Unknown Album";
                    details["Artist"] = item["parentTitle"]?.ToString() ?? "Unknown Artist";
                    details["ReleaseDate"] = item["originallyAvailableAt"]?.ToString() ?? "N/A";
                    details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                    details["Url"] = item["key"]?.ToString() ?? "N/A";
                    details["ArtistUrl"] = item["parentKey"]?.ToString() ?? "N/A";
                    details["Studio"] = item["studio"]?.ToString() ?? "N/A";
                    details["Genre"] = item["Genre"]?.ToString() ?? "N/A";
                    details["Summary"] = item["summary"]?.ToString() ?? "No description available.";
                    details["TrackKey"] = item["key"]?.ToString() ?? "N/A";
                    // TODO: Limit the summary to 100 characters. This should be done in select menu not here
                    if (details["Summary"].Length > 100)
                    {
                        details["Summary"] = details["Summary"][..97] + "...";
                    }
                    // Get track count
                    try
                    {
                        List<Dictionary<string, string>> tracks = await GetTracks(details["Url"]);
                        int trackCount = tracks.Count;
                        details["TrackCount"] = trackCount.ToString();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to retrieve tracks: {Message}", ex.Message);
                        details["TrackCount"] = "Error retrieving track count";
                    }
                    break;
                case "playlist":
                    details["Title"] = item["title"]?.ToString() ?? "Unknown Playlist";
                    details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                    details["Url"] = item["key"]?.ToString() ?? "N/A";
                    details["TrackCount"] = item["leafCount"]?.ToString() ?? "0";
                    break;
            }
            results.Add($"Item{id++}", details);
        }
        return results;
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> GetPlaylists()
    {
        try
        {
            string uri = $"{plexUrl}/playlists?playlistType=audio";
            string? response = await PerformRequestAsync(uri);
            logger.LogDebug("GetPlaylists Response: {Response}", response);
            return await ParseSearchResults(response!, "playlist") ?? throw new Exception("No playlists found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch playlists: {Message}", ex.Message);
            return [];
        }
    }

    public async Task<Dictionary<string, string>?> GetTrackDetails(string trackKey)
    {
        string uri = GetPlaybackUrl(trackKey);
        string? response = await PerformRequestAsync(uri);
        if (string.IsNullOrEmpty(response))
        {
            logger.LogWarning("No track details found.");
            return null; // Handle no data found
        }
        JObject jObject = JObject.Parse(response);
        JToken? item = (jObject["MediaContainer"]?["Metadata"] ?? Enumerable.Empty<JToken>()).FirstOrDefault()
            ?? throw new InvalidOperationException("Metadata item not found.");
        string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "Play URL Missing in GetTrackDetails";
        string playableUrl = GetPlaybackUrl(partKey);
        Dictionary<string, string> trackDetails = new()
        {
            ["Title"] = item["title"]?.ToString() ?? "Unknown Title",
            ["Artist"] = item["grandparentTitle"]?.ToString() ?? "Unknown Artist",
            ["Album"] = item["parentTitle"]?.ToString() ?? "Unknown Album",
            ["ReleaseDate"] = item["originallyAvailableAt"]?.ToString() ?? "N/A",
            ["Artwork"] = item["thumb"]?.ToString() ?? "N/A",
            ["Url"] = playableUrl,
            ["ArtistUrl"] = item["grandparentKey"]?.ToString() ?? "N/A",
            ["Duration"] = item["duration"]?.ToString() ?? "N/A", // Duration in milliseconds
            ["Studio"] = item["studio"]?.ToString() ?? "N/A"
        };

        logger.LogDebug("Track details:\nTitle: {Title}, Artist: {Artist}, Album: {Album}\nRelease Date: {ReleaseDate}, Artwork: {Artwork}, URL: {Url}\nArtist URL: {ArtistUrl}, Duration: {Duration}, Studio: {Studio}",
            trackDetails["Title"], trackDetails["Artist"], trackDetails["Album"], trackDetails["ReleaseDate"],
            trackDetails["Artwork"], trackDetails["Url"], trackDetails["ArtistUrl"], trackDetails["Duration"], trackDetails["Studio"]);

        return trackDetails;
    }

    public async Task<List<Dictionary<string, string>>> GetTracks(string Key)
    {
        string uri = GetPlaybackUrl(Key);
        string? response = await PerformRequestAsync(uri);
        if (string.IsNullOrEmpty(response))
        {
            logger.LogError("No results found.");
            throw new Exception("No results found.");
        }
        List<Dictionary<string, string>> tracks = [];
        JObject jObject = JObject.Parse(response);
        JToken? items = jObject["MediaContainer"]!["Metadata"];
        if (items == null)
        {
            logger.LogWarning("No track metadata available.");
            return tracks; // Return an empty list if no metadata found
        }
        foreach (JToken item in items)
        {
            string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "Play URL Missing in GetTracks";
            string playableUrl = GetPlaybackUrl(partKey);
            logger.LogDebug("Each Track:{item}", item);
            Dictionary<string, string> trackDetails = new()
            {
                ["Title"] = item["title"]?.ToString() ?? "Title Missing in GetTracks",
                ["Artist"] = item["grandparentTitle"]?.ToString() ?? "Artist Missing in GetTracks",
                ["Album"] = item["parentTitle"]?.ToString() ?? "Album Missing in GetTracks",
                ["ReleaseDate"] = item["originallyAvailableAt"]?.ToString() ?? "Release Date Missing in GetTracks",
                ["Artwork"] = item["thumb"]?.ToString() ?? "Artwork Missing in GetTracks",
                ["Url"] = playableUrl,
                ["ArtistUrl"] = item["grandparentKey"]?.ToString() ?? "Artist URL Missing in GetTracks",
                ["Duration"] = item["duration"]?.ToString() ?? "Duration Missing in GetTracks", // Duration in milliseconds
                ["Studio"] = item["studio"]?.ToString() ?? "Studio Missing in GetTracks",
                ["TrackKey"] = item["key"]?.ToString() ?? "TrackKey Missing in GetTracks"
            };
            tracks.Add(trackDetails);
        }
        return tracks;
    }

    public async Task<List<Dictionary<string, string>>> GetAlbums(string artistKey)
    {
        string uri = GetPlaybackUrl(artistKey);
        logger.LogDebug("Fetching albums with URI: {Uri}", uri);
        string? response = await PerformRequestAsync(uri);
        logger.LogDebug("Fetching albums response: {Response}", response);
        if (string.IsNullOrEmpty(response))
        {
            logger.LogWarning("No albums found.");
            return [];
        }
        List<Dictionary<string, string>> albums = [];
        JObject? jObject = JObject.Parse(response);
        JToken? items = jObject?["MediaContainer"]?["Metadata"];
        if (items == null)
        {
            logger.LogWarning("No album metadata available.");
            return albums;
        }
        foreach (JToken item in items)
        {
            Dictionary<string, string> albumDetails = new()
            {
                ["Title"] = item["title"]?.ToString() ?? "Title Missing in GetAlbums",
                ["Url"] = item["key"]?.ToString() ?? "Url Missing in GetAlbums",
                ["TrackKey"] = item["key"]?.ToString() ?? "TrackKey Missing in GetAlbums"
            };
            albums.Add(albumDetails);
            logger.LogDebug("Album Title: {Title}, Album URL: {Url}", albumDetails["Title"], albumDetails["Url"]);
        }

        logger.LogDebug("Albums found: {Albums}", JsonConvert.SerializeObject(albums, Formatting.Indented));

        return albums;
    }
}

