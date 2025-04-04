namespace PlexBot.Core.Exceptions;

/// <summary>
/// Exception thrown when errors occur during audio playback operations.
/// This exception type specifically handles errors related to the audio player,
/// such as connection failures, playback errors, or queue management issues.
/// </summary>
public class PlayerException : PlexBotException
{
    /// <summary>
    /// Gets or sets the playback operation that was being attempted.
    /// Indicates whether the failure was during play, pause, skip, or another
    /// operation, providing context for error handling and user messaging.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets or sets the guild ID where the playback error occurred.
    /// Useful for diagnostics and for targeting error messages to the correct server.
    /// </summary>
    public ulong? GuildId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerException"/> class with a message.
    /// Creates a basic player exception with an error message but no specific operation or guild.
    /// </summary>
    /// <param name="message">The error message that explains the playback failure</param>
    public PlayerException(string message)
        : base(message, "An error occurred while playing audio. Please try again.")
    {
        Operation = "Unknown";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerException"/> class with a message and operation.
    /// Creates a player exception with both an error message and the specific operation that failed.
    /// </summary>
    /// <param name="message">The error message that explains the playback failure</param>
    /// <param name="operation">The playback operation that was being attempted</param>
    public PlayerException(string message, string operation)
        : base(message, GetUserFriendlyMessage(operation))
    {
        Operation = operation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerException"/> class with a message, operation, and guild ID.
    /// Creates a detailed player exception with error message, operation, and the specific guild
    /// where the failure occurred.
    /// </summary>
    /// <param name="message">The error message that explains the playback failure</param>
    /// <param name="operation">The playback operation that was being attempted</param>
    /// <param name="guildId">The ID of the guild where the error occurred</param>
    public PlayerException(string message, string operation, ulong guildId)
        : base(message, GetUserFriendlyMessage(operation))
    {
        Operation = operation;
        GuildId = guildId;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerException"/> class with a message, operation, and inner exception.
    /// Creates a player exception with error message, operation, and the underlying exception that
    /// caused the failure.
    /// </summary>
    /// <param name="message">The error message that explains the playback failure</param>
    /// <param name="operation">The playback operation that was being attempted</param>
    /// <param name="innerException">The exception that is the cause of the playback failure</param>
    public PlayerException(string message, string operation, Exception innerException)
        : base(message, GetUserFriendlyMessage(operation), innerException)
    {
        Operation = operation;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerException"/> class with a message, operation, guild ID, and inner exception.
    /// Creates a fully detailed player exception with error message, operation, guild ID, and the
    /// underlying exception that caused the failure.
    /// </summary>
    /// <param name="message">The error message that explains the playback failure</param>
    /// <param name="operation">The playback operation that was being attempted</param>
    /// <param name="guildId">The ID of the guild where the error occurred</param>
    /// <param name="innerException">The exception that is the cause of the playback failure</param>
    public PlayerException(string message, string operation, ulong guildId, Exception innerException)
        : base(message, GetUserFriendlyMessage(operation), innerException)
    {
        Operation = operation;
        GuildId = guildId;
    }

    /// <summary>
    /// Generates a user-friendly error message based on the playback operation.
    /// Maps technical operation names to messages that end users can understand
    /// and potentially act upon.
    /// </summary>
    /// <param name="operation">The playback operation that failed</param>
    /// <returns>A user-friendly error message corresponding to the operation</returns>
    private static string GetUserFriendlyMessage(string operation)
    {
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