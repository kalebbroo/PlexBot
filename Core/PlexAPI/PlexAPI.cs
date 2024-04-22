using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace PlexBot.Core.PlexAPI
{
    public class PlexApi(string baseAddress, string plexToken)
    {
        private readonly string _plexURL = baseAddress;
        private readonly string _plexToken = plexToken;

        // Private method to perform the HTTP request
        public async Task<string> PerformRequestAsync(string uri)
        {
            Console.WriteLine($"Performing request to: {uri}");
            HttpClient client = new();
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Plex-Token", $"{_plexToken}");
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
            return $"{_plexURL}{partKey}?X-Plex-Token={_plexToken}";
        }

        // Gets the URL for searching the music library
        public string GetSearchUrl(string partKey)
        {
            return $"{_plexURL}{partKey}";
        }

        // Public method to search the music library
        public async Task<Dictionary<string, Dictionary<string, string>>> SearchLibraryAsync(string query, string type)
        {
            int typeId = 9;
            if (type == "track")
            {
                typeId = 10;
            }
            else if (type == "album")
            {
                typeId = 9;
            }
            else if (type == "artist")
            {
                typeId = 8;
            }
            else if (type == "playlist")
            {
                typeId = 15;
            }
            string encodedQuery = HttpUtility.UrlEncode(query);
            // Limit to 25 results Due to Discord's 25 limit on select options
            string uri = $"{_plexURL}/library/sections/5/search?type={typeId}&query={encodedQuery}&limit=25";
            string Response = await PerformRequestAsync(uri);

            // if the response is empty, throw an exception
            if (string.IsNullOrEmpty(Response))
            {
                Console.WriteLine("No results found.");
                throw new Exception("No results found.");

            }
            return await ParseSearchResults(Response, type);
        }

        // Method to refresh the music library
        public async Task<string> RefreshLibraryAsync(int libraryId)
        {
            string uri = $"{_plexURL}/library/sections/{libraryId}/refresh";
            return await PerformRequestAsync(uri);
        }

        // Method to add a new item to the music library
        public async Task<string> AddToLibraryAsync(int libraryId, string metadata)
        {
            string uri = $"{_plexURL}/library/sections/{libraryId}/all?type=12&title={metadata}";
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
                        details["Url"] = item.SelectToken("Media[0].Part[0].key")?.ToString() != null
                        ? $"{item.SelectToken("Media[0].Part[0].key")}" : "N/A";
                        Console.WriteLine(details["Url"]);
                        break;

                    case "artist":
                        details["Title"] = item["title"]?.ToString() ?? "Unknown Artist";
                        details["Summary"] = item["summary"]?.ToString() ?? "No description available.";
                        details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                        details["Url"] = item["key"]?.ToString() ?? "N/A";
                        break;

                    case "album":
                        details["Title"] = item["title"]?.ToString() ?? "Unknown Album";
                        details["Artist"] = item["parentTitle"]?.ToString() ?? "Unknown Artist";
                        details["ReleaseDate"] = item["originallyAvailableAt"]?.ToString() ?? "N/A";
                        details["Artwork"] = item["thumb"]?.ToString() ?? "N/A";
                        details["Url"] = item["key"]?.ToString() ?? "N/A";
                        details["Summary"] = item["summary"]?.ToString() ?? "No description available.";
                        if (details["Summary"].Length > 100)
                        {
                            details["Summary"] = details["Summary"][..97] + "...";
                        }

                        try
                        {
                            List<string> tracks = await GetTracks(details["Url"]);
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
            string uri = $"{_plexURL}/playlists?playlistType=audio";
            string response = await PerformRequestAsync(uri);
            Console.WriteLine(response); // Debugging
            return await ParseSearchResults(response, "playlist");
        }


        public async Task<List<string>> GetTracks(string Key)
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
            List<string> tracks = [];
            JObject jObject = JObject.Parse(response);
            var items = jObject["MediaContainer"]["Metadata"];
            if (items == null)
            {
                Console.WriteLine("No track metadata available.");
                return tracks; // Return an empty list if no metadata found
            }

            foreach (JToken item in items)
            {
                var mediaItems = item["Media"];
                if (mediaItems != null)
                {
                    foreach (var media in mediaItems)
                    {
                        var parts = media["Part"];
                        if (parts != null)
                        {
                            foreach (var part in parts)
                            {
                                var key = part["key"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(key))
                                {
                                    tracks.Add(key);
                                    Console.WriteLine(key);
                                }
                            }
                        }
                    }
                }
            }
            return tracks;
        }
    }
}

