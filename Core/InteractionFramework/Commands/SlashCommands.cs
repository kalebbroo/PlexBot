namespace PlexBot.Core.InteractionFramework.Commands;

public class SlashCommands : InteractionsCore
{
    private readonly ILogger<SlashCommands> _logger;
    private readonly IAudioService _audioService;
    private readonly PlexCore _plexCore;
    private readonly PlexMusic _plexMusic;

    //TODO: Since plexMusic inherits from PlexCore, we can remove the PlexCore dependency most likely. Needs testing.
    public SlashCommands(ILogger<SlashCommands> logger, IAudioService audioService, PlexCore plexCore, PlexMusic plexMusic, LavaLinkCommands lavaLink)
    : base(lavaLink)
    {
        _logger = logger;
        _audioService = audioService;
        _lavaLink = lavaLink;
        _plexCore = plexCore;
        _plexMusic = plexMusic;
    }

    /// <summary>Responds with help information about how to use the bot, including available commands.</summary>
    [SlashCommand("help", "Learn how to use the bot")]
    public async Task HelpCommand()
    {
        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle("Plex Music Bot Help")
            .WithThumbnailUrl(Context.Guild.IconUrl)
            .WithDescription("This bot allows you to play music from your Plex library and other sources directly in Discord. " +
            "Use the slash commands and buttons to control the playback.")
            .AddField("/search", "Search media from various sources. Example: `/search query:\"Your Query\" source:\"plex\"`", false)
            .AddField("/playlist", "Play songs from a specified Plex playlist. Example: `/playlist playlist:\"Your Playlist\"`", false)
            .WithColor(Discord.Color.Blue)
            .WithFooter(footer => footer.Text = "For more information, visit the support server.")
            .WithCurrentTimestamp();
        await RespondAsync(embed: embed.Build());
    }

    [SlashCommand("search", "Search media from various sources", runMode: RunMode.Async)]
    public async Task SearchCommand(
    [Summary("query", "The query to search for")] string query,
    [Autocomplete(typeof(AutoComplete.AutoComplete))]
        [Summary("source", "The source to search in (e.g., plex, youtube, soundcloud)")] string source = "plex")
    {
        await DeferAsync(ephemeral: true);
        _logger.LogInformation("Searching for: {query} in {source}...", query, source);
        try
        {
            Dictionary<string, List<Dictionary<string, string>>> results = [];
            string service = "";
            switch (source.ToLower())
            {
                case "plex":
                    // Rework this search lib method to either be SearchMusicLib or 1 method to search all libs
                    results = await _plexMusic.SearchLibraryAsync(query);
                    break;
                case "youtube":
                    TrackLoadResult ytSearch = await _audioService.Tracks.LoadTracksAsync(query, TrackSearchMode.YouTubeMusic);
                    _logger.LogDebug("{JsonSerializedObject}", JsonConvert.SerializeObject(ytSearch));
                    List<Dictionary<string, string>> ytResults = ytSearch.Tracks.Select(track => new Dictionary<string, string>
                        {
                            { "Title", track.Title },
                            { "Description", track.ProbeInfo! },
                            { "TrackKey", track.Identifier },
                            { "Artist", track.Author },
                            { "Duration", track.Duration.ToString() },
                            { "Url", track.Uri!.ToString() },
                            { "Artwork", track.ArtworkUri!.ToString() }
                        }).ToList();
                    results.Add("Tracks", ytResults);
                    service = "youtube";
                    _logger.LogDebug("{JsonSerializedObject}", JsonConvert.SerializeObject(results));
                    break;
                case "soundcloud":
                    // TODO: Add your SoundCloud search implementation here
                    break;
                case "twitch":
                    // TODO: Add your Twitch search implementation here
                    break;
                case "vimeo":
                    // TODO: Add your Vimeo search implementation here
                    break;
                case "bandcamp":
                    // TODO: Add your Bandcamp search implementation here
                    break;
                default:
                    await FollowupAsync("Invalid source specified.", ephemeral: true);
                    return;
            }
            if (results == null || results.Count == 0)
            {
                await FollowupAsync("No results found.", ephemeral: true);
                return;
            }
            if (results.TryGetValue("Artists", out List<Dictionary<string, string>>? artists) && artists.Count > 0)
            {
                if (artists.FirstOrDefault()?.TryGetValue("TrackKey", out var trackKey) == true)
                {
                    await SendSelectMenu($"Artists:{trackKey}:{service}", artists, "Select an Artist");
                }
                else
                {
                    await FollowupAsync("No valid TrackKey found.", ephemeral: true);
                }
            }
            if (results.TryGetValue("Albums", out List<Dictionary<string, string>>? albums) && albums.Count > 0)
            {
                if (albums.FirstOrDefault()?.TryGetValue("TrackKey", out var trackKey) == true)
                {
                    await SendSelectMenu($"Albums:{trackKey}:{service}", albums, "Select an album");
                }
                else
                {
                    await FollowupAsync("No valid TrackKey found.", ephemeral: true);
                }
            }
            if (results.TryGetValue("Tracks", out List<Dictionary<string, string>>? tracks) && tracks.Count > 0)
            {
                if (tracks.FirstOrDefault()?.TryGetValue("TrackKey", out var trackKey) == true)
                {
                    await SendSelectMenu($"Tracks:{trackKey}:{service}", tracks, "Select a track");
                }
                else
                {
                    await FollowupAsync("No valid TrackKey found.", ephemeral: true);
                }
            }
        }
        catch (Exception ex)
        {
            await FollowupAsync("An error occurred: " + ex.Message, ephemeral: true);
            _logger.LogError(ex, "Error in the Try block of the slash command: {ErrorMessage}", ex.Message);
        }
    }

    private async Task SendSelectMenu(string customId, List<Dictionary<string, string>> items, string placeholder)
    {
        List<SelectMenuOptionBuilder> selectMenuOptions = items.Select(item =>
        {
            string description = item["Description"] ?? "No description available.";
            description = description.Length > 100 ? description[..97] + "..." : description;
            return new SelectMenuOptionBuilder()
                .WithLabel(item["Title"] ?? "Unknown Title")
                .WithValue(item["TrackKey"] ?? "N/A")
                .WithDescription(description);
        }).ToList();
        string[] source = customId.Split(':');
        string title = source[0];
        string service = source[1];
        SelectMenuBuilder selectMenu = new SelectMenuBuilder()
            .WithCustomId($"search_plex:{customId.ToLower()}")
            .WithPlaceholder(placeholder)
            .WithOptions(selectMenuOptions)
            .WithMinValues(1)
            .WithMaxValues(1);
        await FollowupAsync($"Select a {title.ToLower()} to play.", components: new ComponentBuilder().WithSelectMenu(selectMenu).Build(), ephemeral: true);
    }

    [SlashCommand("playlist", "Play a playlist from Plex", runMode: RunMode.Async)]
    public async Task PlaylistCommand(
    [Summary("playlist", "Choose a playlist.")]
    [Autocomplete(typeof(AutoComplete.AutoComplete))] string playlistKey,
    [Summary("shuffle", "Shuffle the playlist.")] bool shuffle = false)
    {
        await RespondAsync("Loading playlist...", ephemeral: true);
        try
        {
            // Retrieve track details from the playlist
            List<Dictionary<string, string>> trackDetails = await _plexMusic.GetTracks(playlistKey);
            if (trackDetails.Count == 0)
            {
                await FollowupAsync("The playlist is empty or could not be loaded.");
                _logger.LogInformation("Playlist is empty or could not be loaded.");
                return;
            }
            // Optionally shuffle the playlist before adding it to the queue
            // This is intentionally seperate from shuffling the queue to allow
            // an added playlist to be shuffled without affecting the current queue
            if (shuffle)
            {
                Random rng = new();
                trackDetails = [.. trackDetails.OrderBy(x => rng.Next())];
            }
            SocketInteraction interaction = Context.Interaction;
            await _lavaLink.AddToQueue(interaction, trackDetails);
        }
        catch (Exception ex)
        {
            await FollowupAsync($"An error occurred: {ex.Message}", ephemeral: true);
            _logger.LogError(ex, "Error in the Try block of the slash command ({command}): {ErrorMessage}", nameof(PlaylistCommand), ex.Message);
        }
    }

    // test command to get an API response in json format
    [SlashCommand("test_response", "returns json in the console")]
    public async Task TestCommand(
    [Summary("type", "The type of request to make")]
        [Choice("AllPlaylists", "playlists"),
         Choice("Songs", "songs"),
         Choice("Artists", "artists"),
         Choice("Albums", "albums"),
         Choice("SearchSongs", "search_songs"),
         Choice("SearchArtists", "search_artists"),
         Choice("SearchAlbums", "search_albums"),
         Choice("PlaylistContents", "playlist_contents")]
        string type,
    [Summary("query", "Query to search for, required for searches and optional for playlists")]
        string? query = null,
    [Summary("playlistID", "Playlist ID, required for fetching playlist contents")]
        string? playlistID = null)
    {
        await DeferAsync();
        string uri;
        switch (type)
        {
            case "playlists":
                uri = "/playlists?playlistType=audio";
                break;
            case "songs":
                uri = "/library/sections/5/all?type=10";
                break;
            case "artists":
                uri = "/library/sections/5/all?type=8";
                break;
            case "albums":
                uri = "/library/sections/5/all?type=9";
                break;
            case "search_songs":
                uri = $"/library/sections/5/search?type=10&query={WebUtility.UrlEncode(query)}";
                break;
            case "search_artists":
                uri = $"/library/sections/5/search?type=8&query={WebUtility.UrlEncode(query)}";
                break;
            case "search_albums":
                uri = $"/library/sections/5/search?type=9&query={WebUtility.UrlEncode(query)}";
                break;
            case "playlist_contents":
                if (string.IsNullOrEmpty(playlistID))
                {
                    await FollowupAsync("Playlist ID is required for fetching playlist contents.");
                    return;
                }
                uri = $"/playlists/{playlistID}/items?type=10";
                break;
            default:
                await FollowupAsync("Invalid type.");
                return;
        }
        uri = _plexCore.GetSearchUrl(uri);
        string? response = await _plexCore.PerformRequestAsync(uri);
        _logger.LogInformation("response");
        await FollowupAsync("Check the console for the response.");
    }
}