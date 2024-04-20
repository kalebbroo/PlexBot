using System;
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
        private static async Task<string> PerformRequestAsync(string uri)
        {
            HttpClient client = new();
            HttpRequestMessage request = new(HttpMethod.Get, uri);
            HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine(responseContent);
            return responseContent;
        }

        // Public method to search the music library
        public async Task<List<MediaItem>> SearchLibraryAsync(string query, string type)
        {
            string encodedQuery = HttpUtility.UrlEncode(query);
            string uri = $"{_plexURL}/hubs/search?query={encodedQuery}&limit=50&X-Plex-Token={_plexToken}";
            string xmlResponse = await PerformRequestAsync(uri);
            return ParseSearchResults(xmlResponse, type);
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

        // Parses the XML search results based on type and extracts media part details if available
        private static List<MediaItem> ParseSearchResults(string xmlContent, string type)
        {
            XDocument doc = XDocument.Parse(xmlContent);
            List<MediaItem> items = [];

            // Depending on the type, the element might be "Track", "Artist", or "Album"
            string elementType = type[..1].ToUpper() + type[1..].ToLower();  // Capitalize first letter
            foreach (var element in doc.Descendants(elementType))
            {
                if (element.Attribute("type")?.Value == type)
                {
                    var mediaItem = new MediaItem
                    {
                        Title = element.Attribute("title")?.Value,
                        Key = element.Attribute("key")?.Value,
                        Type = element.Attribute("type")?.Value,
                        Artist = element.Attribute("grandparentTitle")?.Value, // Artist might not be applicable for all types
                        Album = element.Attribute("parentTitle")?.Value, // Album might not be applicable for all types
                        ReleaseDate = element.Attribute("originallyAvailableAt")?.Value,
                        Duration = element.Attribute("duration")?.Value,
                        Thumb = element.Attribute("thumb")?.Value
                    };

                    // Special handling for tracks to extract media part information
                    if (type == "track")
                    {
                        var part = element.Descendants("Part").FirstOrDefault();
                        if (part != null)
                        {
                            mediaItem.PartKey = part.Attribute("key")?.Value;
                            mediaItem.PartId = part.Attribute("id")?.Value;
                        }
                    }

                    items.Add(mediaItem);
                }
            }
            return items;
        }

        // Gets the playback URL for a media item using the part_id and the key together in the url
        public string GetPlaybackUrl(string partKey)
        {
            return $"{_plexURL}{partKey}?X-Plex-Token={_plexToken}";
        }
    }

    public class MediaItem
    {
        public string? Title { get; set; }
        public string? Key { get; set; }
        public string? Type { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? ReleaseDate { get; set; }
        public string? Duration { get; set; }
        public string? Thumb { get; set; }
        public string? PartKey { get; set; }
        public string? PartId { get; set; }
    }
}

