using PlexBot.Core.Exceptions;

namespace PlexBot.Core.Discord.Messages;

/// <summary>Chooses the text a user sees when something fails. A PlexBot exception carries its own user-facing message;
/// any other exception, or none, gets the caller's fallback, so raw exception text never reaches Discord.</summary>
public static class ErrorText
{
    /// <summary>The body for a failure: the exception's user message when it is a PlexBot exception that has one, otherwise the fallback</summary>
    public static string BodyFor(Exception? ex, string fallback) =>
        ex is PlexBotException { UserFriendlyMessage: { Length: > 0 } message } ? message : fallback;
}
