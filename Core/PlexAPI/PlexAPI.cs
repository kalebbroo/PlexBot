using Newtonsoft.Json.Linq;
using System.Web;
using PlexBot.Core.LavaLink;
using Discord;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System;
using Newtonsoft.Json;

namespace PlexBot.Core.PlexAPI
{
    public class PlexApi(string plexUrl, string plexToken, LavaLinkCommands lavaLinkCommands)
    {
        private readonly string _plexURL = plexUrl;
        private readonly string _plexToken = plexToken;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLinkCommands;

        // Private method to perform the HTTP request
        public async Task<string?> PerformRequestAsync(string uri)
        {
            Console.WriteLine($"PerformRequestAsync used this URI: {uri}"); // debug
            HttpClient client = new();
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Plex-Token", $"{plexToken}");
            HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Console.WriteLine($"Response status code: {response.StatusCode}"); // debug
            // if call is not successful, throw an exception
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch data from Plex: {response.StatusCode}");
                throw new Exception($"Failed to fetch data from Plex: {response.StatusCode}");
            }
            string? contentType = response?.Content?.Headers?.ContentType?.MediaType;
            if (contentType != "application/json")
            {
                Console.WriteLine($"Unexpected content type: {contentType}");
                return null;
            }
            string responseContent = await response!.Content.ReadAsStringAsync();
            //Console.WriteLine($"Response content: {responseContent}"); // debug
            Console.WriteLine($"Response content length: {responseContent.Length}"); // debug
            return responseContent;
        }

        // Gets the playback URL for a media item, removing duplicated '/children' if present.
        public string GetPlaybackUrl(string partKey)
        {
            // Check if the partKey already starts with "http"
            if (partKey.StartsWith("http"))
            {
                return partKey;
            }

            // TODO: Figure out why /children is duplicated in the URL Then remove this band-aid fix
            string childrenSegment = "/children";
            if (partKey.Contains(childrenSegment + childrenSegment))
            {
                partKey = partKey.Replace(childrenSegment + childrenSegment, childrenSegment);
            }
            return $"{plexUrl}{partKey}?X-Plex-Token={plexToken}";
        }

        // Gets the URL for searching the music library
        public string GetSearchUrl(string partKey)
        {
            return $"{plexUrl}{partKey}";
        }

        // Public method to search the music library
        public async Task<Dictionary<string, List<Dictionary<string, string>>>> SearchLibraryAsync(string query)
        {
            string encodedQuery = HttpUtility.UrlEncode(query);
            string uri = $"{plexUrl}/hubs/search?query={encodedQuery}&sectionId=5&limit=100";
            Console.WriteLine($"Performing request to URI: {uri}"); // Debugging
            string? response = await PerformRequestAsync(uri);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No results found.");
                throw new Exception("No results found.");
            }
            Console.WriteLine($"Response received: {response.Length} characters"); // Debugging
            Dictionary<string, List<Dictionary<string, string>>> results = await ParseResults(response);
            Console.WriteLine($"Search results: {JsonConvert.SerializeObject(results)}"); // Debugging
            return results;
        }

        // Method to refresh the music library
        public async Task<string> RefreshLibraryAsync(int libraryId)
        {
            string uri = $"{plexUrl}/library/sections/{libraryId}/refresh";
            return await PerformRequestAsync(uri);
        }
        // Method to add a new item to the music library
        public async Task<string> AddToLibraryAsync(int libraryId, string metadata)
        {
            string uri = $"{plexUrl}/library/sections/{libraryId}/all?type=12&title={metadata}";
            return await PerformRequestAsync(uri);
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
            Console.WriteLine("Parsed JSON response"); // Debugging
            JToken mediaContainer = jObject["MediaContainer"];
            if (mediaContainer == null)
            {
                Console.WriteLine("MediaContainer is null in the JSON response");
                return results;
            }
            JToken hubs = mediaContainer["Hub"];
            if (hubs == null)
            {
                Console.WriteLine("Hubs are null in the MediaContainer");
                return results;
            }
            foreach (JToken hub in hubs)
            {
                string hubType = hub["type"]?.ToString() ?? "unknown";
                Console.WriteLine($"Processing hub of type: {hubType}"); // Debugging
                JToken metadataItems = hub["Metadata"];
                if (metadataItems == null)
                {
                    Console.WriteLine("Metadata items are null in the Hub");
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
                    Console.WriteLine($"Processed item: {details["Title"]}, Type: {details["Type"]}, Url: {details["Url"]}, TrackKey: {details["TrackKey"]}"); // Debugging
                    switch (hubType)
                    {
                        case "artist":
                            results["Artists"].Add(details);
                            try
                            {
                                List<Dictionary<string, string>> albumDetails = await GetAlbums(details["TrackKey"]);
                                details["AlbumCount"] = albumDetails.Count.ToString();
                                int trackCount = 0;
                                foreach (var album in albumDetails)
                                {
                                    List<Dictionary<string, string>> tracks = await GetTracks(album["TrackKey"]);
                                    trackCount += tracks.Count;
                                }
                                details["TrackCount"] = trackCount.ToString();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Failed to retrieve album or track details: {ex.Message}");
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
                                Console.WriteLine($"Failed to retrieve tracks: {ex.Message}");
                                details["TrackCount"] = "Error retrieving track count";
                            }
                            break;
                        case "track":
                            results["Tracks"].Add(details);
                            break;
                    }
                }
            }
            Console.WriteLine($"Results came back with no errors in parse results"); // Debugging
            return results;
        }

        public async Task<Dictionary<string, Dictionary<string, string>>> ParseSearchResults(string jsonResponse, string type)
        {
            Dictionary<string, Dictionary<string, string>> results = [];
            JObject jObject = JObject.Parse(jsonResponse);
            int id = 0;
            foreach (JToken item in jObject["MediaContainer"]["Metadata"])
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
                            foreach (var album in albumDetails)
                            {
                                List<Dictionary<string, string>> tracks = await GetTracks(album["Url"]);
                                trackCount += tracks.Count;
                            }
                            details["TrackCount"] = trackCount.ToString();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to retrieve album or track details: {ex.Message}");
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
                            Console.WriteLine($"Failed to retrieve tracks: {ex.Message}");
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
                //Console.WriteLine(response); // Debugging
                return await ParseSearchResults(response, "playlist");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch playlists: {ex.Message}");
                return [];
            }
        }

        public async Task<Dictionary<string, string>> GetTrackDetails(string trackKey)
        {
            string uri = GetPlaybackUrl(trackKey);
            string? response = await PerformRequestAsync(uri);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No track details found.");
                return null; // Handle no data found
            }
            JObject jObject = JObject.Parse(response);
            JToken? item = jObject["MediaContainer"]["Metadata"].FirstOrDefault();
            if (item == null)
            {
                Console.WriteLine("No track metadata available.");
                return null;
            }
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
            Console.WriteLine($"Track details:\nTitle: {trackDetails["Title"]}, Artist: {trackDetails["Artist"]}, Album: {trackDetails["Album"]}, " +
                $"Release Date: {trackDetails["ReleaseDate"]}, Artwork: {trackDetails["Artwork"]}, URL: {trackDetails["Url"]}, " +
                $"Artist URL: {trackDetails["ArtistUrl"]}, Duration: {trackDetails["Duration"]}, Studio: {trackDetails["Studio"]}"); // Debugging
            return trackDetails;
        }

        public async Task<List<Dictionary<string, string>>> GetTracks(string Key)
        {
            string uri = GetPlaybackUrl(Key);
            string? response = await PerformRequestAsync(uri);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No results found.");
                throw new Exception("No results found.");
            }
            List<Dictionary<string, string>> tracks = [];
            JObject jObject = JObject.Parse(response);
            JToken items = jObject["MediaContainer"]["Metadata"];
            if (items == null)
            {
                Console.WriteLine("No track metadata available.");
                return tracks; // Return an empty list if no metadata found
            }
            foreach (JToken item in items)
            {
                string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "Play URL Missing in GetTracks";
                string playableUrl = GetPlaybackUrl(partKey);
                //Console.WriteLine($"\nEach Track:\n{item}\n"); // Debugging
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
            Console.WriteLine($"Fetching albums with URI: {uri}"); // Debugging
            string? response = await PerformRequestAsync(uri);
            //Console.WriteLine(response); // Debugging
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No albums found.");
                return [];
            }
            List<Dictionary<string, string>> albums = [];
            JObject? jObject = JObject.Parse(response);
            JToken? items = jObject?["MediaContainer"]?["Metadata"];
            if (items == null)
            {
                Console.WriteLine("No album metadata available.");
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
                Console.WriteLine($"Album Title: {albumDetails["Title"]}, Album URL: {albumDetails["Url"]}"); // Debugging

            }
            string json = JsonConvert.SerializeObject(albums, Formatting.Indented); // Debugging
            //Console.WriteLine($"Albums: {json}"); // Debugging
            return albums;
        }
    }
}

