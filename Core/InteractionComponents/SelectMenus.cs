using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.Players;
using PlexBot.Core.LavaLink;
using Lavalink4NET.Players;
using System.Runtime.InteropServices;
using System.Text;

namespace PlexBot.Core.InteractionComponents
{
    public class SelectMenus(DiscordSocketClient client, PlexApi plexApi, LavaLinkCommands lavaLink, Players.Players visualPlayers) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client = client;
        private readonly PlexApi _plexApi = plexApi;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;
        private readonly Players.Players _visualPlayers = visualPlayers;

        [ComponentInteraction("search_plex:*", runMode: RunMode.Async)]
        public async Task DisplaySearchResults(string customId, string[] selections)
        {
            await DeferAsync(ephemeral: true);
            string? selectedValue = selections.FirstOrDefault();
            if (string.IsNullOrEmpty(selectedValue))
            {
                await ModifyOriginalResponseAsync(msg => msg.Content = "Invalid selection made.");
                return;
            }
            try
            {
                switch (customId)
                {
                    case "tracks":
                        {
                            Dictionary<string, string>? trackDetails = await plexApi.GetTrackDetails(selectedValue);
                            if (trackDetails != null)
                            {
                                await lavaLink.AddToQueue(Context.Interaction, [trackDetails]);
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Track added to queue.");
                            }
                            else
                            {
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Failed to retrieve track details.");
                            }
                            break;
                        }
                    case "albums":
                        {
                            List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                            if (tracks != null && tracks.Count > 0)
                            {
                                await lavaLink.AddToQueue(Context.Interaction, tracks);
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Tracks from album added to queue.");
                            }
                            else
                            {
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Failed to retrieve tracks for the album.");
                            }
                            break;
                        }
                    case "artists":
                        {
                            List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
                            List<Dictionary<string, string>> allTracks = [];
                            foreach (var album in albums)
                            {
                                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(album["TrackKey"]);
                                if (tracks != null && tracks.Count > 0)
                                {
                                    allTracks.AddRange(tracks);
                                }
                            }
                            if (allTracks.Count > 0)
                            {
                                await lavaLink.AddToQueue(Context.Interaction, allTracks);
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Tracks from all albums by the artist added to queue.");
                            }
                            else
                            {
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Failed to retrieve tracks for the artist.");
                            }
                            break;
                        }
                    default:
                        {
                            await ModifyOriginalResponseAsync(msg => msg.Content = "Invalid selection type.");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                await ModifyOriginalResponseAsync(msg => msg.Content = $"An error occurred: {ex.Message}");
                Console.WriteLine($"Error in DisplaySearchResults: {ex.Message}");
            }
        }

        [ComponentInteraction("queue:*")]
        public async Task HandleQueueOptions(string customId, string[] selectedValues)
        {
            await DeferAsync(ephemeral: true);
            CustomPlayer? player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            string[] args = customId.Split(':');
            string action = args[0];
            int currentPage = args.Length > 2 ? int.Parse(args[2]) : 1;
            switch (action)
            {
                case "edit":
                    switch (selectedValues[0])
                    {
                        case "playNext":
                            await ShowQueueEditMenu(player, "queue:playNext", "Select a track to play next", "Move to play next", currentPage, selectedValues[0]);
                            break;
                        case "remove":
                            await ShowQueueEditMenu(player, "queue:remove", "Select a track to remove", "Remove from queue", currentPage, selectedValues[0]);
                            break;
                        case "rearrange":
                            await ShowQueueEditMenu(player, "queue:rearrangePosition", "Select a track to rearrange", "Rearrange queue", currentPage, selectedValues[0]);
                            break;
                    }
                    break;
                case "playNext":
                    await HandlePlayNext(player, selectedValues);
                    break;
                case "remove":
                    await HandleRemove(player, selectedValues);
                    break;
                case "rearrangePosition":
                    await HandleRearrangePosition(player, selectedValues);
                    break;
                case "rearrange":
                    await HandleRearrange(player, selectedValues);
                    break;
                case "next":
                    await NavigateQueuePage(player, currentPage + 1, selectedValues[0]);
                    break;
                case "back":
                    await NavigateQueuePage(player, currentPage - 1, selectedValues[0]);
                    break;
            }
        }

        private async Task ShowQueueEditMenu(CustomPlayer player, string customIdPrefix, string placeholder, string description, int currentPage, string selectedAction)
        {
            int totalItems = player.Queue.Count;
            int itemsPerPage = 24; // Max number of items per select menu
            int totalPages = (int)Math.Ceiling(totalItems / (double)itemsPerPage);
            int startIndex = (currentPage - 1) * itemsPerPage;
            List<ITrackQueueItem> queueItems = player.Queue.Skip(startIndex).Take(itemsPerPage).ToList();
            SelectMenuBuilder menu = BuildSelectMenu($"{customIdPrefix}:{selectedAction}:{currentPage}", queueItems, startIndex, placeholder, description);
            ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu);
            builder = BuildPaginationComponents(builder, currentPage, totalPages, selectedAction);
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = $"Select a track to {description.ToLower()}:";
                x.Components = builder.Build();
            });
        }

        private async Task HandlePlayNext(CustomPlayer player, string[] selectedValues)
        {
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            int selectedIndex = int.Parse(selectedValues[0]);
            if (selectedIndex >= 0 && selectedIndex < player.Queue.Count)
            {
                ITrackQueueItem itemToMove = player.Queue.ElementAt(selectedIndex);
                await player.Queue.RemoveAsync(itemToMove);
                await player.Queue.InsertAsync(0, itemToMove);
                await ModifyOriginalResponseAsync(x => x.Content = $"Moved '{(itemToMove as CustomTrackQueueItem)?.Title}' to the top of the queue!");

            }
            else
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
            }
        }

        private async Task HandleRemove(CustomPlayer player, string[] selectedValues)
        {
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            int selectedIndex = int.Parse(selectedValues[0]);
            if (selectedIndex >= 0 && selectedIndex < player.Queue.Count)
            {
                ITrackQueueItem itemToRemove = player.Queue.ElementAt(selectedIndex);
                await player.Queue.RemoveAsync(itemToRemove);
                await ModifyOriginalResponseAsync(x => x.Content = $"Removed '{(itemToRemove as CustomTrackQueueItem)?.Title}' from the queue!");

            }
            else
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
            }
        }

        private async Task HandleRearrangePosition(CustomPlayer player, string[] selectedValues)
        {
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            int firstSelectionIndex = int.Parse(selectedValues[0]);
            if (firstSelectionIndex < 0 || firstSelectionIndex >= player.Queue.Count)
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
                return;
            }
            List<SelectMenuOptionBuilder> options = player.Queue.Select((track, index) => new SelectMenuOptionBuilder()
                .WithLabel($"{index + 1}: {(track as CustomTrackQueueItem)?.Title}")
                .WithValue($"{firstSelectionIndex}:{index}")
                .WithDescription("Select new position"))
                .ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId($"queue:rearrangePosition")
                .WithPlaceholder("Select new position")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu);
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "Select new position for the track:";
                x.Components = builder.Build();
            });
        }

        private async Task HandleRearrange(CustomPlayer player, string[] selectedValues)
        {
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            string[] parts = selectedValues[0].Split(':');
            int originalIndex = int.Parse(parts[0]);
            int newIndex = int.Parse(parts[1]);
            if (originalIndex < 0 || originalIndex >= player.Queue.Count ||
                newIndex < 0 || newIndex >= player.Queue.Count)
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
                return;
            }
            ITrackQueueItem itemToMove = player.Queue.ElementAt(originalIndex);
            await player.Queue.RemoveAsync(itemToMove);
            await player.Queue.InsertAsync(newIndex, itemToMove);
            await ModifyOriginalResponseAsync(x => x.Content = $"Moved '{(itemToMove as CustomTrackQueueItem)?.Title}' to position {newIndex + 1} in the queue.");

        }

        private async Task NavigateQueuePage(CustomPlayer player, int newPage, string selectedAction)
        {
            switch (selectedAction)
            {
                case "playNext":
                    await ShowQueueEditMenu(player, "queue:playNext", "Select a track to play next", "Move to play next", newPage, selectedAction);
                    break;
                case "remove":
                    await ShowQueueEditMenu(player, "queue:remove", "Select a track to remove", "Remove from queue", newPage, selectedAction);
                    break;
                case "rearrangePosition":
                    await ShowQueueEditMenu(player, "queue:rearrangePosition", "Select a track to rearrange", "Rearrange queue", newPage, selectedAction);
                    break;
            }
        }

        private static ComponentBuilder BuildPaginationComponents(ComponentBuilder builder, int currentPage, int totalPages, string action)
        {
            // TODO: Move to Buttons.cs and fix logic
            builder.WithButton("Back", $"queue:back:{action}:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: currentPage == 1)
                   .WithButton("Next", $"queue:next:{action}:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: currentPage == totalPages);
            return builder;
        }

        private SelectMenuBuilder BuildSelectMenu(string customId, List<ITrackQueueItem> queueItems, int startIndex, string placeholder, string description)
        {
            List<SelectMenuOptionBuilder> options = queueItems.Select((track, index) => new SelectMenuOptionBuilder()
                .WithLabel($"{startIndex + index + 1}: {(track as CustomTrackQueueItem)?.Title}")
                .WithValue($"{startIndex + index}")
                .WithDescription(description))
                .ToList();
            return new SelectMenuBuilder()
                .WithCustomId(customId)
                .WithPlaceholder(placeholder)
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
        }
    }
}