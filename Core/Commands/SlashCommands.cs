﻿using Discord;
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
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle("Plex Music Bot Help")
                .WithThumbnailUrl(Context.Guild.IconUrl)
                .WithDescription("This bot allows you to play music from your Plex library and other sources directly in Discord. " +
                "Use the slash commands and buttons to control the playback.")
                .AddField("/search", "Search media from various sources. Example: `/search query:\"Your Query\" source:\"plex\"`", false)
                .AddField("/playlist", "Play songs from a specified Plex playlist. Example: `/playlist playlist:\"Your Playlist\"`", false)
                .WithColor(Color.Blue)
                .WithFooter(footer => footer.Text = "For more information, visit the support server.")
                .WithCurrentTimestamp();
            await RespondAsync(embed: embed.Build());
        }

        /// <summary>Main play command for plex</summary>
        [SlashCommand("youtube", "Plays a single track from YouTube", runMode: RunMode.Async)]
        public async Task Play(string search)
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
                .LoadTrackAsync(search, TrackSearchMode.YouTubeMusic)
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

        [SlashCommand("search", "Search media from various sources", runMode: RunMode.Async)]
        public async Task SearchCommand(
        [Summary("query", "The query to search for")] string query,
        [Autocomplete(typeof(AutoComplete.AutoComplete))]
        [Summary("source", "The source to search in (e.g., plex, youtube, soundcloud)")] string source = "plex")
        {
            await DeferAsync(ephemeral: true);
            Console.WriteLine($"Searching for: {query} in {source}...");
            try
            {
                Dictionary<string, List<Dictionary<string, string>>>? results = null;

                switch (source.ToLower())
                {
                    case "plex":
                        results = await _plexApi.SearchLibraryAsync(query);
                        break;
                    case "youtube":
                        // TODO: Add your YouTube search implementation here
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
                List<Dictionary<string, string>> trackDetails = await plexApi.GetTracks(playlistKey);
                if (trackDetails.Count == 0)
                {
                    await FollowupAsync("The playlist is empty or could not be loaded.");
                    Console.WriteLine("Playlist is empty or could not be loaded.");
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
