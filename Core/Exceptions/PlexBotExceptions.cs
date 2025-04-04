namespace PlexBot.Core.Exceptions;

/// <summary>
/// Base exception class for all application-specific exceptions.
/// Serves as the foundation for a domain-specific exception hierarchy, allowing
/// for consistent error handling patterns across the application while providing
/// meaningful context about the nature of errors.
/// </summary>
public class PlexBotException : Exception
{
    /// <summary>
    /// Gets or sets a recommended user-friendly error message.
    /// This property provides a message suitable for displaying to end users,
    /// with technical details abstracted away while still conveying the nature
    /// of the error. If not explicitly set, it defaults to a generic message.
    /// </summary>
    public string UserFriendlyMessage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexBotException"/> class.
    /// Creates a basic exception with no specific message or inner exception.
    /// </summary>
    public PlexBotException()
        : base("An error occurred in the Plex Bot application.")
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexBotException"/> class with a message.
    /// Creates an exception with a specific error message explaining the issue.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    public PlexBotException(string message)
        : base(message)
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexBotException"/> class with a message and user-friendly message.
    /// Creates an exception with both a detailed technical message for logging and
    /// a simplified message suitable for displaying to users.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="userFriendlyMessage">A simplified message suitable for displaying to users</param>
    public PlexBotException(string message, string userFriendlyMessage)
        : base(message)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexBotException"/> class with a message and inner exception.
    /// Creates an exception with a specific error message and includes the inner exception
    /// that is the cause of the current exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or a null reference if no
    /// inner exception is specified
    /// </param>
    public PlexBotException(string message, Exception innerException)
        : base(message, innerException)
    {
        UserFriendlyMessage = "Something went wrong. Please try again later.";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexBotException"/> class with a message, user-friendly message, and inner exception.
    /// Creates an exception with a detailed technical message, user-friendly message, and
    /// includes the inner exception that is the cause of the current exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception</param>
    /// <param name="userFriendlyMessage">A simplified message suitable for displaying to users</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or a null reference if no
    /// inner exception is specified
    /// </param>
    public PlexBotException(string message, string userFriendlyMessage, Exception innerException)
        : base(message, innerException)
    {
        UserFriendlyMessage = userFriendlyMessage;
    }
}