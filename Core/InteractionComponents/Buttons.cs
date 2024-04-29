using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PlexBot.Core.LavaLink;
using PlexBot.Core.Players;
using PlexBot.Core.Commands;
using Lavalink4NET.Players.Queued;

namespace PlexBot.Core.InteractionComponents
{
    public class Buttons(SlashCommands commands, LavaLinkCommands lavaLink) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly SlashCommands _commands = commands;
        private readonly LavaLinkCommands _lavaLink = lavaLink;
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

        [ComponentInteraction("pause_resume:*", runMode: RunMode.Async)]
        public async Task TogglePauseResumePlayer(string customId)
        {
            await DeferAsync();

            // Check the current state and toggle
            string statusMessage = await lavaLink.TogglePauseResume(Context.Interaction);

            // Prepare to update the buttons based on the new state
            var builder = new ComponentBuilder();

            if (statusMessage.Contains("Paused"))
            {
                // Player was paused, show only the resume button
                builder.WithButton("Resume", "pause_resume:resume", ButtonStyle.Success);
            }
            else
            {
                // Player was resumed, show only the pause button
                builder.WithButton("Pause", "pause_resume:pause", ButtonStyle.Danger);
            }

            // Update the original message with the new button and status
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = statusMessage;
                x.Components = builder.Build();
            });
        }

        [ComponentInteraction("kill:*", runMode: RunMode.Async)]
        public async Task Kill()
        {
            if (IsOnCooldown(Context.User, "kill"))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
                return;
            }
            await DeferAsync();

            var player = await lavaLink.GetPlayerAsync(Context.Interaction, connectToVoiceChannel: false);
            if (player != null)
            {
                await player.StopAsync();
                await player.DisconnectAsync();
                await FollowupAsync("Player stopped and disconnected.", ephemeral: true);
            }
            else
            {
                await FollowupAsync("No active player to kill.", ephemeral: true);
            }
        }

        [ComponentInteraction("repeat:*", runMode: RunMode.Async)]
        public async Task Repeat()
        {
            if (IsOnCooldown(Context.User, "repeat"))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
                return;
            }
            await DeferAsync();

            var options = new List<SelectMenuOptionBuilder>()
            {
                new("Off", "off", "Turn off repeat"),
                new("Repeat One", "one", "Repeat the current track"),
                new("Repeat All", "all", "Repeat the entire queue")
            };

            var menu = new SelectMenuBuilder()
                .WithCustomId("set_repeat")
                .WithPlaceholder("Choose repeat mode")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);

            var builder = new ComponentBuilder().WithSelectMenu(menu);

            await FollowupAsync("Select the repeat mode:", components: builder.Build(), ephemeral: true);
        }

        [ComponentInteraction("set_repeat:*")]
        public async Task SetRepeat(string repeatMode)
        {
            var player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }

            player.RepeatMode = repeatMode switch
            {
                "off" => TrackRepeatMode.None,
                "one" => TrackRepeatMode.Track,
                "all" => TrackRepeatMode.Queue,
                _ => player.RepeatMode
            };

            await FollowupAsync($"Repeat mode set to {player.RepeatMode}.", ephemeral: true);
        }


        [ComponentInteraction("queue:*", runMode: RunMode.Async)]
        public async Task Queue(string customId)
        {
            if (IsOnCooldown(Context.User, "queue"))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
                return;
            }

            await DeferAsync();

            var player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null || player.Queue.Count == 0)
            {
                await FollowupAsync("The queue is currently empty.", ephemeral: true);
                return;
            }
            EmbedBuilder embed = new();
                embed.WithTitle("Queue");
                embed.WithDescription("Queue information");
                embed.WithColor(Color.Blue);
                // TODO: Add the queue information to the embed
                embed.WithTimestamp(DateTime.Now);
                embed.WithFooter("Queue footer");
            ComponentBuilder components = new();
                components.WithButton("Clear Queue", "queue:clear", ButtonStyle.Danger);
                components.WithButton("Remove Track", "queue:remove", ButtonStyle.Secondary);
                components.WithButton("Shuffle Queue", "queue:shuffle", ButtonStyle.Secondary);
                components.WithButton("Next", "next_back:next", ButtonStyle.Secondary);
                components.WithButton("Back", "next_back:back", ButtonStyle.Secondary);
            
            await RespondAsync(embed: embed.Build(), components: components.Build());

            // Display the queue in an embed with buttons to clear, remove tracks, shuffle, and go to the next or previous page
        }

        [ComponentInteraction("next_back:*", runMode: RunMode.Async)]
        public async Task NextBack(string customId)
        {
            if (IsOnCooldown(Context.User, "next_back"))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
                return;
            }

            await DeferAsync();

            var player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null || player.Queue.Count == 0)
            {
                await FollowupAsync("The queue is currently empty.", ephemeral: true);
                return;
            }

            if (customId == "next")
            {
                // TODO: Go to the next page
            }
            else
            {
                // TODO: Go to the previous page
            }
        }

    }
}
