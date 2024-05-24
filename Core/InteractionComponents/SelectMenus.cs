using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.LavaLink;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;

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
            string[] custom = customId.Split(':');
            string type = custom[0];
            string service = custom[1];
            string? selectedValue = selections.FirstOrDefault();
            if (string.IsNullOrEmpty(selectedValue))
            {
                await ModifyOriginalResponseAsync(msg => msg.Content = "Invalid selection made.");
                return;
            }
            try
            {
                switch (type)
                {
                    case "tracks":
                        {
                            if (service == "youtube")
                            {
                                string youtubeIdentifier = selectedValue;
                                string youtubeUrl = $"https://www.youtube.com/watch?v={youtubeIdentifier}";
                                Dictionary<string, string> ytTrackDetails = new()
                                {
                                    { "Title", "youtube song title" },
                                    { "TrackKey", youtubeUrl },
                                    { "Artist", "youtube artist" },
                                    { "Duration", "youtube duration" },
                                    { "Url", youtubeUrl }
                                };
                                CustomPlayer? player = await lavaLink.GetPlayerAsync(Context.Interaction, true);
                                await player.PlayAsync(youtubeUrl);
                                //await lavaLink.AddToQueue(Context.Interaction, [ytTrackDetails]);
                                await ModifyOriginalResponseAsync(msg => msg.Content = "Track added to queue.");
                                break;
                            }
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
            CustomPlayer? player = await _lavaLinkCommands.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            string[] args = customId.Split(':');
            int currentPage = args.Length > 2 ? int.Parse(args[2]) : 1;
            string action = args[0];
            string queueAction = selectedValues.Length > 0 ? selectedValues[0] : string.Empty;
            switch (action)
            {
                case "edit":
                    await ShowQueueEditMenu(player, queueAction, currentPage);
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
                    await HandleInitialRearrange(player, selectedValues);
                    break;
            }
        }

        private async Task ShowQueueEditMenu(CustomPlayer player, string queueAction, int currentPage)
        {
            string customIdPrefix = $"queue:{queueAction}";
            string placeholder = queueAction switch
            {
                "playNext" => "Select a track to play next",
                "remove" => "Select a track to remove",
                "rearrange" => "Select a track to rearrange",
                _ => "Select a track"
            };
            string description = queueAction switch
            {
                "playNext" => "Move to play next",
                "remove" => "Remove from queue",
                "rearrange" => "Rearrange queue",
                _ => "Perform action"
            };
            await ShowPaginatedMenu(player, customIdPrefix, placeholder, description, currentPage, queueAction);
        }

        private async Task ShowPaginatedMenu(CustomPlayer player, string customIdPrefix, string placeholder, string description, int currentPage, string selectedAction)
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

            await ShowRearrangePositionMenu(player, firstSelectionIndex, 1);
        }

        private async Task ShowRearrangePositionMenu(CustomPlayer player, int originalIndex, int currentPage)
        {
            int totalItems = player.Queue.Count;
            int itemsPerPage = 24;
            int totalPages = (int)Math.Ceiling(totalItems / (double)itemsPerPage);
            int startIndex = (currentPage - 1) * itemsPerPage;
            List<ITrackQueueItem> queueItems = player.Queue.Skip(startIndex).Take(itemsPerPage).ToList();
            List<SelectMenuOptionBuilder> options = queueItems.Select((track, index) => new SelectMenuOptionBuilder()
                .WithLabel($"{startIndex + index + 1}: {(track as CustomTrackQueueItem)?.Title}")
                .WithValue($"{originalIndex}:{startIndex + index}")
                .WithDescription("Select new position"))
                .ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId($"rearrange:{currentPage}")
                .WithPlaceholder("Select new position")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu);
            builder = BuildPaginationComponents(builder, currentPage, totalPages, "rearrange");
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = "Select new position for the track:";
                x.Components = builder.Build();
            });
        }

        private static ComponentBuilder BuildPaginationComponents(ComponentBuilder builder, int currentPage, int totalPages, string action)
        {
            builder.WithButton("Back", $"edit_buttons:back:{action}:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: currentPage == 1)
                   .WithButton("Next", $"edit_buttons:next:{action}:{currentPage}", ButtonStyle.Secondary, row: 1, disabled: currentPage == totalPages);
            return builder;
        }

        private SelectMenuBuilder BuildSelectMenu(string customId, List<ITrackQueueItem> queueItems, int startIndex, string placeholder, string description)
        {
            List<SelectMenuOptionBuilder> options = queueItems.Select((track, index) => new SelectMenuOptionBuilder()
                .WithLabel($"{startIndex + index + 1}: {(track as CustomTrackQueueItem)?.Title}")
                .WithValue($"{startIndex + index}")
                .WithDescription(description))
                .Take(25) // Ensure we only take 25 items
                .ToList();
            return new SelectMenuBuilder()
                .WithCustomId(customId)
                .WithPlaceholder(placeholder)
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
        }

        [ComponentInteraction("edit_buttons:*", runMode: RunMode.Async)]
        public async Task HandleQueuePagination(string customId)
        {
            await DeferAsync(ephemeral: true);
            string[] args = customId.Split(':');
            string action = args[0];
            string queueAction = args[1];
            int currentPage = int.Parse(args[2]);
            CustomPlayer? player = await _lavaLinkCommands.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            switch (action)
            {
                case "next":
                    await NavigateQueuePage(player, currentPage + 1, queueAction);
                    break;
                case "back":
                    await NavigateQueuePage(player, currentPage - 1, queueAction);
                    break;
            }
        }

        private async Task NavigateQueuePage(CustomPlayer player, int newPage, string selectedAction)
        {
            int totalItems = player.Queue.Count;
            int itemsPerPage = 24; // Max number of items per select menu
            int totalPages = (int)Math.Ceiling(totalItems / (double)itemsPerPage);
            switch (selectedAction)
            {
                case "playNext":
                    await ShowQueueEditMenu(player, "playnext", newPage);
                    break;
                case "remove":
                    await ShowQueueEditMenu(player, "remove", newPage);
                    break;
                case "rearrangePosition":
                    await ShowRearrangePositionMenu(player, 1, newPage);
                    break;
            }
        }

        [ComponentInteraction("rearrange:*")]
        public async Task HandleFinalRearrange(string customId, string[] selectedValues)
        {
            await DeferAsync(ephemeral: true);
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            string[] parts = selectedValues[0].Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int originalIndex) || !int.TryParse(parts[1], out int newIndex))
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
                return;
            }
            CustomPlayer? player = await _lavaLinkCommands.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            if (originalIndex < 0 || originalIndex >= player.Queue.Count || newIndex < 0 || newIndex >= player.Queue.Count)
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
                return;
            }
            ITrackQueueItem itemToMove = player.Queue.ElementAt(originalIndex);
            await player.Queue.RemoveAsync(itemToMove);
            await player.Queue.InsertAsync(newIndex, itemToMove);
            await ModifyOriginalResponseAsync(x =>
            {
                x.Content = $"Moved '{(itemToMove as CustomTrackQueueItem)?.Title}' to position {newIndex + 1} in the queue.";
                x.Components = new ComponentBuilder().Build();
            });
        }

        private async Task HandleInitialRearrange(CustomPlayer player, string[] selectedValues)
        {
            if (selectedValues.Length == 0)
            {
                await FollowupAsync("No track selected.", ephemeral: true);
                return;
            }
            if (!int.TryParse(selectedValues[0], out int originalIndex) || originalIndex < 0 || originalIndex >= player.Queue.Count)
            {
                await FollowupAsync("Invalid track selection.", ephemeral: true);
                return;
            }
            await ShowRearrangePositionMenu(player, originalIndex, 1);
        }
    }
}