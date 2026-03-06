using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Discord.Autocomplete;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Utils;

using PlexBot.Core.Models.Players;
using PlexBot.Core.Services;
using PlexBot.Core.Services.Music;

namespace PlexBot.Core.Discord.Commands;

/// <summary>Provides discord slash commands for music playback with interactive UI components to control playback and manage the music queue</summary>
public class MusicCommands(IPlexMusicService plexMusicService, IPlayerService playerService,
    IAudioService audioService, MusicProviderRegistry providerRegistry)
    : InteractionModuleBase<SocketInteractionContext>
{

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
                await FollowupAsync(components: ComponentV2Builder.Error("Invalid Query", "Please enter a search query."), ephemeral: true);
                return;
            }
            // Normalize source to lowercase
            source = source.ToLowerInvariant();
            // Route search through the provider registry
            IMusicProvider? provider = providerRegistry.GetProvider(source);
            if (provider == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Unknown Source", $"Music source '{source}' is not available."), ephemeral: true);
                return;
            }
            SearchResults results = await provider.SearchAsync(query);
            if (!results.HasResults)
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Results", $"No results found for '{query}' in {provider.DisplayName}."), ephemeral: true);
                return;
            }
            await DisplaySearchResults(query, results, provider);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in search command: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Search Error", "An error occurred while searching. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Displays search results from any music provider using select menus.
    /// Builds menus dynamically based on what result types the provider returned.</summary>
    private async Task DisplaySearchResults(string query, SearchResults results, IMusicProvider provider)
    {
        Logs.Debug($"Found {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks from {provider.DisplayName}");
        string sourceId = provider.Id;
        ComponentBuilder builder = new();

        // Add artist select menu if we have artists
        if (results.Artists.Count > 0)
        {
            SelectMenuBuilder artistMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an artist")
                .WithCustomId($"search:{sourceId}:artist")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Artist artist in results.Artists.Take(25))
            {
                artistMenu.AddOption(
                    artist.Name,
                    artist.SourceKey,
                    TruncateDescription(artist.Summary));
            }
            builder.WithSelectMenu(artistMenu);
        }

        // Add album select menu if we have albums
        if (results.Albums.Count > 0 && builder.ActionRows.Count < 5)
        {
            SelectMenuBuilder albumMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an album")
                .WithCustomId($"search:{sourceId}:album")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Album album in results.Albums.Take(25))
            {
                albumMenu.AddOption(
                    album.Title,
                    album.SourceKey,
                    TruncateDescription($"Album by {album.Artist}"));
            }
            builder.WithSelectMenu(albumMenu);
        }

        // Add track select menu if we have tracks
        if (results.Tracks.Count > 0 && builder.ActionRows.Count < 5)
        {
            SelectMenuBuilder trackMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select a track")
                .WithCustomId($"search:{sourceId}:track")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Track track in results.Tracks.Take(25))
            {
                string title = track.Title.Length > 80 ? track.Title[..77] + "..." : track.Title;
                string description = !string.IsNullOrEmpty(track.DurationDisplay)
                    ? $"{track.Artist} | {track.DurationDisplay}"
                    : $"Track by {track.Artist}";
                trackMenu.AddOption(title, track.SourceKey, TruncateDescription(description));
            }
            builder.WithSelectMenu(trackMenu);
        }

        // Build summary
        List<string> summaryParts = [];
        if (results.Artists.Count > 0) summaryParts.Add($"{results.Artists.Count} artists");
        if (results.Albums.Count > 0) summaryParts.Add($"{results.Albums.Count} albums");
        if (results.Tracks.Count > 0) summaryParts.Add($"{results.Tracks.Count} tracks");
        string summary = $"Found {string.Join(", ", summaryParts)} on {provider.DisplayName}";

        await FollowupAsync(components: ComponentV2Builder.BuildSearchResults(query, summary, builder), ephemeral: true);
    }

    /// <summary>Plays music from a Plex playlist.
    /// Allows users to quickly play entire playlists with optional shuffling.</summary>
    /// <param name="playlist">The playlist to play</param>
    /// <param name="shuffle">Whether to shuffle the playlist</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [SlashCommand("playlist", "Play a Plex playlist")]
    public async Task PlaylistCommand([Summary("playlist", "The playlist to play")]
    [Autocomplete(typeof(PlaylistAutocompleteHandler))]
    string playlist, [Summary("shuffle", "Shuffle the playlist")] bool shuffle = true)
    {
        await RespondAsync(components: ComponentV2Builder.Info("Loading", "Loading playlist..."), ephemeral: true); // Respond to acknowledge the command
        IUserMessage ackMessage = await GetOriginalResponseAsync();
        try
        {
            Logs.Debug($"Loading playlist: {playlist}, shuffle: {shuffle}");
            if (string.IsNullOrWhiteSpace(playlist))
            {
                await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Error("Invalid Playlist", "Please select a playlist."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                return;
            }
            Playlist playlistDetails = await plexMusicService.GetPlaylistDetailsAsync(playlist);
            if (playlistDetails.Tracks.Count == 0)
            {
                await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Info("Empty Playlist", $"Playlist '{playlistDetails.Title}' is empty."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                return;
            }
            // Get a list of tracks from the playlist to add to the queue
            List<Track> tracks = playlistDetails.Tracks;
            if (shuffle)
            {
                Random rng = new();
                tracks = [.. tracks.OrderBy(x => rng.Next())];
            }
            await playerService.AddToQueueAsync(Context.Interaction, tracks);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in playlist command: {ex.Message}");
            await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Error("Playlist Error", "An error occurred while loading the playlist. Please try again later."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
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
        await DeferAsync(ephemeral: true);
        try
        {
            Logs.Info($"Play command: {query}");
            if (string.IsNullOrWhiteSpace(query))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Invalid Query", "Please enter a URL or search term."), ephemeral: true);
                return;
            }
            // Check if the query is a URL — route through Lavalink directly
            if (Uri.TryCreate(query, UriKind.Absolute, out Uri? parsedUri) &&
                (parsedUri.Scheme == "http" || parsedUri.Scheme == "https"))
            {
                await HandleUrlPlaybackAsync(query, parsedUri);
                return;
            }
            // Not a URL — search Plex library
            SearchResults results = await plexMusicService.SearchLibraryAsync(query);
            if (!results.HasResults)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Results", $"No results found for '{query}'."), ephemeral: true);
                return;
            }
            // If we found tracks, play the first one
            if (results.Tracks.Count != 0)
            {
                Track track = results.Tracks.First();
                await playerService.AddToQueueAsync(Context.Interaction, [track]);
                return;
            }
            // If we found albums but no tracks, play the first album
            if (results.Albums.Count != 0)
            {
                Album album = results.Albums.First();
                List<Track> tracks = await plexMusicService.GetTracksAsync(album.SourceKey);
                if (tracks.Count != 0)
                {
                    await playerService.AddToQueueAsync(Context.Interaction, tracks);
                    return;
                }
            }
            // If we found artists but no tracks or albums, play the first artist
            if (results.Artists.Count != 0)
            {
                Artist artist = results.Artists.First();
                List<Track> allTracks = await plexMusicService.GetAllArtistTracksAsync(artist.SourceKey);
                if (allTracks.Count != 0)
                {
                    await playerService.AddToQueueAsync(Context.Interaction, allTracks);
                    return;
                }
            }
            // If we get here, we found results but couldn't play anything
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "Found results, but couldn't play any tracks."), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in play command: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "An error occurred while playing. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Handles playback of a direct URL by routing through registered providers first,
    /// then falling back to generic Lavalink loading for unclaimed URLs.</summary>
    private async Task HandleUrlPlaybackAsync(string url, Uri parsedUri)
    {
        try
        {
            // Check if any registered provider claims this URL
            foreach (IMusicProvider provider in providerRegistry.GetAvailableProviders())
            {
                if (provider.CanHandleUrl(parsedUri))
                {
                    Logs.Debug($"Provider '{provider.DisplayName}' claims URL: {url}");
                    List<Track> tracks = await provider.ResolveUrlAsync(url);
                    if (tracks.Count > 0)
                    {
                        await playerService.AddToQueueAsync(Context.Interaction, tracks);
                        return;
                    }
                }
            }

            // No provider claimed it — generic Lavalink fallback
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            TrackLoadResult loadResult = await audioService.Tracks.LoadTracksAsync(
                url, TrackSearchMode.None, cancellationToken: cts.Token);

            LavalinkTrack? lavalinkTrack = loadResult.Track;
            if (lavalinkTrack == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Not Found",
                    "Could not load a playable track from this URL."), ephemeral: true);
                return;
            }

            Track track = Track.CreateFromUrl(
                lavalinkTrack.Title ?? "Unknown Title",
                lavalinkTrack.Author ?? "Unknown Artist",
                url,
                lavalinkTrack.ArtworkUri?.ToString() ?? "",
                "external");
            track.DurationMs = (long)lavalinkTrack.Duration.TotalMilliseconds;
            track.DurationDisplay = FormatHelper.FormatDuration(lavalinkTrack.Duration);

            await playerService.AddToQueueAsync(Context.Interaction, [track]);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error playing URL: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error",
                "An error occurred while loading this URL. Please try again."), ephemeral: true);
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
            await RespondAsync(components: ComponentV2Builder.BuildHelp());
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in help command: {ex.Message}");
            await RespondAsync(components: ComponentV2Builder.Error("Help Error", "An error occurred while generating help information."), ephemeral: true);
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