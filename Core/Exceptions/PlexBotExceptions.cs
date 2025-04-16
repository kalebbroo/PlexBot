namespace PlexBot.Core.Exceptions;

/// <summary>Foundation exception class that provides the basis for all application-specific errors with both technical and user-friendly messaging</summary>
public class PlexBotException : Exception
{
    /// <summary>A simplified, non-technical message that can be safely displayed to end users through Discord or other interfaces</summary>
    public string UserFriendlyMessage { get; set; }

    /// <summary>Creates a basic exception with default generic messages for both technical logging and user-facing output</summary>
    public PlexBotException()
        : base("An error occurred in the Plex Bot application.")
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>Creates an exception with a detailed technical message for logging but still uses a generic user-facing message</summary>
    /// <param name="message">Technical error description for developers and logs, should include specific details about what failed</param>
    public PlexBotException(string message)
        : base(message)
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>Creates an exception with separate messages for technical logging and user interface display</summary>
    /// <param name="message">Detailed technical message that explains precisely what went wrong for developers and logs</param>
    /// <param name="userFriendlyMessage">Simplified, helpful message suitable for displaying to end users without exposing technical details</param>
    public PlexBotException(string message, string userFriendlyMessage)
        : base(message)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }

    /// <summary>Creates an exception that wraps another exception while preserving the detailed technical context for debugging</summary>
    /// <param name="message">Additional context about where/why the inner exception occurred</param>
    /// <param name="innerException">The original exception that triggered this higher-level exception, maintaining the full error chain</param>
    public PlexBotException(string message, Exception innerException)
        : base(message, innerException)
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>Creates a fully-featured exception with technical message, user-friendly message, and the original exception for complete error handling</summary>
    /// <param name="message">Technical explanation of the error for developers and logs</param>
    /// <param name="userFriendlyMessage">End-user appropriate message that can guide them toward resolution without exposing implementation details</param>
    /// <param name="innerException">The original exception that triggered this error, providing the complete error stack trace for debugging</param>
    public PlexBotException(string message, string userFriendlyMessage, Exception innerException)
        : base(message, innerException)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}