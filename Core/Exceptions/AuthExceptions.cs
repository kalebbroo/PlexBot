namespace PlexBot.Core.Exceptions;

/// <summary>Specialized exception type for authentication-related failures with detailed context for troubleshooting Plex server connectivity issues</summary>
public class AuthenticationException : PlexBotException
{
    /// <summary>Identifies which part of the authentication process failed, helping pinpoint exactly where the auth flow broke down</summary>
    public string AuthMethod { get; }

    /// <summary>Creates a basic authentication exception for general auth failures when the specific authentication step is unknown</summary>
    /// <param name="message">Detailed error message explaining the technical cause of the authentication failure</param>
    public AuthenticationException(string message)
        : base(message, "Failed to authenticate with your Plex server. Please check your credentials.")
    {
        AuthMethod = "Unknown";
    }

    /// <summary>Creates an authentication exception with context about which specific authentication mechanism failed (token validation, pin generation, etc.)</summary>
    /// <param name="message">Technical error details for logging and developer troubleshooting</param>
    /// <param name="authMethod">The specific authentication method that failed, used to generate appropriate user-facing messages</param>
    public AuthenticationException(string message, string authMethod)
        : base(message, GetUserFriendlyMessage(authMethod))
    {
        AuthMethod = authMethod;
    }

    /// <summary>Creates a detailed exception that captures both the auth failure context and the underlying cause for complete diagnostic information</summary>
    /// <param name="message">Technical error explanation for developers</param>
    /// <param name="authMethod">The authentication mechanism that failed (TokenValidation, PinGeneration, PinCheck, etc.)</param>
    /// <param name="innerException">The original exception that triggered this authentication failure, preserving the full error chain</param>
    public AuthenticationException(string message, string authMethod, Exception innerException)
        : base(message, GetUserFriendlyMessage(authMethod), innerException)
    {
        AuthMethod = authMethod;
    }

    /// <summary>Maps technical authentication failure points to user-friendly messages that guide users toward appropriate remedial actions</summary>
    /// <param name="authMethod">The authentication method identifier that failed</param>
    /// <returns>A human-readable explanation and suggested remedy tailored to the specific failure point</returns>
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