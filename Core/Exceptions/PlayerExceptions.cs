namespace PlexBot.Core.Exceptions;

/// <summary>Exception thrown when audio playback operations fail.
/// Use this for connection issues, format problems, or any music player failures.</summary>
public class PlayerException : PlexBotException
{
    /// <summary>The specific playback operation that failed (Play, Pause, Skip, etc.).
    /// Determines which user-friendly error message is shown.</summary>
    public string Operation { get; }

    /// <summary>The Discord server ID where the error occurred.
    /// Useful for multi-server debugging and error reporting.</summary>
    public ulong? GuildId { get; }

    /// <summary>Indicates whether this error is due to content requiring login credentials.
    /// Used specifically for YouTube videos that need age verification or a sign-in.</summary>
    public bool RequiresLogin { get; }

    /// <summary>Basic constructor - Use when you only have an error message without context.
    /// Example: throw new PlayerException("Unknown audio error occurred");</summary>
    /// <param name="message">The technical error message for logs</param>
    public PlayerException(string message)
        : base(message, "An error occurred while playing audio. Please try again.")
    {
        Operation = "Unknown";
        RequiresLogin = false;
    }

    /// <summary>Operation-specific constructor - Use when you know which operation failed.
    /// Example: throw new PlayerException("Format not supported", "Play");</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    public PlayerException(string message, string operation)
        : base(message, GetUserFriendlyMessage(operation))
    {
        Operation = operation;
        RequiresLogin = false;
    }

    /// <summary>Login-required constructor - Use specifically for YouTube videos requiring login.
    /// Example: throw new PlayerException("Age restricted video", "Play", true);</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (usually "Play")</param>
    /// <param name="requiresLogin">Set to true to indicate content needs authentication</param>
    public PlayerException(string message, string operation, bool requiresLogin)
        : base(message, GetUserFriendlyMessage(operation, requiresLogin))
    {
        Operation = operation;
        RequiresLogin = requiresLogin;
    }

    /// <summary>Server-specific constructor - Use for multi-server deployments to track errors by guild.
    /// Example: throw new PlayerException("Voice channel not found", "Connect", ctx.Guild.Id);</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    /// <param name="guildId">The Discord server ID where the error occurred</param>
    public PlayerException(string message, string operation, ulong guildId)
        : base(message, GetUserFriendlyMessage(operation))
    {
        Operation = operation;
        GuildId = guildId;
        RequiresLogin = false;
    }

    /// <summary>Exception-wrapping constructor - Use when catching another exception during playback.
    /// Automatically detects login requirements based on inner exception message.
    /// Example: catch(HttpException ex) { throw new PlayerException("Network error", "Play", ex); }</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    /// <param name="innerException">The original exception that caused this failure</param>
    public PlayerException(string message, string operation, Exception innerException)
        : base(message, GetUserFriendlyMessage(operation), innerException)
    {
        Operation = operation;
        RequiresLogin = innerException?.Message?.Contains("login", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>Complete constructor - Use for maximum debugging context with server ID and original exception.
    /// Example: catch(Exception ex) { throw new PlayerException("Failed", "Skip", guildId, ex); }</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    /// <param name="guildId">The Discord server ID where the error occurred</param>
    /// <param name="innerException">The original exception that caused this failure</param>
    public PlayerException(string message, string operation, ulong guildId, Exception innerException)
        : base(message, GetUserFriendlyMessage(operation), innerException)
    {
        Operation = operation;
        GuildId = guildId;
        RequiresLogin = innerException?.Message?.Contains("login", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    /// <summary>Maps technical operation names to user-friendly error messages.
    /// Provides a special message for content that requires login credentials.
    /// Operation names should be one of: Connect, Play, Pause, Resume, Skip, Stop, Queue, Volume, Disconnect.</summary>
    /// <param name="operation">The operation name (must match one of the defined operations)</param>
    /// <param name="requiresLogin">Whether the error is due to content requiring login credentials</param>
    /// <returns>A user-friendly error message appropriate for the specific situation</returns>
    private static string GetUserFriendlyMessage(string operation, bool requiresLogin = false)
    {
        if (requiresLogin)
        {
            return "This video requires age verification or login and cannot be played.";
        }
        return operation switch
        {
            "Connect" => "Failed to connect to the voice channel. Please check your permissions and try again.",
            "Play" => "Failed to play the requested track. The media may be unavailable or in an unsupported format.",
            "Pause" => "Failed to pause playback. The player may be in an inconsistent state.",
            "Resume" => "Failed to resume playback. The player may be in an inconsistent state.",
            "Skip" => "Failed to skip to the next track. The queue may be empty or corrupted.",
            "Stop" => "Failed to stop playback. The player may be in an inconsistent state.",
            "Queue" => "Failed to manage the playback queue. The operation could not be completed.",
            "Volume" => "Failed to adjust the volume. The player may be in an inconsistent state.",
            "Disconnect" => "Failed to disconnect from the voice channel. The connection may already be closed.",
            _ => "An error occurred during audio playback. Please try again."
        };
    }
}
