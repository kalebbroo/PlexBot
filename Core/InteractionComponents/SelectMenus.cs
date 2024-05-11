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
                await FollowupAsync("No selection made.");
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
                Dictionary<string, Dictionary<string, string>> parseTrack = await plexApi.ParseSearchResults(jsonResponse, "track");
                List<Dictionary<string, string>> trackDetailsList = [.. parseTrack.Values];
                await lavaLink.AddToQueue(Context.Interaction, trackDetailsList);
                await FollowupAsync("Track(s) added to queue.", ephemeral: true);
            }
            else
            {
                await FollowupAsync("Failed to retrieve track information.", ephemeral: true);
            }
        }

        private async Task HandleAlbumSelection(string selectedValue)
        {
            List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
            tracks.Add(new Dictionary<string, string> { { "Title", "Play All Tracks" }, { "RatingKey", "play_all" } });
            List<SelectMenuOptionBuilder> options = tracks.Select(track =>
                new SelectMenuOptionBuilder()
                    .WithLabel(track["Title"])
                    .WithValue(track.TryGetValue("TrackKey", out string? trackKey) ? trackKey : "N/A")
                    .WithDescription("Select a track")).ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId("handle_search:album")
                .WithPlaceholder("Select a track or play all")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            Console.WriteLine($"HandleAlbumSelection: {tracks[0].TryGetValue("TrackKey", out string? albumKey)}");
            ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
            IComponentInteraction interaction = (IComponentInteraction)Context.Interaction;
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Select a track or play all:";
                msg.Components = components.Build();
            });
            //await FollowupAsync("Select a track or play all:", components: components.Build(), ephemeral: true);
        }

        private async Task HandleArtistSelection(string selectedValue)
        {
            List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
            albums.Add(new Dictionary<string, string> { { "Title", "Play All Albums" }, { "RatingKey", "play_all" } });
            List<SelectMenuOptionBuilder> options = albums.Select(album =>
                new SelectMenuOptionBuilder()
                    .WithLabel(album["Title"])
                    .WithValue(album.TryGetValue("TrackKey", out string? albumKey) ? albumKey : "N/A")
                    .WithDescription("Select an album")).ToList();
            SelectMenuBuilder menu = new SelectMenuBuilder()
                .WithCustomId("handle_search:artist")
                .WithPlaceholder("Select an album or play all")
                .WithOptions(options)
                .WithMinValues(1)
                .WithMaxValues(1);
            ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
            Console.WriteLine($"HandleArtistSelection: {albums[0].TryGetValue("TrackKey", out string? albumKey)}");
            IComponentInteraction interaction = (IComponentInteraction)Context.Interaction;
            await interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "Select an album or play all:";
                msg.Components = components.Build();
            });
            //await FollowupAsync("Select an album or play all:", components: components.Build(), ephemeral: true);
        }

        [ComponentInteraction("handle_search:*", runMode: RunMode.Async)]
        public async Task HandleSearchResults(string customId, string[] selections)
        {
            await DeferAsync(ephemeral: true);
            string selectedValue = selections.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(selectedValue))
            {
                await FollowupAsync("No selection made.");
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
            if (selectedValue == "play_all")
            {
                List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
                List<Dictionary<string, string>> tracks = new List<Dictionary<string, string>>();
                foreach (var album in albums)
                {
                    tracks.AddRange(await plexApi.GetTracks(album["RatingKey"]));
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
            if (selectedValue == "play_all")
            {
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await ModifyOriginalResponseAsync(msg => msg.Content = "Playing all tracks for the selected album.");
            }
            else
            {
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await FollowupAsync("Track(s) added to queue.", ephemeral: true);
            }
        }

        private async Task HandleTrackSearchResult(string selectedValue)
        {
            if (selectedValue == "play_all")
            {
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                await lavaLink.AddToQueue(Context.Interaction, tracks);
                await FollowupAsync("Playing all tracks.");
            }
            else
            {
                // Get the track details using the selectedValue as the track key
                Dictionary<string, string>? trackDetails = await plexApi.GetTrackDetails(selectedValue);
                if (trackDetails != null)
                {
                    await lavaLink.AddToQueue(Context.Interaction, [trackDetails]);
                    await FollowupAsync($"Playing track: {trackDetails["Title"]}");
                }
                else
                {
                    await FollowupAsync("Track details not found.");
                }
            }
        }

        [ComponentInteraction("queue:*")]
        public async Task HandlePlayNextSelection(string customId, string[] selectedValues)
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
