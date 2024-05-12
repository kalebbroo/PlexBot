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
            switch (customId)
            {
                case "track":
                    await HandleTrackSelection(selectedValue);
                    break;
                case "album":
                    await HandleAlbumSelection(selectedValue);
                    break;
                case "artist":
                    await HandleArtistSelection(selectedValue);
                    break;
            }
        }

        private async Task HandleTrackSelection(string selectedValue)
        {
            string? jsonResponse = await plexApi.PerformRequestAsync(plexApi.GetPlaybackUrl(selectedValue));
            if (!string.IsNullOrEmpty(jsonResponse))
            {
                Console.WriteLine($"Calling GetTrackDetails with {selectedValue} From HandleTrackSelection"); // debug
                Dictionary<string, string>? trackDetails = await plexApi.GetTrackDetails(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, [trackDetails]);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Track(s) added to queue.");
            }
            else
            {
                await ModifyOriginalResponseAsync(msg => msg.Content = "Failed to retrieve track information.");
            }
        }

        private async Task HandleAlbumSelection(string albumKey)
        {
            if (albumKey.StartsWith("play_all:"))
            {
                string[] parts = albumKey.Split(':');
                albumKey = parts[1];
            }
            Console.WriteLine($"Calling GetTracks with {albumKey} From HandleAlbumSelection"); // debug
            List<Dictionary<string, string>> tracks = await plexApi.GetTracks(albumKey);
            tracks.Add(new Dictionary<string, string> { { "Title", "Play All Tracks" }, { "TrackKey", "play_all:" + albumKey } });
            List<SelectMenuOptionBuilder> options = tracks.Select(track =>
                new SelectMenuOptionBuilder()
                    .WithLabel(track["Title"])
                    .WithValue(track.TryGetValue("TrackKey", out string? trackKey) ? trackKey : "TrackKey Missing!!")
                    .WithDescription("Select a track")).ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId("handle_search:album")
                .WithPlaceholder("Select a track or play all")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
            IComponentInteraction interaction = (IComponentInteraction)Context.Interaction;
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Select a track or play all:";
                msg.Components = components.Build();
            });
        }

        private async Task HandleArtistSelection(string selectedValue)
        {
            Console.WriteLine($"Calling GetAlbums with {selectedValue} From HandleArtistSelection"); // debug
            List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
            albums.Add(new Dictionary<string, string> { { "Title", "Play All Albums" }, { "TrackKey", "play_all:" + selectedValue } });
            List<SelectMenuOptionBuilder> options = albums.Select(album =>
                new SelectMenuOptionBuilder()
                    .WithLabel(album["Title"])
                    .WithValue(album.TryGetValue("TrackKey", out string? albumKey) ? albumKey : "Missing TrackKey in HandleArtistSelection")
                    .WithDescription("Select an album")).ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId("handle_search:artist")
                .WithPlaceholder("Select an album or play all")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
            IComponentInteraction interaction = (IComponentInteraction)Context.Interaction;
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Select an album or play all:";
                msg.Components = components.Build();
            });
        }

        [ComponentInteraction("handle_search:*", runMode: RunMode.Async)]
        public async Task HandleSearchResults(string customId, string[] selections)
        {
            await DeferAsync(ephemeral: true);
            string selectedValue = selections.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(selectedValue))
            {
                await ModifyOriginalResponseAsync(msg => msg.Content = "Invalid selection made.");
                return;
            }
            switch (customId)
            {
                case "artist":
                    await HandleArtistSearchResult(selectedValue);
                    break;
                case "album":
                    await HandleAlbumSearchResult(selectedValue);
                    break;
                case "track":
                    await HandleTrackSearchResult(selectedValue);
                    break;
            }
        }

        private async Task HandleArtistSearchResult(string selectedValue)
        {
            if (selectedValue.StartsWith("play_all:"))
            {
                string[] parts = selectedValue.Split(':');
                string artistKey = parts[1];
                Console.WriteLine($"Calling GetAlbums with {artistKey} From HandleArtistSearchResult"); // debug
                List<Dictionary<string, string>> albums = await plexApi.GetAlbums(artistKey);
                List<Dictionary<string, string>> tracks = [];
                foreach (var album in albums)
                {
                    tracks.AddRange(await plexApi.GetTracks(album["TrackKey"]));
                }
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Playing all albums for the selected artist.");
            }
            else
            {
                await HandleAlbumSelection(selectedValue);
            }
        }

        private async Task HandleAlbumSearchResult(string selectedValue)
        {
            if (selectedValue.StartsWith("play_all:"))
            {
                string[] parts = selectedValue.Split(':');
                string albumKey = parts[1];
                Console.WriteLine($"Calling GetTracks with {albumKey} From HandleAlbumSearchResult"); // debug
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(albumKey);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Playing all tracks for the selected album.");
            }
            else
            {
                Console.WriteLine($"Calling GetTracks with {selectedValue} From HandleAlbumSearchResult"); // debug
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Track(s) added to queue.");
            }
        }

        private async Task HandleTrackSearchResult(string selectedValue)
        {
            if (selectedValue.StartsWith("play_all:"))
            {
                Console.WriteLine($"Calling GetTracks with {selectedValue} From HandleTrackSearchResult"); // debug
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Playing all tracks.");
            }
            else
            {
                // Get the track details using the selectedValue as the track key
                Console.WriteLine($"Calling GetTrackDetails with {selectedValue} From HandleTrackSearchResult"); // debug
                Dictionary<string, string>? trackDetails = await plexApi.GetTrackDetails(selectedValue);
                if (trackDetails != null)
                {
                    await lavaLink.AddToQueue(Context.Interaction, [trackDetails]);
                    await ModifyOriginalResponseAsync(msg => msg.Content = $"Playing track: {trackDetails["Title"]}");
                }
                else
                {
                    await ModifyOriginalResponseAsync(msg => msg.Content = "Track details not found.");
                }
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
