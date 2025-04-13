using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Discord.Autocomplete;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Services;
using PlexBot.Utils;

using Color = Discord.Color;

namespace PlexBot.Core.Discord.Commands;

/// <summary>Provides discord slash commands for music playback with interactive UI components to control playback and manage the music queue</summary>
/// <param name="plexMusicService">Service that interfaces with Plex API to search and retrieve media from the library</param>
/// <param name="playerService">Service that manages audio player lifecycle and provides playback controls</param>
/// <param name="audioService">Lavalink audio service that handles the actual streaming and playback</param>
public class MusicCommands(IPlexMusicService plexMusicService, IPlayerService playerService, IAudioService audioService) 
    : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IPlexMusicService _plexMusicService = plexMusicService ?? throw new ArgumentNullException(nameof(plexMusicService));
    private readonly IPlayerService _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
    private readonly IAudioService _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

    /// <summary>Searches media libraries and displays interactive results that users can directly queue from the search results</summary>
    /// <param name="query">The search text to find matching media across available sources</param>
    /// <param name="source">The media source to search (Plex, YouTube, etc.) with autocomplete support</param>
    /// <returns>A task representing the asynchronous operation of the search and response</returns>
    [SlashCommand("search", "Search for music in your Plex library or other sources")]
    public async Task SearchCommand(
        [Summary("query", "What to search for")] string query,
        [Summary("source", "Where to search for music")]
    [Autocomplete(typeof(SourceAutocompleteHandler))]
    string source = "plex")
    {
        // Defer the response to give us time to search
        await DeferAsync(ephemeral: true);
        try
        {
            Logs.Debug($"Searching for '{query}' in {source}");
            if (string.IsNullOrWhiteSpace(query))
            {
                await FollowupAsync(embed: DiscordEmbedBuilder.Error("Invalid Query", "Please enter a search query."), ephemeral: true);
                return;
            }
            // Normalize source to lowercase
            source = source.ToLowerInvariant();
            // Handle the search based on the selected source
            switch (source)
            {
                case "plex":
                    await HandlePlexSearch(query);
                    break;
                case "youtube":
                    await HandleYouTubeSearch(query);
                    break;
                default:
                    await FollowupAsync(embed: DiscordEmbedBuilder.Error("Not Implemented", $"Search in {source} is not yet implemented."), ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in search command: {ex.Message}");
            await FollowupAsync(embed: DiscordEmbedBuilder.Error("Search Error", "An error occurred while searching. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Searches Plex library for media matching the query and displays interactive select menus for artists, albums, and tracks</summary>
    private async Task HandlePlexSearch(string query)
    {
        // Perform the search
        SearchResults results = await _plexMusicService.SearchLibraryAsync(query);
        if (!results.HasResults)
        {
            await FollowupAsync(embed: DiscordEmbedBuilder.Info("No Results", $"No results found for '{query}' in Plex."), ephemeral: true);
            return;
        }
        Logs.Debug($"Found {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks");
        // Build response with select menus for each result type
        ComponentBuilder builder = new();
        // Add artist select menu if we have artists
        if (results.Artists.Count > 0)
        {
            SelectMenuBuilder artistMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an artist")
                .WithCustomId("search:artist")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Artist artist in results.Artists.Take(25)) // Discord limit of 25 options
            {
                artistMenu.AddOption(
                    artist.Name,
                    artist.SourceKey,
                    TruncateDescription(artist.Summary));
            }
            builder.WithSelectMenu(artistMenu);
        }
        // Add album select menu if we have albums
        if (results.Albums.Count != 0)
        {
            SelectMenuBuilder albumMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an album")
                .WithCustomId("search:album")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Album album in results.Albums.Take(25)) // Discord Limit of 25 options
            {
                albumMenu.AddOption(
                    album.Title,
                    album.SourceKey,
                    $"Album by {album.Artist}");
            }
            if (builder.ActionRows.Count < 5) // Discord limit of 5 action rows
            {
                builder.WithSelectMenu(albumMenu);
            }
        }
        // Add track select menu if we have tracks
        if (results.Tracks.Count > 0)
        {
            SelectMenuBuilder trackMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select a track")
                .WithCustomId("search:track")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Track track in results.Tracks.Take(25))
            {
                trackMenu.AddOption(
                    track.Title,
                    track.SourceKey,
                    $"Track by {track.Artist}");
            }
            if (builder.ActionRows.Count < 5)
            {
                builder.WithSelectMenu(trackMenu);
            }
        }
        // Build the response
        string summary = $"Found {results.Artists.Count} artists, {results.Albums.Count} albums, and {results.Tracks.Count} tracks";
        await FollowupAsync(
            embed: DiscordEmbedBuilder.Info("Search Results", $"Search results for '{query}':\n{summary}"),
            components: builder.Build(),
            ephemeral: true);
    }

    /// <summary>Searches YouTube for tracks matching the query and presents results in an interactive select menu for quick playback</summary>
    private async Task HandleYouTubeSearch(string query)
    {
        try
        {
            Logs.Debug($"Searching YouTube for: {query}");
            TrackLoadResult searchResults = await _audioService.Tracks.LoadTracksAsync(
                query, TrackSearchMode.YouTube, cancellationToken: CancellationToken.None);
            List<LavalinkTrack> tracks = [.. searchResults.Tracks];
            if (tracks.Count == 0)
            {
                await FollowupAsync(embed: DiscordEmbedBuilder.Info("No Results", $"No results found for '{query}' on YouTube."), ephemeral: true);
                return;
            }
            Logs.Debug($"Found {tracks.Count} YouTube results");
            // Create a select menu for YouTube results
            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select a YouTube track")
                .WithCustomId("search:youtube")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (LavalinkTrack lavalinkTrack in tracks.Take(25))
            {
                // Get track information
                string title = lavalinkTrack.Title ?? "Unknown Title";
                string author = lavalinkTrack.Author ?? "Unknown";
                string duration = FormatDuration(lavalinkTrack.Duration);
                string trackId = lavalinkTrack.Uri?.ToString() ?? lavalinkTrack.Identifier;
                // Add to select menu
                selectMenu.AddOption(
                    // Limit title length for Discord UI
                    title.Length > 80 ? title[..77] + "..." : title,
                    trackId,
                    $"{author} | {duration}");
            }
            // Build and send component
            ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(selectMenu);
            await FollowupAsync(
                embed: DiscordEmbedBuilder.Info("YouTube Results", $"Found {Math.Min(tracks.Count, 25)} results on YouTube for '{query}'"),
                components: builder.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error searching YouTube: {ex.Message}");
            await FollowupAsync(embed: DiscordEmbedBuilder.Error("YouTube Search Error", "An error occurred while searching YouTube. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Converts a TimeSpan duration into a human-readable format (e.g., "3:45" or "1:23:45") for display in UI elements</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    /// <summary>Plays music from a Plex playlist.
    /// Allows users to quickly play entire playlists with optional shuffling.</summary>
    /// <param name="playlist">The playlist to play</param>
    /// <param name="shuffle">Whether to shuffle the playlist</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [SlashCommand("playlist", "Play a Plex playlist")]
    public async Task PlaylistCommand([Summary("playlist", "The playlist to play")]
    [Autocomplete(typeof(PlaylistAutocompleteHandler))]
    string playlist, [Summary("shuffle", "Shuffle the playlist")] bool shuffle = false)
    {
        await RespondAsync(embed: DiscordEmbedBuilder.Info("Loading", "Loading playlist..."), ephemeral: true); // Respond to acknowledge the command
        IUserMessage ackMessage = await GetOriginalResponseAsync();
        try
        {
            Logs.Debug($"Loading playlist: {playlist}, shuffle: {shuffle}");
            if (string.IsNullOrWhiteSpace(playlist))
            {
                await ackMessage.ModifyAsync(msg => msg.Embed = DiscordEmbedBuilder.Error("Invalid Playlist", "Please select a playlist."));
                //await FollowupAsync(embed: DiscordEmbedBuilder.Error("Invalid Playlist", "Please select a playlist."), ephemeral: true);
                return;
            }
            Playlist playlistDetails = await _plexMusicService.GetPlaylistDetailsAsync(playlist);
            if (playlistDetails.Tracks.Count == 0)
            {
                await ackMessage.ModifyAsync(msg => msg.Embed = DiscordEmbedBuilder.Info("Empty Playlist", $"Playlist '{playlistDetails.Title}' is empty."));
                //await FollowupAsync(embed: DiscordEmbedBuilder.Info("Empty Playlist", $"Playlist '{playlistDetails.Title}' is empty."), ephemeral: true);
                return;
            }
            // Get a list of tracks from the playlist to add to the queue
            List<Track> tracks = playlistDetails.Tracks;
            if (shuffle)
            {
                Random rng = new();
                tracks = [.. tracks.OrderBy(x => rng.Next())];
            }
            await _playerService.AddToQueueAsync(Context.Interaction, tracks);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in playlist command: {ex.Message}");
            await ackMessage.ModifyAsync(msg => msg.Embed = DiscordEmbedBuilder.Error("Playlist Error", "An error occurred while loading the playlist. Please try again later."));
            //await FollowupAsync(embed: DiscordEmbedBuilder.Error("Playlist Error", "An error occurred while loading the playlist. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Plays a single track by URL or search term.
    /// Provides a convenient shortcut for playing individual tracks without searching first.</summary>
    /// <param name="query">The track to play (URL or search term)</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [SlashCommand("play", "Play a track by URL or search term")]
    public async Task PlayCommand(
        [Summary("query", "The track to play (URL or search term)")]
        string query)
    {
        await DeferAsync();
        try
        {
            Logs.Info($"Play command: {query}");
            if (string.IsNullOrWhiteSpace(query))
            {
                await FollowupAsync(embed: DiscordEmbedBuilder.Error("Invalid Query", "Please enter a URL or search term."), ephemeral: true);
                return;
            }
            // Check if the query is a URL
            bool isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
            if (isUrl)
            {
                // URL handling would go here, but for now we'll treat it as a search
                await FollowupAsync(embed: DiscordEmbedBuilder.Info("URL Playback", "Direct URL playback is not yet implemented. Treating as a search query."), ephemeral: true);
            }
            // Search for the track
            SearchResults results = await _plexMusicService.SearchLibraryAsync(query);
            if (!results.HasResults)
            {
                await FollowupAsync(embed: DiscordEmbedBuilder.Error("No Results", $"No results found for '{query}'."), ephemeral: true);
                return;
            }
            // If we found tracks, play the first one
            if (results.Tracks.Count != 0)
            {
                Track track = results.Tracks.First();
                await _playerService.AddToQueueAsync(Context.Interaction, [track]);
                return;
            }
            // If we found albums but no tracks, play the first album
            if (results.Albums.Count != 0)
            {
                Album album = results.Albums.First();
                List<Track> tracks = await _plexMusicService.GetTracksAsync(album.SourceKey);
                if (tracks.Count != 0)
                {
                    await _playerService.AddToQueueAsync(Context.Interaction, tracks);
                    return;
                }
            }
            // If we found artists but no tracks or albums, play the first artist
            if (results.Artists.Count != 0)
            {
                Artist artist = results.Artists.First();
                List<Album> albums = await _plexMusicService.GetAlbumsAsync(artist.SourceKey);
                List<Track> allTracks = [];
                foreach (var album in albums)
                {
                    List<Track> tracks = await _plexMusicService.GetTracksAsync(album.SourceKey);
                    allTracks.AddRange(tracks);
                }
                if (allTracks.Count != 0)
                {
                    await _playerService.AddToQueueAsync(Context.Interaction, allTracks);
                    return;
                }
            }
            // If we get here, we found results but couldn't play anything
            await FollowupAsync(embed: DiscordEmbedBuilder.Error("Playback Error", "Found results, but couldn't play any tracks."), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in play command: {ex.Message}");
            await FollowupAsync(embed: DiscordEmbedBuilder.Error("Playback Error", "An error occurred while playing. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Shows information about the bot and available commands.
    /// Provides a user-friendly help interface with command examples.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [SlashCommand("help", "Shows information about the bot and available commands")]
    public async Task HelpCommand()
    {
        try
        {
            // Create a rich embed with command information
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle("📻 Plex Music Bot Help")
                .WithDescription("Play music from your Plex library directly in Discord voice channels.")
                .WithColor(Color.Blue)
                .WithCurrentTimestamp();
            // Add command sections
            embed.AddField("/search [query] [source]",
                "Search for music in your Plex library or other sources.\n" +
                "Example: `/search query:\"The Beatles\" source:\"plex\"`",
                false);
            embed.AddField("/playlist [playlist] [shuffle]",
                "Play a Plex playlist, optionally shuffled.\n" +
                "Example: `/playlist playlist:\"Summer Hits\" shuffle:true`",
                false);
            embed.AddField("/play [query]",
                "Quickly play music that matches your search.\n" +
                "Example: `/play query:\"Bohemian Rhapsody\"`",
                false);
            embed.AddField("Player Controls",
                "Use the buttons on the player message to control playback:\n" +
                "• **Play/Pause**: Toggle playback\n" +
                "• **Skip**: Move to the next track\n" +
                "• **Queue Options**: View and manage the queue\n" +
                "• **Repeat**: Set repeat mode\n" +
                "• **Kill**: Stop playback and disconnect",
                false);
            // Send the embed
            await RespondAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in help command: {ex.Message}");
            await RespondAsync(embed: DiscordEmbedBuilder.Error("Help Error", "An error occurred while generating help information."), ephemeral: true);
        }
    }

    /// <summary>Truncates a description to fit within Discord's select menu limits.
    /// Discord select menu option descriptions must be 100 characters or less.</summary>
    /// <param name="description">The description to truncate</param>
    /// <param name="maxLength">Maximum allowed length</param>
    /// <returns>The truncated description</returns>
    private static string TruncateDescription(string? description, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(description))
        {
            return "No description available";
        }
        return description.Length <= maxLength
            ? description
            : string.Concat(description.AsSpan(0, maxLength - 3), "...");
    }
}