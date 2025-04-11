namespace PlexBot.Services;

/// <summary>Defines the contract for services that communicate with Plex Media Server through its REST API, abstracting authentication and error handling</summary>
public interface IPlexApiService
{
    /// <summary>Executes an authenticated HTTP request to a Plex API endpoint and returns the raw JSON response for further processing</summary>
    /// <param name="uri">The endpoint URI relative to the Plex server base URL (e.g., "/library/sections")</param>
    /// <param name="cancellationToken">Optional token to cancel the operation for timeout management or user interruption</param>
    /// <returns>The raw JSON response string from the Plex API, requiring further parsing by the caller</returns>
    /// <exception cref="PlexApiException">Thrown when communication fails due to network issues, authentication problems, or server errors</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is deliberately canceled through the token</exception>
    Task<string> PerformRequestAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Builds a fully authenticated streaming URL for a specific media item that can be used by media players</summary>
    /// <param name="partKey">The Plex-specific identifier for the media part, typically found in Media[0].Part[0].key in API responses</param>
    /// <returns>A complete, authenticated URL that can be passed directly to audio/video players for streaming</returns>
    string GetPlaybackUrl(string partKey);

    /// <summary>Constructs a fully authenticated URL for performing searches against the Plex server's content libraries</summary>
    /// <param name="path">The search path component that defines the query parameters and search scope</param>
    /// <returns>A complete, authenticated URL ready for HTTP requests to search Plex content</returns>
    string GetSearchUrl(string path);
}