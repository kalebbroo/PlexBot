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
                    string? jsonResponse = await plexApi.PerformRequestAsync(plexApi.GetPlaybackUrl(selectedValue));
                    if (!string.IsNullOrEmpty(jsonResponse))
                    {
                        Dictionary<string, Dictionary<string, string>> parseTrack = await plexApi.ParseSearchResults(jsonResponse, customId);
                        List<Dictionary<string, string>> trackDetailsList = [];
                        foreach (var entry in parseTrack)
                        {
                            if (entry.Value.TryGetValue("Url", out string? value) && !string.IsNullOrEmpty(value))
                            {
                                string partKey = value;
                                string fullUrl = plexApi.GetPlaybackUrl(partKey);
                                entry.Value["Url"] = fullUrl;
                                Console.WriteLine($"Track URL: {fullUrl}"); // Debug
                            }
                            trackDetailsList.Add(entry.Value);
                        }
                        if (trackDetailsList.Count != 0)
                        {
                            await lavaLink.AddToQueue(Context.Interaction, trackDetailsList);
                            await FollowupAsync("Track(s) added to queue.", ephemeral: true);
                        }
                        else
                        {
                            await FollowupAsync("No tracks found to add to the queue.", ephemeral: true);
                        }
                    }
                    else
                    {
                        await FollowupAsync("Failed to retrieve track information.", ephemeral: true);
                    }
                    break;
                case "album":
                    // Display albums or tracks under an artist
                    await HandleAlbumOrArtist(selectedValue, "album");
                    break;
                case "artist":
                    // Display albums for the selected artist
                    await HandleAlbumOrArtist(selectedValue, "artist");
                    break;
            }
        }

        private async Task HandleAlbumOrArtist(string selectedValue, string type)
        {
            if (type == "artist")
            {
                // Fetch albums for the selected artist
                List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
                if (albums.Count > 0)
                {
                    albums.Add(new Dictionary<string, string> { { "Title", "Play All Albums" }, { "RatingKey", "play_all" } });
                }
                Console.WriteLine($"Albums: {albums}"); // Debug
                // Build select menu for albums
                List<SelectMenuOptionBuilder> options = albums.Select(album =>
                    new SelectMenuOptionBuilder()
                        .WithLabel(album["Title"])
                        .WithValue(album.TryGetValue("Url", out string? url) ? url : "N/A")
                        .WithDescription("Select an album")).ToList();

                SelectMenuBuilder menu = new SelectMenuBuilder()
                    .WithCustomId("handle_search:artist")
                    .WithPlaceholder("Select an album or play all")
                    .WithOptions(options)
                    .WithMinValues(1)
                    .WithMaxValues(1);

                ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
                await FollowupAsync("Select an album or play all:", components: components.Build(), ephemeral: true);
            }
            else if (type == "album")
            {
                // Fetch tracks for the selected album
                List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                if (tracks.Count > 0)
                {
                    tracks.Add(new Dictionary<string, string> { { "Title", "Play All Tracks" }, { "RatingKey", "play_all" } });
                }
                // Build select menu for tracks
                List<SelectMenuOptionBuilder> options = tracks.Select(track =>
                    new SelectMenuOptionBuilder()
                        .WithLabel(track["Title"])
                        .WithValue(track.TryGetValue("Url", out string? url) ? url : "N/A")
                        .WithDescription("Select a track")).ToList();

                SelectMenuBuilder menu = new SelectMenuBuilder()
                    .WithCustomId("handle_search:album")
                    .WithPlaceholder("Select a track or play all")
                    .WithOptions(options)
                    .WithMinValues(1)
                    .WithMaxValues(1);

                ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
                await FollowupAsync("Select a track or play all:", components: components.Build(), ephemeral: true);
            }
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
                    // Handle artist-specific actions
                    if (selectedValue == "play_all")
                    {
                        // Implement logic to play all albums for the selected artist
                        await FollowupAsync("Playing all albums for the selected artist.", ephemeral: true);
                        // Example call to a function that handles playback
                        // await PlayAllAlbumsForArtist(artistId);
                    }
                    else
                    {
                        // Fetch albums for the selected artist and update the menu
                        List<Dictionary<string, string>> albums = await plexApi.GetAlbums(selectedValue);
                        if (albums.Count > 0)
                        {
                            albums.Add(new Dictionary<string, string> { { "Title", "Play All Albums" }, { "RatingKey", "play_all" } });
                        }
                        List<SelectMenuOptionBuilder> options = albums.Select(album =>
                            new SelectMenuOptionBuilder()
                                .WithLabel(album["Title"])
                                .WithValue(album.TryGetValue("Url", out string? url) ? url : "N/A")
                                .WithDescription("Select an album to play or play all")).ToList();

                        SelectMenuBuilder menu = new SelectMenuBuilder()
                            .WithCustomId("handle_search:album")
                            .WithPlaceholder("Select an album or play all")
                            .WithOptions(options)
                            .WithMinValues(1)
                            .WithMaxValues(1);

                        ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
                        await FollowupAsync("Select an album or play all:", components: components.Build(), ephemeral: true);
                    }
                    break;
                case "album":
                    // Handle album-specific actions
                    if (selectedValue == "play_all")
                    {
                        // Implement logic to play all tracks for the selected album
                        await FollowupAsync("Playing all tracks for the selected album.", ephemeral: true);
                        // Example call to a function that handles playback
                        // await PlayAllTracksForAlbum(albumId);
                    }
                    else
                    {
                        // Fetch tracks for the selected album and update the menu
                        List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                        if (tracks.Count > 0)
                        {
                            tracks.Add(new Dictionary<string, string> { { "Title", "Play All Tracks" }, { "RatingKey", "play_all" } });
                        }
                        List<SelectMenuOptionBuilder> options = tracks.Select(track =>
                            new SelectMenuOptionBuilder()
                                .WithLabel(track["Title"])
                                .WithValue(track.TryGetValue("Url", out string? url) ? url : "N/A")
                                .WithDescription("Select a track to play or play all")).ToList();

                        SelectMenuBuilder menu = new SelectMenuBuilder()
                            .WithCustomId("handle_search:track")
                            .WithPlaceholder("Select a track or play all")
                            .WithOptions(options)
                            .WithMinValues(1)
                            .WithMaxValues(1);

                        ComponentBuilder components = new ComponentBuilder().WithSelectMenu(menu);
                        await FollowupAsync("Select a track or play all:", components: components.Build(), ephemeral: true);
                    }
                    break;
                case "track":
                    // Specific track selected or play all tracks in an album
                    if (selectedValue == "play_all")
                    {
                        // Assume you have a method to get all tracks of an album
                        List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                        await lavaLink.AddToQueue(Context.Interaction, tracks);
                        await FollowupAsync("Playing all tracks.");
                    }
                    else
                    {
                        // Single track selection, fetch and queue the track
                        List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                        Dictionary<string, string>? trackDetails = tracks.FirstOrDefault(t => t["Url"].Contains(selectedValue));
                        if (trackDetails != null)
                        {
                            await lavaLink.AddToQueue(Context.Interaction, new List<Dictionary<string, string>> { trackDetails });
                            await FollowupAsync($"Playing track: {trackDetails["Title"]}");
                        }
                        else
                        {
                            await FollowupAsync("Track details not found.");
                        }
                    }
                    break;
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
