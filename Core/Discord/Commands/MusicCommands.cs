using Discord.Net;
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Discord.Autocomplete;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Services.PlexApi;
using PlexBot.Utils;

using PlexBot.Core.Models.Players;
using PlexBot.Core.Services;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Core.Services.Music;

namespace PlexBot.Core.Discord.Commands;

/// <summary>Provides discord slash commands for music playback with interactive UI components to control playback and manage the music queue</summary>
public class MusicCommands(IPlexMusicService plexMusicService, IPlayerService playerService,
    IAudioService audioService, MusicProviderRegistry providerRegistry, IPlexSonicService plexSonicService)
    : InteractionModuleBase<SocketInteractionContext>
{

    /// <summary>Unified entry point for all music discovery — routes to Plex library, sonic features,
    /// or extension providers based on a single mode selector</summary>
    [SlashCommand("search", "Search for music across all sources")]
    public async Task SearchCommand(
        [Summary("mode", "Where and how to search")]
        [Autocomplete(typeof(SearchModeAutocompleteHandler))]
        string mode,
        [Summary("query", "What to search for")]
        [Autocomplete(typeof(SearchQueryAutocompleteHandler))]
        string query)
    {
        try
        {
            await DeferAsync(ephemeral: true);
            string normalizedMode = mode.ToLowerInvariant();
            Logs.Debug($"Search: query='{query}', mode={normalizedMode}");

            // Ignore hint placeholder values from autocomplete
            if (query.StartsWith("hint_", StringComparison.Ordinal))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Invalid Query", "Please type your search — don't select the hint text."), ephemeral: true);
                return;
            }

            if (normalizedMode is "mood" or "genre" or "radio")
            {
                await HandleSonicModeAsync(query, normalizedMode);
                return;
            }

            string providerId = normalizedMode == "library" ? "plex" : normalizedMode;
            IMusicProvider? provider = providerRegistry.GetProvider(providerId);
            if (provider is null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Unknown Source", $"Music source '{providerId}' is not available."), ephemeral: true);
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
        catch (HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.UnknownInteraction)
        {
            Logs.Warning($"Search command hit 10062 (Unknown interaction) — Discord-side timing issue");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in search command: {ex}");
            try { await FollowupAsync(components: ComponentV2Builder.Error("Search Error", "An error occurred while searching. Please try again later."), ephemeral: true); }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Dispatches non-library search modes to the appropriate sonic handler based on mode string</summary>
    public async Task HandleSonicModeAsync(string query, string mode)
    {
        switch (mode)
        {
            case "mood":
                await HandleMoodSearchAsync(query);
                break;
            case "genre":
                await HandleGenreSearchAsync(query);
                break;
            case "radio":
                await HandleRadioSearchAsync(query);
                break;
            default:
                await FollowupAsync(components: ComponentV2Builder.Error("Unknown Mode", $"Search mode '{mode}' is not recognized."), ephemeral: true);
                break;
        }
    }

    /// <summary>Matches query against Plex mood tags by ID (from autocomplete) or name (free-text),
    /// then displays tracks filtered by the matched mood</summary>
    public async Task HandleMoodSearchAsync(string query)
    {
        List<MoodTag> moods = await plexSonicService.GetAvailableMoodsAsync();
        if (moods.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Info("No Moods", "No mood tags found. Sonic analysis may not be enabled on your Plex server."), ephemeral: true);
            return;
        }

        // Match by ID first (autocomplete sends the numeric ID), then by name
        MoodTag? matched = moods.FirstOrDefault(m => m.Id == query)
            ?? moods.FirstOrDefault(m => m.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? moods.FirstOrDefault(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            string suggestions = string.Join(", ", moods.Take(20).Select(m => m.Name));
            await FollowupAsync(components: ComponentV2Builder.Error("Mood Not Found", $"No mood matching '{query}'. Try: {suggestions}"), ephemeral: true);
            return;
        }

        List<Track> tracks = await plexSonicService.GetMoodTracksAsync(matched.Id);
        if (tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Info("No Tracks", $"No tracks found for mood '{matched.Name}'."), ephemeral: true);
            return;
        }

        await DisplaySonicResults($"Mood: {matched.Name}", $"Found {tracks.Count} tracks matching this mood", tracks);
    }

    /// <summary>Matches query against Plex genre tags by ID (from autocomplete) or name (free-text),
    /// then displays tracks filtered by the matched genre</summary>
    public async Task HandleGenreSearchAsync(string query)
    {
        List<GenreTag> genres = await plexSonicService.GetAvailableGenresAsync();
        if (genres.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Info("No Genres", "No genre tags found in your library."), ephemeral: true);
            return;
        }

        // Match by ID first (autocomplete sends the numeric ID), then by name
        GenreTag? matched = genres.FirstOrDefault(g => g.Id == query)
            ?? genres.FirstOrDefault(g => g.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? genres.FirstOrDefault(g => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (matched is null)
        {
            string suggestions = string.Join(", ", genres.Take(20).Select(g => g.Name));
            await FollowupAsync(components: ComponentV2Builder.Error("Genre Not Found", $"No genre matching '{query}'. Try: {suggestions}"), ephemeral: true);
            return;
        }

        List<Track> tracks = await plexSonicService.GetGenreTracksAsync(matched.Id);
        if (tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Info("No Tracks", $"No tracks found for genre '{matched.Name}'."), ephemeral: true);
            return;
        }

        await DisplaySonicResults($"Genre: {matched.Name}", $"Found {tracks.Count} tracks in this genre", tracks);
    }

    /// <summary>Seeds a radio station from a search result track, plays a station selected from autocomplete,
    /// or lists available stations when query is empty</summary>
    public async Task HandleRadioSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            List<RadioStation> stations = await plexSonicService.GetRadioStationsAsync();
            if (stations.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Stations", "No radio stations found. Enter a track name to start radio from a specific track."), ephemeral: true);
                return;
            }
            await DisplayRadioStations(stations);
            return;
        }

        // Check if query is a station key (from autocomplete, e.g. "/library/sections/6/stations/1")
        if (query.StartsWith("/library/sections/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleStationPlayAsync(query);
            return;
        }

        // Otherwise treat as a track search to seed radio
        SearchResults searchResults = await plexMusicService.SearchLibraryAsync(query);
        if (searchResults.Tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("No Results", $"No tracks found for '{query}'. Try a different search term."), ephemeral: true);
            return;
        }

        Track seedTrack = searchResults.Tracks.First();
        string ratingKey = PlexJsonParser.ExtractRatingKey(seedTrack.SourceKey);
        if (string.IsNullOrEmpty(ratingKey))
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Error", "Could not extract track identifier."), ephemeral: true);
            return;
        }

        List<Track> radioTracks = await plexSonicService.GetRadioTracksAsync(ratingKey);
        if (radioTracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Info("Radio Unavailable", $"Could not generate radio from '{seedTrack.Title}'. Sonic analysis may not be enabled."), ephemeral: true);
            return;
        }

        await DisplaySonicResults(
            $"Radio from: {seedTrack.Title}",
            $"Generated {radioTracks.Count} radio tracks seeded from **{seedTrack.Artist}** - {seedTrack.Title}",
            radioTracks);
    }

    /// <summary>Fetches tracks from a Plex radio station key and queues them for playback</summary>
    public async Task HandleStationPlayAsync(string stationKey)
    {
        try
        {
            List<RadioStation> stations = await plexSonicService.GetRadioStationsAsync();
            RadioStation? station = stations.FirstOrDefault(s => s.SourceKey == stationKey);
            string stationName = station?.Title ?? "Radio Station";

            // Fetch tracks from the station endpoint
            string response = await plexSonicService.GetStationTracksAsync(stationKey);
            if (string.IsNullOrEmpty(response))
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Tracks", $"Could not load tracks from '{stationName}'."), ephemeral: true);
                return;
            }

            List<Track> tracks = plexSonicService.ParseTracksFromResponse(response);
            if (tracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Tracks", $"No tracks available from '{stationName}'."), ephemeral: true);
                return;
            }

            await DisplaySonicResults(
                $"Station: {stationName}",
                $"Loaded {tracks.Count} tracks from {stationName}",
                tracks);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error loading station: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Station Error", "Failed to load radio station tracks."), ephemeral: true);
        }
    }

    /// <summary>Builds a track select menu with optional "Play All" button, reusing the search:plex:track
    /// interaction pattern so tracks route through the existing selection handler</summary>
    public async Task DisplaySonicResults(string title, string description, List<Track> tracks)
    {
        ComponentBuilder builder = new();
        SelectMenuBuilder trackMenu = new SelectMenuBuilder()
            .WithPlaceholder("Select a track to play")
            .WithCustomId("search:plex:track")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (Track track in tracks.Take(25))
        {
            string trackTitle = string.IsNullOrEmpty(track.Title) ? "Unknown Track" : track.Title;
            trackTitle = trackTitle.Length > 80 ? trackTitle[..77] + "..." : trackTitle;
            string key = string.IsNullOrEmpty(track.SourceKey) ? $"unknown_{Guid.NewGuid()}" : track.SourceKey;
            string trackArtist = track.Artist ?? "Unknown Artist";
            string desc = !string.IsNullOrEmpty(track.DurationDisplay)
                ? $"{trackArtist} | {track.DurationDisplay}"
                : $"Track by {trackArtist}";
            trackMenu.AddOption(trackTitle, key, TruncateDescription(desc));
        }
        builder.WithSelectMenu(trackMenu);

        if (tracks.Count > 1)
        {
            string firstTrackKey = PlexJsonParser.ExtractRatingKey(tracks.First().SourceKey);
            builder.WithButton("Play All", $"sonic:playall:{title.Replace(":", "-")}:{firstTrackKey}", ButtonStyle.Success);
        }

        await FollowupAsync(components: ComponentV2Builder.BuildSonicResults(title, description, builder), ephemeral: true);
    }

    /// <summary>Builds a station select menu using the search:plex:radio_station interaction pattern
    /// so selection routes through the existing search handler's radio_station case</summary>
    public async Task DisplayRadioStations(List<RadioStation> stations)
    {
        ComponentBuilder builder = new();
        SelectMenuBuilder stationMenu = new SelectMenuBuilder()
            .WithPlaceholder("Select a radio station")
            .WithCustomId("search:plex:radio_station")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (RadioStation station in stations.Take(25))
        {
            string title = string.IsNullOrEmpty(station.Title) ? "Unknown Station" : station.Title;
            title = title.Length > 80 ? title[..77] + "..." : title;
            string desc = !string.IsNullOrEmpty(station.Description)
                ? TruncateDescription(station.Description)
                : $"{station.Type} station";
            stationMenu.AddOption(title, station.SourceKey, desc);
        }
        builder.WithSelectMenu(stationMenu);

        await FollowupAsync(components: ComponentV2Builder.BuildSonicResults(
            "Radio Stations", $"Found {stations.Count} stations", builder), ephemeral: true);
    }

    /// <summary>Builds up to three select menus (artists, albums, tracks) dynamically based on which
    /// result types the provider returned, each routing through the search:*:* interaction handler</summary>
    public async Task DisplaySearchResults(string query, SearchResults results, IMusicProvider provider)
    {
        Logs.Debug($"Found {results.Artists.Count} artists, {results.Albums.Count} albums, {results.Tracks.Count} tracks from {provider.DisplayName}");
        string sourceId = provider.Id;
        ComponentBuilder builder = new();

        if (results.Artists.Count > 0)
        {
            SelectMenuBuilder artistMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an artist")
                .WithCustomId($"search:{sourceId}:artist")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Artist artist in results.Artists.Take(25))
            {
                string name = string.IsNullOrEmpty(artist.Name) ? "Unknown Artist" : artist.Name;
                string key = string.IsNullOrEmpty(artist.SourceKey) ? $"unknown_{Guid.NewGuid()}" : artist.SourceKey;
                artistMenu.AddOption(name, key, TruncateDescription(artist.Summary));
            }
            builder.WithSelectMenu(artistMenu);
        }

        if (results.Albums.Count > 0 && (builder.ActionRows?.Count ?? 0) < 5)
        {
            SelectMenuBuilder albumMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select an album")
                .WithCustomId($"search:{sourceId}:album")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Album album in results.Albums.Take(25))
            {
                string title = string.IsNullOrEmpty(album.Title) ? "Unknown Album" : album.Title;
                string key = string.IsNullOrEmpty(album.SourceKey) ? $"unknown_{Guid.NewGuid()}" : album.SourceKey;
                string artist = album.Artist ?? "Unknown Artist";
                albumMenu.AddOption(
                    title.Length > 100 ? title[..97] + "..." : title,
                    key,
                    TruncateDescription($"Album by {artist}"));
            }
            builder.WithSelectMenu(albumMenu);
        }

        if (results.Tracks.Count > 0 && (builder.ActionRows?.Count ?? 0) < 5)
        {
            SelectMenuBuilder trackMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select a track")
                .WithCustomId($"search:{sourceId}:track")
                .WithMinValues(1)
                .WithMaxValues(1);
            foreach (Track track in results.Tracks.Take(25))
            {
                string title = string.IsNullOrEmpty(track.Title) ? "Unknown Track" : track.Title;
                title = title.Length > 80 ? title[..77] + "..." : title;
                string key = string.IsNullOrEmpty(track.SourceKey) ? $"unknown_{Guid.NewGuid()}" : track.SourceKey;
                string trackArtist = track.Artist ?? "Unknown Artist";
                string description = !string.IsNullOrEmpty(track.DurationDisplay)
                    ? $"{trackArtist} | {track.DurationDisplay}"
                    : $"Track by {trackArtist}";
                trackMenu.AddOption(title, key, TruncateDescription(description));
            }
            builder.WithSelectMenu(trackMenu);
        }

        List<string> summaryParts = [];
        if (results.Artists.Count > 0) summaryParts.Add($"{results.Artists.Count} artists");
        if (results.Albums.Count > 0) summaryParts.Add($"{results.Albums.Count} albums");
        if (results.Tracks.Count > 0) summaryParts.Add($"{results.Tracks.Count} tracks");
        string summary = $"Found {string.Join(", ", summaryParts)} on {provider.DisplayName}";

        await FollowupAsync(components: ComponentV2Builder.BuildSearchResults(query, summary, builder), ephemeral: true);
    }

    /// <summary>Loads a Plex or custom playlist, optionally shuffles it, and queues all tracks for playback</summary>
    [SlashCommand("playlist", "Play a playlist")]
    public async Task PlaylistCommand([Summary("playlist", "The playlist to play")]
    [Autocomplete(typeof(PlaylistAutocompleteHandler))]
    string playlist, [Summary("shuffle", "Shuffle the playlist")] bool shuffle = true)
    {
        await RespondAsync(components: ComponentV2Builder.Info("Loading", "Loading playlist..."), ephemeral: true);
        IUserMessage ackMessage = await GetOriginalResponseAsync();
        try
        {
            Logs.Debug($"Loading playlist: {playlist}, shuffle: {shuffle}");
            if (string.IsNullOrWhiteSpace(playlist))
            {
                await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Error("Invalid Playlist", "Please select a playlist."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                return;
            }

            Playlist? playlistDetails = null;

            if (playlist.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            {
                IMusicProvider? customProvider = providerRegistry.GetProvider("playlists");
                if (customProvider is not null)
                    playlistDetails = await customProvider.GetPlaylistDetailsAsync(playlist);

                if (playlistDetails is null)
                {
                    await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Error("Not Found", "Custom playlist not found."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                    return;
                }
            }
            else
            {
                playlistDetails = await plexMusicService.GetPlaylistDetailsAsync(playlist);
            }

            if (playlistDetails.Tracks.Count == 0)
            {
                await ackMessage.ModifyAsync(msg => { msg.Components = ComponentV2Builder.Info("Empty Playlist", $"Playlist '{playlistDetails.Title}' is empty."); msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                return;
            }
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

    /// <summary>Attempts URL playback via registered providers first, then Lavalink fallback,
    /// or searches Plex library and auto-plays the best match (track → album → artist)</summary>
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
            if (Uri.TryCreate(query, UriKind.Absolute, out Uri? parsedUri) &&
                (parsedUri.Scheme == "http" || parsedUri.Scheme == "https"))
            {
                await HandleUrlPlaybackAsync(query, parsedUri);
                return;
            }
            SearchResults results = await plexMusicService.SearchLibraryAsync(query);
            if (!results.HasResults)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Results", $"No results found for '{query}'."), ephemeral: true);
                return;
            }
            if (results.Tracks.Count != 0)
            {
                Track track = results.Tracks.First();
                await playerService.AddToQueueAsync(Context.Interaction, [track]);
                return;
            }
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
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "Found results, but couldn't play any tracks."), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in play command: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "An error occurred while playing. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Tries each registered provider's CanHandleUrl first (e.g. YouTube provider claims youtube.com),
    /// then falls back to generic Lavalink loading for unclaimed URLs</summary>
    public async Task HandleUrlPlaybackAsync(string url, Uri parsedUri)
    {
        try
        {
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

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            TrackLoadResult loadResult = await audioService.Tracks.LoadTracksAsync(
                url, TrackSearchMode.None, cancellationToken: cts.Token);

            LavalinkTrack? lavalinkTrack = loadResult.Track;
            if (lavalinkTrack is null)
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

    /// <summary>Quick health check to verify the Discord interaction pipeline is responding</summary>
    [SlashCommand("ping", "Test if interactions are working")]
    public async Task PingCommand()
    {
        Logs.Info($"Ping: about to DeferAsync");
        await DeferAsync(ephemeral: true);
        Logs.Info($"Ping: DeferAsync succeeded");
        await FollowupAsync(components: ComponentV2Builder.Info("Pong", "Interaction pipeline is healthy."), ephemeral: true);
    }

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

    /// <summary>Ensures description fits Discord's 100-char select menu option limit</summary>
    public static string TruncateDescription(string? description, int maxLength = 100)
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
