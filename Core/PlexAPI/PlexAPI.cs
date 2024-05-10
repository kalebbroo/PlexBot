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
            //Console.WriteLine($"Performing request to: {uri}"); // debug
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
            return responseContent;
        }

        // Gets the playback URL for a media item, removing duplicated '/children' if present.
        public string GetPlaybackUrl(string partKey)
        {
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
        public async Task<Dictionary<string, Dictionary<string, string>>> SearchLibraryAsync(string query, string type)
        {
            int typeId = GetTypeID(type);
            string encodedQuery = HttpUtility.UrlEncode(query);
            string uri = $"{plexUrl}/library/sections/5/search?type={typeId}&query={encodedQuery}&limit=25";
            string? response = await PerformRequestAsync(uri);
            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("No results found.");
                throw new Exception("No results found.");
            }
            Dictionary<string, Dictionary<string, string>> results = await ParseSearchResults(response, type);
            Console.WriteLine($"Search results: {results}"); // Debugging
            return results;
        }

        private static int GetTypeID(string type)
        {
            return type.ToLower() switch
            {
                "track" => 10,
                "album" => 9,
                "artist" => 8,
                "playlist" => 15,
                _ => -1,
            };
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
            JToken? item = jObject["MediaContainer"]["Metadata"].FirstOrDefault(); // Assuming only one track is expected
            if (item == null)
            {
                Console.WriteLine("No track metadata available.");
                return null;
            }

            string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "N/A";
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
                string partKey = item.SelectToken("Media[0].Part[0].key")?.ToString() ?? "N/A";
                string playableUrl = GetPlaybackUrl(partKey);
                //Console.WriteLine($"\nEach Track:\n{item}\n"); // Debugging
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
                    ["Studio"] = item["studio"]?.ToString() ?? "N/A",
                    ["Trackkey"] = item["key"]?.ToString() ?? "N/A"
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
            Console.WriteLine(response); // Debugging
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
                    ["Title"] = item["title"]?.ToString() ?? "Unknown Album",
                    ["Url"] = item["key"]?.ToString() ?? "N/A"
                };
                albums.Add(albumDetails);
                Console.WriteLine($"Album Title: {albumDetails["Title"]}, Album URL: {albumDetails["Url"]}"); // Debugging

            }
            string json = JsonConvert.SerializeObject(albums, Formatting.Indented); // Debugging
            Console.WriteLine($"Albums: {json}"); // Debugging
            return albums;
        }
    }
}

