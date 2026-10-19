using PlexBot.Core.Discord.Messages;

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

    /// <summary>Basic constructor - Use when you only have an error message without context.
    /// Example: throw new PlayerException("Unknown audio error occurred");</summary>
    /// <param name="message">The technical error message for logs</param>
    public PlayerException(string message)
        : base(message, "Playback failed for an unknown reason. Check the bot log for details.")
    {
        Operation = "Unknown";
    }

    /// <summary>Operation-specific constructor - Use when you know which operation failed.
    /// Example: throw new PlayerException("Format not supported", "Play");</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    public PlayerException(string message, string operation)
        : base(message, GetUserFriendlyMessage(operation))
    {
        Operation = operation;
    }

    /// <summary>Operation-specific constructor with an explicit user-facing message, for failures
    /// whose cause (not voice channel, player gone) should be shown to the user as-is.</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed</param>
    /// <param name="userMessage">The message shown to the user</param>
    public PlayerException(string message, string operation, string userMessage)
        : base(message, userMessage)
    {
        Operation = operation;
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
    }

    /// <summary>Exception-wrapping constructor - Use when catching another exception during playback.
    /// Example: catch(HttpException ex) { throw new PlayerException("Network error", "Play", ex); }</summary>
    /// <param name="message">The technical error message for logs</param>
    /// <param name="operation">The operation that failed (Connect, Play, Pause, etc.)</param>
    /// <param name="innerException">The original exception that caused this failure</param>
    public PlayerException(string message, string operation, Exception innerException)
        : base(message, GetUserFriendlyMessage(operation), innerException)
    {
        Operation = operation;
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
    }

    /// <summary>Maps technical operation names to user-friendly error messages.
    /// Operation names should be one of: Connect, Play, Pause, Resume, Skip, Stop, Queue, Volume, Disconnect.</summary>
    /// <param name="operation">The operation name (must match one of the defined operations)</param>
    /// <returns>A user-friendly error message appropriate for the specific situation</returns>
    private static string GetUserFriendlyMessage(string operation)
    {
        return operation switch
        {
            "Connect" => "PlexBot could not join the voice channel or reach its audio server. Check that the bot can join this channel "
                + $"and that Lavalink is running, then try again. [Lavalink help]({DocsLinks.LavalinkConnection})",
            "Play" => "This track could not be played. Plex may not have a playable file for it, or the stream did not load. "
                + $"[Track loading help]({DocsLinks.TrackLoadFailures})",
            "Pause" => "Could not pause or resume playback. Check the bot log for details.",
            "Resume" => "Could not pause or resume playback. Check the bot log for details.",
            "Skip" => "Could not skip to the next track. Check the bot log for details.",
            "Stop" => "Could not stop playback. Check the bot log for details.",
            "Queue" => "Could not update the queue. Check the bot log for details.",
            "Volume" => "Could not change the volume. Check the bot log for details.",
            "Disconnect" => "Could not leave the voice channel. It may already have disconnected.",
            _ => "Playback failed for an unknown reason. Check the bot log for details."
        };
    }
}
