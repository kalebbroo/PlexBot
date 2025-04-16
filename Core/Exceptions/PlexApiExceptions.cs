namespace PlexBot.Core.Exceptions;

/// <summary>Specialized exception for Plex API communication failures that provides rich diagnostic information to help pinpoint server connectivity issues</summary>
public class PlexApiException : PlexBotException
{
    /// <summary>The HTTP status code returned by the Plex server, crucial for diagnosing whether the issue is authentication, permissions, or server availability</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>The specific API endpoint that was being accessed when the error occurred, helpful for isolating which feature or resource is problematic</summary>
    public string? Endpoint { get; }

    /// <summary>The raw error response from the Plex server, often containing JSON with detailed error codes and messages from Plex itself</summary>
    public string? ResponseContent { get; }

    /// <summary>Creates a basic API exception for general communication errors when specific HTTP details aren't available</summary>
    /// <param name="message">Technical error message detailing what went wrong with the API call</param>
    public PlexApiException(string message)
        : base(message, "There was a problem communicating with your Plex server. Please check your server connection.")
    {
    }

    /// <summary>Creates an API exception with HTTP status context to help differentiate between auth failures, missing resources, and server errors</summary>
    /// <param name="message">Detailed technical explanation for logging and developer troubleshooting</param>
    /// <param name="statusCode">The HTTP response code from the Plex server, used to generate appropriate user-friendly messages</param>
    public PlexApiException(string message, HttpStatusCode statusCode)
        : base(message, GetUserFriendlyMessage(statusCode))
    {
        StatusCode = statusCode;
    }

    /// <summary>Creates a comprehensive API exception with complete context about the failed request, including the specific endpoint and response</summary>
    /// <param name="message">Technical error details for developers</param>
    /// <param name="statusCode">HTTP status code indicating the nature of the failure</param>
    /// <param name="endpoint">The specific Plex API endpoint that was being accessed</param>
    /// <param name="responseContent">The raw error response body from the Plex server, often containing additional error details</param>
    public PlexApiException(string message, HttpStatusCode statusCode, string endpoint, string? responseContent = null)
        : base(message, GetUserFriendlyMessage(statusCode))
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseContent = responseContent;
    }

    /// <summary>Creates an exception that wraps another underlying exception that caused the API communication failure, preserving the error chain</summary>
    /// <param name="message">Explanation of what API operation was being attempted when the error occurred</param>
    /// <param name="innerException">The original exception (like network failures or timeouts) that triggered this API exception</param>
    public PlexApiException(string message, Exception innerException)
        : base(message, "There was a problem communicating with your Plex server. Please check your server connection.", innerException)
    {
    }

    /// <summary>Translates technical HTTP status codes into actionable user-friendly messages with specific troubleshooting suggestions</summary>
    /// <param name="statusCode">The HTTP status code returned by the Plex server</param>
    /// <returns>A human-readable explanation that guides users toward appropriate remedial actions based on the specific error type</returns>
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