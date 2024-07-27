

namespace PlexBot.Core.PlexAPI
{
    public class PlexCore
    {
        public readonly string plexUrl = Environment.GetEnvironmentVariable("PLEX_URL") ?? "";
        readonly string plexToken = Environment.GetEnvironmentVariable("PLEX_TOKEN") ?? "";

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
    }
}
