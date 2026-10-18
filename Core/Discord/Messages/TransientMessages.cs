using Discord;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Messages;

/// <summary>Status messages that remove themselves after a short while, so the channel stays clear. The lifetime is
/// bot.transientMessageSeconds; 0 keeps every message.</summary>
public static class TransientMessages
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromSeconds(Math.Max(0, BotConfig.GetInt("bot.transientMessageSeconds", 30)));

    /// <summary>Deletes a message after the lifetime. Runs in the background; a failed delete is only logged.</summary>
    public static void DeleteLater(IDeletable message)
    {
        if (Lifetime <= TimeSpan.Zero) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(Lifetime).ConfigureAwait(false);
            try { await message.DeleteAsync().ConfigureAwait(false); }
            catch (Exception ex) { Logs.Debug($"Could not remove a status message: {ex.Message}"); }
        });
    }

    /// <summary>Deletes the interaction's own reply after the lifetime, for replies sent with RespondAsync or edited
    /// with ModifyOriginalResponseAsync</summary>
    public static async Task DeleteOriginalLaterAsync(IDiscordInteraction interaction)
    {
        if (Lifetime <= TimeSpan.Zero) return;
        try
        {
            DeleteLater(await interaction.GetOriginalResponseAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Logs.Debug($"Could not schedule removal of a status reply: {ex.Message}");
        }
    }
}
