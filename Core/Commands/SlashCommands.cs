using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Players.Queued;
using PlexBot.Core.LavaLink;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.InteractionComponents;
using PlexBot.Core.Players;
using System.Net;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PlexBot.Core.Commands
{
    public class SlashCommands(IAudioService audioService, LavaLinkCommands lavaLinkCommands, PlexApi plexApi, Players.Players visualPlayer, IMemoryCache memoryCache,
        SelectMenus selectMenus) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IAudioService _audioService = audioService;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLinkCommands;
        private readonly PlexApi _plexApi = plexApi;
        private readonly SelectMenus _selectMenus = selectMenus;
        private readonly Players.Players _players = visualPlayer;

        /// <summary>Responds with help information about how to use the bot, including available commands.</summary>
        [SlashCommand("help", "Learn how to use the bot")]
        public async Task HelpCommand()
        {
            try
            {
#warning TODO: Update help message
                EmbedBuilder embed = new EmbedBuilder()
                    .WithTitle("Hartsy.AI Bot Help")
                    .WithThumbnailUrl(Context.Guild.IconUrl)
                    .WithDescription("Hartsy.AI is the premier Stable Diffusion platform for generating images with text directly in Discord. " +
                    "\n\nOur custom Discord bot enables users to generate images with text using our fine-tuned templates, choose your favorite " +
                    "images to send to #showcase for community voting, and potentially get featured weekly on the server. \n\nDiscover more and subscribe at: https://hartsy.ai")
                    .AddField("Available Slash Commands", "Checked the pinned messages for a more detailed explanation of these commands.", false)
                    .AddField("/generate", "Generate an image based on the text you provide, select a template, and optionally add extra prompt " +
                    "information. Example: `/generate_logo text:\"Your Text\" template:\"Template Name\" additions:\"Extra Prompt\"`", false)
                    .AddField("/user_info", "Check the status of your subscription and see how many tokens you have left for image generation. Example: `/user_info`", false)
                    .AddField("/help", "Shows this help message. Example: `/help`", false)
                    .WithColor(Color.Blue)
                    .WithFooter(footer => footer.Text = "For more information, visit Hartsy.AI")
                    .WithCurrentTimestamp();
                await RespondAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await RespondAsync($"An error occurred: {ex.Message}", ephemeral: true);
            }
        }

        /// <summary>Main play command for plex</summary>
        [SlashCommand("play", "Plays music from YouTube", runMode: RunMode.Async)]
        public async Task Play(string query)
        {
            await DeferAsync().ConfigureAwait(false);
            SocketInteraction interaction = Context.Interaction;
            var player = await lavaLinkCommands.GetPlayerAsync(interaction, connectToVoiceChannel: true).ConfigureAwait(false);
            if (player == null)
            {
                await FollowupAsync("You need to be in a voice channel.").ConfigureAwait(false);
                return;
            }
            // Load the track from YouTube using the query provided
            var track = await audioService.Tracks
                .LoadTrackAsync(query, TrackSearchMode.YouTubeMusic)
                .ConfigureAwait(false);
            // If no track was found, we send an error message to the user.
            if (track is null)
            {
                await FollowupAsync("😖 No results.").ConfigureAwait(false);
                return;
            }
            // Play the track
            await player.PlayAsync(track);
            await FollowupAsync($"Playing: {track.Title}").ConfigureAwait(false);
        }

        [SlashCommand("search", "Search Plex for media", runMode: RunMode.Async)]
        public async Task SearchCommand(
            [Summary("query", "The query to search for")] string query)
        {
            await DeferAsync(ephemeral: true);
            Console.WriteLine($"Searching for: {query}...");

            try
            {
                Dictionary<string, List<Dictionary<string, string>>> results = await _plexApi.SearchLibraryAsync(query);
                Console.WriteLine("After search library async"); // Debugging
                if (results == null || results.Count == 0)
                {
                    await FollowupAsync("No results found.", ephemeral: true);
                    return;
                }
                Console.WriteLine($"API Results: {JsonConvert.SerializeObject(results)}"); // Debugging

                if (results.ContainsKey("Artists") && results["Artists"].Count > 0)
                {
                    await SendSelectMenu("Artists", results["Artists"], "Select an artist");
                }

                if (results.ContainsKey("Albums") && results["Albums"].Count > 0)
                {
                    await SendSelectMenu("Albums", results["Albums"], "Select an album");
                }

                if (results.ContainsKey("Tracks") && results["Tracks"].Count > 0)
                {
                    await SendSelectMenu("Tracks", results["Tracks"], "Select a track");
                }
            }
            catch (Exception ex)
            {
                await FollowupAsync("An error occurred: " + ex.Message, ephemeral: true);
                Console.WriteLine($"Error in the Try block of the slash command: {ex.Message}");
            }
        }

        private async Task SendSelectMenu(string title, List<Dictionary<string, string>> items, string placeholder)
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

            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithCustomId($"search_plex:{title.ToLower()}")
                .WithPlaceholder(placeholder)
                .WithOptions(selectMenuOptions)
                .WithMinValues(1)
                .WithMaxValues(1);

            await FollowupAsync($"Select a {title.ToLower()} to play.", components: new ComponentBuilder().WithSelectMenu(selectMenu).Build(), ephemeral: true);
        }

        [SlashCommand("playlist", "Play a playlist", runMode: RunMode.Async)]
        public async Task PlaylistCommand(
        [Summary("playlist", "Choose a playlist.")]
        [Autocomplete(typeof(AutoComplete.AutoComplete))] string playlistKey,
        [Summary("shuffle", "Shuffle the playlist.")] bool shuffle = false)
        {
            await RespondAsync("Loading playlist...", ephemeral: true);
            try
            {
                // Retrieve track details from the playlist
                List<Dictionary<string, string>> trackDetails = await plexApi.GetTracks(playlistKey);
                if (trackDetails.Count == 0)
                {
                    await FollowupAsync("The playlist is empty or could not be loaded.");
                    Console.WriteLine("Playlist is empty or could not be loaded.");
                    return;
                }
                // Optionally shuffle the playlist
                if (shuffle)
                {
                    Random rng = new();
                    trackDetails = [.. trackDetails.OrderBy(x => rng.Next())];
                }
                SocketInteraction interaction = Context.Interaction;
                await lavaLinkCommands.AddToQueue(interaction, trackDetails);
            }
            catch (Exception ex)
            {
                await FollowupAsync($"An error occurred: {ex.Message}", ephemeral: true);
                Console.WriteLine(ex.ToString());
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
            string uri = "";
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
            uri = plexApi.GetSearchUrl(uri);
            string? response = await plexApi.PerformRequestAsync(uri);
            Console.WriteLine(response);
            await FollowupAsync("Check the console for the response.");
        }
    }
}
