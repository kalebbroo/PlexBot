﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PlexBot.Core.LavaLink;
using PlexBot.Core.Players;
using PlexBot.Core.Commands;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using Lavalink4NET.Players;
using static System.Console;
using Microsoft.VisualBasic;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Reflection;
using System;
using Discord.Rest;

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
            RestInteractionMessage msg = await Context.Interaction.GetOriginalResponseAsync();
            string statusMessage = await lavaLink.TogglePauseResume(Context.Interaction);
            var builder = new ComponentBuilder();
            if (statusMessage.Contains("Paused"))
            {
                builder.WithButton("Resume", "pause_resume:resume", ButtonStyle.Success, row: 0);
                builder.WithButton("Skip", "skip:skip", ButtonStyle.Primary, row: 0);
                builder.WithButton("Queue Options", "queue:options:1", ButtonStyle.Success, row: 0);
                builder.WithButton("Repeat", "repeat:select", ButtonStyle.Secondary, row: 0);
                builder.WithButton("Kill", "kill:kill", ButtonStyle.Danger, row: 0);
                await ModifyOriginalResponseAsync(x =>
                {
                    x.Components = builder.Build();
                });
            }
            else
            {
                await BuildDefaultComponents(msg);
            }
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

            List<SelectMenuOptionBuilder> options =
            [
                new("Off", "off", "Turn off repeat"),
                new("Repeat One", "one", "Repeat the current track"),
                new("Repeat All", "all", "Repeat the entire queue")
            ];

            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId($"set_repeat")
                .WithPlaceholder("Choose repeat mode")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);

            ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu);

            await FollowupAsync("Select the repeat mode:", components: builder.Build(), ephemeral: true);
        }

        [ComponentInteraction("set_repeat")]
        public async Task SetRepeat(string[] selections)
        {
            CustomPlayer? player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            string? selectedRepeatMode = selections.FirstOrDefault();
            player.RepeatMode = selectedRepeatMode switch
            {
                "off" => TrackRepeatMode.None,
                "one" => TrackRepeatMode.Track,
                "all" => TrackRepeatMode.Queue,
                _ => player.RepeatMode
            };
            // TODO: Change visual player to show current repeat mode
            await RespondAsync($"Repeat mode set to {player.RepeatMode}.", ephemeral: true);
        }

        [ComponentInteraction("skip:*", runMode: RunMode.Async)]
        public async Task Skip(string customId)
        {
            if (IsOnCooldown(Context.User, "skip"))
            {
                await FollowupAsync("You are on cooldown.", ephemeral: true);
                return;
            }
            await DeferAsync();
            CustomPlayer? player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            await player.SkipAsync();
        }

        [ComponentInteraction("queue_options:*", runMode: RunMode.Async)]
        public async Task HandleQueueInteraction(string customId)
        {
            await DeferAsync();
            string[] args = customId.Split(':');
            if (args.Length < 2)
            {
                await FollowupAsync("Invalid queue command.", ephemeral: true);
                return;
            }
            string action = args[0]; // 'view', 'next', 'back', 'shuffle', 'playNext', or 'clear'
            WriteLine($"Queue Options Pressed: {action}");
            if (!int.TryParse(args[1], out int currentPage)) // Page number
            {
                await FollowupAsync("Invalid page number.", ephemeral: true);
                return;
            }
            CustomPlayer? player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null || player.Queue.Count == 0)
            {
                await FollowupAsync("The queue is currently empty.", ephemeral: true);
                return;
            }
            RestInteractionMessage msg = await Context.Interaction.GetOriginalResponseAsync();
            switch (action)
            {
                case "options":
                    ComponentBuilder newComponents = new ComponentBuilder()
                        .WithButton("View Queue", "queue_options:view:1", ButtonStyle.Success, row: 1)
                        .WithButton("Shuffle", "queue_options:shuffle:1", ButtonStyle.Primary, row: 0)
                        .WithButton("Edit", $"queue_options:edit:1", ButtonStyle.Primary, row: 0)
                        .WithButton("Clear", "queue_options:clear:1", ButtonStyle.Danger, row: 0)
                        .WithButton("Return Buttons", "queue_options:return:1", ButtonStyle.Danger, row: 0);
                    await ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Components = newComponents.Build();
                    });
                    return;
                case "view":
                    break;
                case "next":
                    currentPage++;
                    break;
                case "back":
                    currentPage = Math.Max(1, currentPage - 1);
                    break;
                case "shuffle":
                    if (msg.Embeds.Count != 0)
                    {
                        string title = msg.Embeds.First().Title;
                        if (title == "Now Playing")
                        {
                            await BuildDefaultComponents(msg);
                            return;
                        }
                        else
                        {
                            await player.Queue.ShuffleAsync();
                            currentPage = 1;
                        }
                    }
                        break;
                case "edit":
                    SelectMenuBuilder menu = new SelectMenuBuilder()
                        .WithCustomId("queue:edit")
                        .WithPlaceholder("How do you want to edit the Queue?");
                    menu.AddOption("Play Next", "playNext", "Move A Track to The Top");
                    menu.AddOption("Remove", "remove", "Remove Track(s) From The Queue");
                    menu.AddOption("Rearrange", "rearrange", "Move items around the queue");
                    ComponentBuilder menuComponents = new ComponentBuilder().WithSelectMenu(menu);
                    await FollowupAsync("Select an option to edit the queue:", components: menuComponents.Build(), ephemeral: true);
                    return;
                case "clear":
                    await player.Queue.ClearAsync();
                    if (msg.Embeds.Count != 0)
                    {
                        string title = msg.Embeds.First().Title;
                        if (title == "Now Playing")
                        {
                            await BuildDefaultComponents(msg);
                            return;
                        }
                        else
                        { 
                            await Context.Interaction.DeleteOriginalResponseAsync();
                        }
                    }
                    return;
                case "return":
                    await BuildDefaultComponents(msg);
                    return;
                case "close":
                    await Context.Interaction.DeleteOriginalResponseAsync();
                    return;
            }
            EmbedBuilder embed = CreateQueueEmbed(player, currentPage);
            ComponentBuilder components = BuildQueueComponents(currentPage, player.Queue.Count, 23);
            if (msg.Embeds.Count != 0)
            {
                string title = msg.Embeds.First().Title;
                if (title == "Now Playing")
                {
                    await FollowupAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
                }
                else
                {
                    IComponentInteraction interaction = (IComponentInteraction)Context.Interaction;
                    await interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Embed = embed.Build();
                        msg.Components = components.Build();
                    });
                }
            }
            else
            {
                await FollowupAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
            }
        }

        private static ComponentBuilder BuildQueueComponents(int currentPage, int totalItems, int itemsPerPage)
        {
            ComponentBuilder components = new ComponentBuilder()
                .WithButton("Back", $"queue_options:back:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: currentPage == 1)
                .WithButton("Next", $"queue_options:next:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: (currentPage * itemsPerPage) >= totalItems)
                .WithButton("Shuffle Queue", "queue_options:shuffle:1", ButtonStyle.Primary, row: 0)
                .WithButton("Edit Queue", $"queue_options:edit:{currentPage}", ButtonStyle.Primary, row: 0)
                .WithButton("Clear Queue", "queue_options:clear:1", ButtonStyle.Danger, row: 0)
                .WithButton("Close", "queue_options:close:1", ButtonStyle.Danger, row: 0);
            return components;
        }

        private static async Task BuildDefaultComponents(RestInteractionMessage originalResponse)
        {
            ComponentBuilder components = new ComponentBuilder()
                .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary, row: 0) 
                .WithButton("Skip", "skip:skip", ButtonStyle.Primary, row: 0)
                .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success, row: 0)
                .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary, row: 0)
                .WithButton("Kill", "kill:kill", ButtonStyle.Danger, row: 0);
            await originalResponse.ModifyAsync(props =>
            {
                props.Components = components.Build();
            });
        }

        private static EmbedBuilder CreateQueueEmbed(CustomPlayer player, int currentPage, int itemsPerPage = 23)
        {
            int totalTracks = player.Queue.Count;
            int totalPages = (totalTracks + itemsPerPage - 1) / itemsPerPage;
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle("Current Music Queue")
                .WithColor(Color.Blue)
                .WithFooter($"Page {currentPage} of {totalPages} ({totalTracks} Queued Tracks)")
                .WithTimestamp(DateTime.Now);
            int startIndex = (currentPage - 1) * itemsPerPage;
            // Display the "Now Playing" track only on the first page and separately from the queue
            if (currentPage == 1 && player.CurrentTrack != null)
            {
                embed.AddField(
                    "Now Playing: " + player.CurrentTrack.Title,
                    $"Artist: {player.CurrentTrack.Author}\nDuration: {player.CurrentTrack.Duration}",
                    inline: true
                );
            }
            IEnumerable<ITrackQueueItem> queueItems = player.Queue.Skip(startIndex).Take(itemsPerPage);
            int itemNumber = startIndex;  // Start numbering from the index of the first item on this page

            foreach (var item in queueItems)
            {
                itemNumber++; // Increment first to start from the next number
                string trackLabel = $"#{itemNumber}"; // Label according to the actual position in the queue
                if (item is CustomTrackQueueItem customTrack)
                {
                    embed.AddField(
                        $"{trackLabel}: {customTrack.Title}",
                        $"Artist: {customTrack.Artist}\nAlbum: {customTrack.Album}\nDuration: {customTrack.Duration}",
                        inline: true
                    );
                }
                else
                {
                    LavalinkTrack? track = item.Track;
                    embed.AddField(
                        $"{trackLabel}: {track!.Title}",
                        $"Artist: {track.Author}\nDuration: {track.Duration}",
                        inline: true
                    );
                }
            }
            return embed;
        }
    }
}