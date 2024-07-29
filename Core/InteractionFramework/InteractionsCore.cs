using Discord.Interactions;
using Discord.WebSocket;
using PlexBot.Core.LavaLink;

namespace PlexBot.Core.InteractionFramework
{
    public class InteractionsCore(LavaLinkCommands lavaLink) : InteractionModuleBase<SocketInteractionContext>
    {
        public LavaLinkCommands _lavaLink = lavaLink;
        private static readonly Dictionary<(ulong, string), DateTime> _lastInteracted = [];
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);

        /// <summary>Checks if a user is on cooldown for a specific command.</summary>
        /// <param name="user">The user to check for cooldown.</param>
        /// <param name="command">The command to check for cooldown.</param>
        /// <returns>True if the user is on cooldown; otherwise, false.</returns>
        protected static bool IsOnCooldown(SocketUser user, string command)
        {
            var key = (user.Id, command);
            if (_lastInteracted.TryGetValue(key, out DateTime lastInteraction))
            {
                if (DateTime.UtcNow - lastInteraction < Cooldown)
                {
                    return true;
                }
            }
            _lastInteracted[key] = DateTime.UtcNow;
            return false;
        }

        /// <summary>Handles cooldown response.</summary>
        /// <param name="user">The user to check for cooldown.</param>
        /// <param name="command">The command to check for cooldown.</param>
        /// <returns>Task representing the cooldown response.</returns>
        protected async Task HandleCooldown(SocketUser user, string command)
        {
            if (IsOnCooldown(user, command))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
            }
        }
    }
}
