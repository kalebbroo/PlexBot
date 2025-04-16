using PlexBot.Core.Exceptions;
using PlexBot.Utils.Http;
using PlexBot.Utils;
using PlexBot.Core.Services;

namespace PlexBot.Core.Services.PlexApi
{
    /// <summary>Implements low-level communication with Plex Media Server API, handling authentication, request formatting, and error management</summary>
    public class PlexApiService : IPlexApiService
    {
        private readonly HttpClientWrapper _httpClient;
        private readonly string _plexUrl;
        private string? _plexToken;

        /// <summary>Configures the service with required dependencies and validates essential configuration settings</summary>
        /// <param name="httpClient">HTTP client for making network requests to the Plex server</param>
        /// <param name="authService">Authentication service that provides valid Plex access tokens</param>
        public PlexApiService(HttpClient httpClient)
        {
            _httpClient = new HttpClientWrapper(httpClient, "PlexAPI");
            _plexUrl = EnvConfig.Get("PLEX_URL", "").TrimEnd('/');
            _plexToken = EnvConfig.Get("PLEX_TOKEN", "");
            if (string.IsNullOrEmpty(_plexUrl))
            {
                throw new ArgumentException("PLEX_URL is not configured. Please set it in the .env file.");
            }
            Logs.Init($"PlexApiService initialized with server URL: {_plexUrl}");
        }

        /// <inheritdoc />
        public async Task<string> PerformRequestAsync(string uri, CancellationToken cancellationToken = default)
        {
            try
            {
                // Prepare the full URL
                string fullUrl = uri.StartsWith("http") ? uri : $"{_plexUrl}{uri}";
                // Add Plex token if not already in the URL
                if (!fullUrl.Contains("X-Plex-Token="))
                {
                    fullUrl += (fullUrl.Contains('?') ? "&" : "?") + $"X-Plex-Token={_plexToken}";
                }
                Logs.Debug($"Performing Plex API request to: {fullUrl.Replace(_plexToken!, "[REDACTED]")}");
                // Set up headers
                var headers = new Dictionary<string, string>
                {
                    ["Accept"] = "application/json"
                };
                // Send the request
                string response = await _httpClient.SendRequestForStringAsync(HttpMethod.Get, fullUrl, null, headers, cancellationToken);
                // Check for empty response
                if (string.IsNullOrEmpty(response))
                {
                    throw new PlexApiException("Plex API returned an empty response");
                }
                Logs.Debug($"Received Plex API response ({response.Length} bytes)");
                return response;
            }
            catch (PlexApiException)
            {
                // Just rethrow PlexApiExceptions as they're already properly formatted
                throw;
            }
            catch (AuthenticationException)
            {
                // Just rethrow AuthenticationExceptions as they're already properly formatted
                throw;
            }
            catch (Exception ex)
            {
                // Wrap other exceptions in a PlexApiException for consistent error handling
                throw new PlexApiException($"Error performing Plex API request: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public string GetPlaybackUrl(string partKey)
        {
            // Ensure part key is properly formatted
            if (string.IsNullOrEmpty(partKey))
            {
                throw new ArgumentException("Part key cannot be null or empty", nameof(partKey));
            }
            // If the partKey is already a full URL, just return it
            if (partKey.StartsWith("http"))
            {
                return partKey;
            }
            // Fix duplicated "/children" if present (a common issue with some Plex servers)
            string childrenSegment = "/children";
            if (partKey.Contains(childrenSegment + childrenSegment))
            {
                partKey = partKey.Replace(childrenSegment + childrenSegment, childrenSegment);
            }
            // Construct the URL with the token
            return $"{_plexUrl}{partKey}?X-Plex-Token={_plexToken}";
        }

        /// <inheritdoc />
        public string GetSearchUrl(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }
            // Remove any leading slash to avoid double slashes
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }
            return $"{_plexUrl}/{path}";
        }
    }
}