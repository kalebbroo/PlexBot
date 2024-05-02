using Newtonsoft.Json.Linq;
using System.Web;
using PlexBot.Core.LavaLink;

namespace PlexBot.Core.PlexAPI
{
    public class PlexApi(string plexUrl, string plexToken, LavaLinkCommands lavaLinkCommands)
    {
        private readonly string _plexURL = plexUrl;
        private readonly string _plexToken = plexToken;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLinkCommands;

        // Private method to perform the HTTP request
        public async Task<string> PerformRequestAsync(string uri)
        {
            Console.WriteLine($"Performing request to: {uri}");
            HttpClient client = new();
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Plex-Token", $"{plexToken}");
            HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Console.WriteLine($"Response status code: {response.StatusCode}");
            // if call is not successful, throw an exception
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch data from Plex: {response.StatusCode}");
                throw new Exception($"Failed to fetch data from Plex: {response.StatusCode}");
            }
            string responseContent = await response.Content.ReadAsStringAsync();
            return responseContent;
        }

        // Gets the playback URL for a media item using the part_id and the key together in the url
        public string GetPlaybackUrl(string partKey)
        {
            return $"{plexUrl}{partKey}?X-Plex-Token={plexToken}";
        }

        // Gets the URL for searching the music library
        public string GetSearchUrl(string partKey)
        {
            return $"{plexUrl}{partKey}";
        }

        // Public method to search the music library
        public async Task<Dictionary<string, Dictionary<string, string>>> SearchLibraryAsync(string query, string type)
        {
            int typeId = GetTypeID(type);
            string encodedQuery = HttpUtility.UrlEncode(query);
            string uri = $"{plexUrl}/library/sections/5/search?type={typeId}&query={encodedQuery}&limit=25";
            string response = await PerformRequestAsync(uri);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No results found.");
                throw new Exception("No results found.");
            }
            Dictionary<string, Dictionary<string, string>> results = await ParseSearchResults(response, type);
            return results;
        }

        private int GetTypeID(string type)
        {
            switch (type.ToLower())
            {
                case "track": return 10;
                case "album": return 9;
                case "artist": return 8;
                case "playlist": return 15;
                default: return -1;
            }
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
                        details["Duration"] = item["duration"]?.ToString() ?? "N/A"; // Duration in milliseconds
                        details["Studio"] = item["studio"]?.ToString() ?? "N/A";
                        Console.WriteLine(details["Url"]);
                        List<Dictionary<string, string>> queue = [details];
                        break;
                    case "artist":
                        details["Title"] = item["title"]?.ToString() ?? "Unknown Artist";
                        details["Summary"] = item["summary"]?.ToString() ?? "No description available.";
                        details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                        details["Url"] = item["key"]?.ToString() ?? "N/A";
                        // Fetch album and track count
                        try
                        {
                            var albumDetails = await GetAlbums(details["Url"]);
                            details["AlbumCount"] = albumDetails.Count.ToString();

                            int trackCount = 0;
                            foreach (var album in albumDetails)
                            {
                                var tracks = await GetTracks(album["Url"]);
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

        // Method to fetch playlists from Plex
        public async Task<Dictionary<string, Dictionary<string, string>>> GetPlaylists()
        {
            try
            {
                string uri = $"{plexUrl}/playlists?playlistType=audio";
                string response = await PerformRequestAsync(uri);
                Console.WriteLine(response); // Debugging
                return await ParseSearchResults(response, "playlist");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch playlists: {ex.Message}");
                return [];
            }
        }

        public async Task<List<Dictionary<string, string>>> GetTracks(string Key)
        {
            string uri = GetPlaybackUrl(Key);
            Console.WriteLine(uri);
            string response = await PerformRequestAsync(uri);
            Console.WriteLine(response);
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
                string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "N/A";
                string playableUrl = GetPlaybackUrl(partKey);
                Console.WriteLine($"\nEach Track:\n{item}\n"); // Debugging
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
                tracks.Add(trackDetails);
            }
            return tracks;
        }

        public async Task<List<Dictionary<string, string>>> GetAlbums(string artistKey)
        {
            string uri = GetPlaybackUrl(artistKey + "/children");
            Console.WriteLine($"Fetching albums with URI: {uri}");
            string response = await PerformRequestAsync(uri);
            Console.WriteLine(response);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No albums found.");
                return [];
            }
            List<Dictionary<string, string>> albums = [];
            JObject jObject = JObject.Parse(response);
            var items = jObject["MediaContainer"]["Metadata"];
            if (items == null)
            {
                Console.WriteLine("No album metadata available.");
                return albums;
            }
            foreach (JToken item in items)
            {
                Dictionary<string, string> albumDetails = new()
                {
                    ["Title"] = item["title"]?.ToString() ?? "Unknown Album",
                    ["Url"] = item["key"]?.ToString() ?? "N/A"
                };
                albums.Add(albumDetails);
            }
            return albums;
        }
    }
}

