namespace PlexBot.Services;

/// <summary>
/// Defines the contract for services that interact with the Plex API.
/// This interface abstracts the core communication layer with Plex servers,
/// allowing for flexible implementation details while maintaining a consistent
/// interaction pattern across the application.
/// </summary>
public interface IPlexApiService
{
    /// <summary>
    /// Performs a request to the Plex API and returns the raw response.
    /// This method handles all the authentication, request building, and error
    /// handling necessary to communicate with Plex, while allowing the caller to
    /// focus on the specific endpoint and parameters needed.
    /// </summary>
    /// <param name="uri">The endpoint URI to request, relative to the Plex server base URL</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>
    /// The raw JSON response from the Plex API as a string, which can be 
    /// parsed by the caller according to the expected response format
    /// </returns>
    /// <exception cref="PlexApiException">Thrown when communication with the Plex API fails</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled</exception>
    Task<string> PerformRequestAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a complete playback URL for a media item.
    /// Takes a Plex part key (typically obtained from search results or library browsing)
    /// and constructs a fully qualified URL including authentication tokens and any
    /// necessary path adjustments.
    /// </summary>
    /// <param name="partKey">
    /// The part key identifying the media, usually from the Media[0].Part[0].key property 
    /// in Plex API responses
    /// </param>
    /// <returns>
    /// A complete URL that can be used by media players to stream the content,
    /// including the necessary authentication
    /// </returns>
    string GetPlaybackUrl(string partKey);

    /// <summary>
    /// Generates a complete URL for searching the Plex server.
    /// Combines the base server URL with the provided path and adds any necessary
    /// authentication parameters.
    /// </summary>
    /// <param name="path">The search path relative to the Plex server base URL</param>
    /// <returns>A complete URL that can be used to search the Plex server</returns>
    string GetSearchUrl(string path);
}