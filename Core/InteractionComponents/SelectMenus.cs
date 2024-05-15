using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.Players;
using PlexBot.Core.LavaLink;
using Lavalink4NET.Players;

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
                            Dictionary<string, string>? trackDetails = await _plexApi.GetTrackDetails(selectedValue);
                            if (trackDetails != null)
                            {
                                await _lavaLinkCommands.AddToQueue(Context.Interaction, [trackDetails]);
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
                            List<Dictionary<string, string>> tracks = await _plexApi.GetTracks(selectedValue);
                            if (tracks != null && tracks.Count > 0)
                            {
                                await _lavaLinkCommands.AddToQueue(Context.Interaction, tracks);
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
                            List<Dictionary<string, string>> albums = await _plexApi.GetAlbums(selectedValue);
                            List<Dictionary<string, string>> allTracks = [];
                            foreach (var album in albums)
                            {
                                List<Dictionary<string, string>> tracks = await _plexApi.GetTracks(album["TrackKey"]);
                                if (tracks != null && tracks.Count > 0)
                                {
                                    allTracks.AddRange(tracks);
                                }
                            }
                            if (allTracks.Count > 0)
                            {
                                await _lavaLinkCommands.AddToQueue(Context.Interaction, allTracks);
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
            string selectedAction = selectedValues[0];
            switch (customId)
            {
                case "edit":
                    switch (selectedAction)
                    {
                        case "playNext":
                            int totalItems = player!.Queue.Count;
                            int itemsPerPage = 24; // Max number of items per select menu
                            int totalPages = (int)Math.Ceiling(totalItems / (double)itemsPerPage);
                            for (int page = 1; page <= totalPages; page++)
                            {
                                int startIndex = (page - 1) * itemsPerPage;
                                List<ITrackQueueItem> queueItems = player.Queue.Skip(startIndex).Take(itemsPerPage).ToList();
                                List<SelectMenuOptionBuilder> options = queueItems.Select((track, index) => new SelectMenuOptionBuilder()
                                    .WithLabel($"{startIndex + index + 1}: {track?.Track!.Title}")
                                    .WithValue($"{startIndex + index}")
                                    .WithDescription("Move to play next"))
                                    .ToList();
                                SelectMenuBuilder menu = new SelectMenuBuilder()
                                    .WithCustomId($"queue:selectNextPage{page}")
                                    .WithPlaceholder("Select a track to play next")
                                    .WithOptions(options)
                                    .WithMinValues(1)
                                    .WithMaxValues(1);
                                ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu);
                                await FollowupAsync("Select a track to play next:", components: builder.Build(), ephemeral: true);
                            }
                            break;

                        case "remove":
                            // Implement logic to remove a track from the queue
                            break;
                        case "rearrange":
                            // Implement logic to rearrange tracks within the queue
                            break;
                    }
                    break;
                case "playNext":
                    if (selectedValues.Length == 0)
                    {
                        await FollowupAsync("No track selected.", ephemeral: true);
                        return;
                    }
                    int selectedIndex = int.Parse(selectedValues[0]);
                    if (player == null)
                    {
                        await FollowupAsync("Player not found.", ephemeral: true);
                        return;
                    }
                    if (selectedIndex >= 0 && selectedIndex < player.Queue.Count)
                    {
                        ITrackQueueItem itemToMove = player.Queue.ElementAt(selectedIndex);
                        await player.Queue.RemoveAsync(itemToMove);
                        await player.Queue.InsertAsync(0, itemToMove);
                        await FollowupAsync($"Moved '{itemToMove?.Track!.Title}' to the top of the queue!", ephemeral: true);
                    }
                    else
                    {
                        await FollowupAsync("Invalid track selection.", ephemeral: true);
                    }
                    break;
                
            }
        }
    }
}
