namespace PlexBot.Core.Exceptions;

/// <summary>
/// Exception thrown when authentication with the Plex server fails.
/// This exception type specifically handles errors related to the authentication process,
/// such as invalid credentials, expired tokens, or failures in the OAuth flow.
/// </summary>
public class AuthenticationException : PlexBotException
{
    /// <summary>
    /// Gets or sets the authentication method that was being attempted.
    /// Indicates whether the failure was during token validation, PIN generation,
    /// or another part of the authentication process.
    /// </summary>
    public string AuthMethod { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationException"/> class with a message.
    /// Creates a basic authentication exception with an error message but no specific auth method.
    /// </summary>
    /// <param name="message">The error message that explains the authentication failure</param>
    public AuthenticationException(string message)
        : base(message, "Failed to authenticate with your Plex server. Please check your credentials.")
    {
        AuthMethod = "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationException"/> class with a message and auth method.
    /// Creates an authentication exception with both an error message and the specific authentication
    /// method that failed.
    /// </summary>
    /// <param name="message">The error message that explains the authentication failure</param>
    /// <param name="authMethod">The authentication method that was being attempted</param>
    public AuthenticationException(string message, string authMethod)
        : base(message, GetUserFriendlyMessage(authMethod))
    {
        AuthMethod = authMethod;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationException"/> class with a message, auth method, and inner exception.
    /// Creates a detailed authentication exception with error message, auth method, and the
    /// underlying exception that caused the failure.
    /// </summary>
    /// <param name="message">The error message that explains the authentication failure</param>
    /// <param name="authMethod">The authentication method that was being attempted</param>
    /// <param name="innerException">The exception that is the cause of the authentication failure</param>
    public AuthenticationException(string message, string authMethod, Exception innerException)
        : base(message, GetUserFriendlyMessage(authMethod), innerException)
    {
        AuthMethod = authMethod;
    }

    /// <summary>
    /// Generates a user-friendly error message based on the authentication method.
    /// Maps technical authentication method names to messages that end users can understand
    /// and potentially act upon.
    /// </summary>
    /// <param name="authMethod">The authentication method that failed</param>
    /// <returns>A user-friendly error message corresponding to the authentication method</returns>
    private static string GetUserFriendlyMessage(string authMethod)
    {
        return authMethod switch
        {
            "TokenValidation" => "Your Plex token appears to be invalid. Please re-authenticate with your Plex account.",
            "PinGeneration" => "Unable to start the authentication process with Plex. Please try again later.",
            "PinCheck" => "Authentication with Plex timed out. Please try again and complete the process within the time limit.",
            "TokenStorage" => "Failed to save your Plex credentials. Please check file permissions.",
            _ => "Failed to authenticate with your Plex server. Please check your credentials."
        };
    }
}