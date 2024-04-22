using Discord.Interactions;
using Discord.WebSocket;

namespace PlexBot.Core.InteractionComponents
{
    public class Buttons(Commands.SlashCommands commands) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly Commands.SlashCommands _commands = commands;
        private static readonly Dictionary<(ulong, string), DateTime> _lastInteracted = [];
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3); // 3 seconds cooldown

        /// <summary>Checks if a user is on cooldown for a specific command.</summary>
        /// <param name="user">The user to check for cooldown.</param>
        /// <param name="command">The command to check for cooldown.</param>
        /// <returns>True if the user is on cooldown; otherwise, false.</returns>
        private static bool IsOnCooldown(SocketUser user, string command)
        {
            var key = (user.Id, command);
            if (_lastInteracted.TryGetValue(key, out var lastInteraction))
            {
                if (DateTime.UtcNow - lastInteraction < Cooldown)
                {
                    return true;
                }
            }
            _lastInteracted[key] = DateTime.UtcNow;
            return false;
        }

        [ComponentInteraction("pause:*", runMode: RunMode.Async)]
        public async Task Pause()
        {
            if (IsOnCooldown(Context.User, "pause"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // pauses the current track
            // removes buttons and adds resume button
        }

        [ComponentInteraction("resume:*", runMode: RunMode.Async)]
        public async Task Resume()
        {
            if (IsOnCooldown(Context.User, "resume"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // resumes the player after pausing
            // Maybe this should not be shown unless the player is paused?
        }

        [ComponentInteraction("skip:*", runMode: RunMode.Async)]
        public async Task Skip()
        {
            if (IsOnCooldown(Context.User, "skip"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // skips the current track
        }

        [ComponentInteraction("kill:*", runMode: RunMode.Async)]
        public async Task Kill()
        {
            if (IsOnCooldown(Context.User, "kill"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // Clears the queue and stops the player then disconnects from the voice channel
        }

        [ComponentInteraction("repeat:*", runMode: RunMode.Async)]
        public async Task Repeat()
        {
            if (IsOnCooldown(Context.User, "repeat"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // create a select menu with repeat options (off, one, all)
        }

        [ComponentInteraction("shuffle:*", runMode: RunMode.Async)]
        public async Task Shuffle()
        {
            if (IsOnCooldown(Context.User, "shuffle"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // create a select menu with shuffle options (on, off)
        }

        [ComponentInteraction("queue:*", runMode: RunMode.Async)]
        public async Task Queue()
        {
            if (IsOnCooldown(Context.User, "queue"))
            {
                await FollowupAsync("You are on cooldown.");
                return;
            }
            await DeferAsync();
            // create a select menu with queue options (clear, remove, move, display a list)
            // or should it display a list of the current queue then have buttons for clear, remove, move?
        }
    }
}
