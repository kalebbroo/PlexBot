namespace PlexBot.Core.Exceptions;

/// <summary>
/// Exception thrown when errors occur during communication with the Plex API.
/// This exception type captures details specific to Plex API interactions, such as
/// HTTP status codes, request URLs, and other contextual information that can help
/// diagnose issues with the Plex server connection.
/// </summary>
public class PlexApiException : PlexBotException
{
    /// <summary>
    /// Gets or sets the HTTP status code returned by the Plex API, if available.
    /// Provides context about the nature of the API failure, such as authentication
    /// failures (401), missing resources (404), or server errors (500).
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Gets or sets the endpoint URL that was being called when the error occurred.
    /// Useful for diagnostics to identify which specific API call failed.
    /// </summary>
    public string? Endpoint { get; }

    /// <summary>
    /// Gets or sets the raw response received from the Plex API, if any.
    /// May contain error details from the Plex server that can help diagnose the issue.
    /// </summary>
    public string? ResponseContent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexApiException"/> class with a message.
    /// Creates a basic API exception with an error message but no status code or endpoint information.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    public PlexApiException(string message)
        : base(message, "There was a problem communicating with your Plex server. Please check your server connection.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexApiException"/> class with a message and HTTP status code.
    /// Creates an API exception with both an error message and the HTTP status code returned by the API.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="statusCode">The HTTP status code returned by the Plex API</param>
    public PlexApiException(string message, HttpStatusCode statusCode)
        : base(message, GetUserFriendlyMessage(statusCode))
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexApiException"/> class with a message, HTTP status code, and endpoint.
    /// Creates a fully detailed API exception with error message, status code, and the specific endpoint that was called.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="statusCode">The HTTP status code returned by the Plex API</param>
    /// <param name="endpoint">The API endpoint that was being called</param>
    /// <param name="responseContent">The raw response content from the API, if available</param>
    public PlexApiException(string message, HttpStatusCode statusCode, string endpoint, string? responseContent = null)
        : base(message, GetUserFriendlyMessage(statusCode))
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseContent = responseContent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexApiException"/> class with a message and inner exception.
    /// Wraps another exception that was the root cause of the API communication failure.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public PlexApiException(string message, Exception innerException)
        : base(message, "There was a problem communicating with your Plex server. Please check your server connection.", innerException)
    {
    }

    /// <summary>
    /// Generates a user-friendly error message based on the HTTP status code.
    /// Maps technical HTTP status codes to messages that end users can understand
    /// and potentially act upon.
    /// </summary>
    /// <param name="statusCode">The HTTP status code to generate a message for</param>
    /// <returns>A user-friendly error message corresponding to the status code</returns>
    private static string GetUserFriendlyMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "Authentication failed with your Plex server. Please check your Plex token.",
            HttpStatusCode.Forbidden => "You don't have permission to access this resource on your Plex server.",
            HttpStatusCode.NotFound => "The requested media could not be found on your Plex server.",
            HttpStatusCode.RequestTimeout => "The connection to your Plex server timed out. Please check your network connection.",
            HttpStatusCode.InternalServerError => "Your Plex server encountered an internal error. Please check your server logs.",
            HttpStatusCode.BadGateway => "There was a connection issue between PlexBot and your Plex server.",
            HttpStatusCode.ServiceUnavailable => "Your Plex server is currently unavailable. Please check if it's running.",
            HttpStatusCode.GatewayTimeout => "The connection to your Plex server timed out. Please check your server.",
            _ => "There was a problem communicating with your Plex server. Please check your server connection."
        };
    }
}