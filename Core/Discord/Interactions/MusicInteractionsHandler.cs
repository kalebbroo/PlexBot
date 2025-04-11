using System.Collections.Concurrent;
using Lavalink4NET;
using PlexBot.Core.Models.Media;
using PlexBot.Services;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>Handles interactive components for the music player.
/// This module processes button clicks, select menu choices, and other
/// interactive elements of the music player interface.</summary>
/// <remarks>Initializes a new instance of the <see cref="MusicInteractionHandler"/> class.
/// Sets up the interaction handler with necessary services.</remarks>
/// <param name="plexMusicService">Service for interacting with Plex music</param>
/// <param name="playerService">Service for managing audio playback</param>
public class MusicInteractionHandler(IPlexMusicService plexMusicService, IPlayerService playerService, 
    IAudioService audioService) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IPlexMusicService _plexMusicService = plexMusicService ?? throw new ArgumentNullException(nameof(plexMusicService));
    private readonly IPlayerService _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
    private readonly IAudioService _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

    // Cooldown tracking to prevent spamming
    private static readonly ConcurrentDictionary<(ulong UserId, string CommandId), DateTime> _lastInteracted = new();
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(2);

    /// <summary>Handles search result selection from search command menu.
    /// Processes the user's selection from search results and plays the selected content.</summary>
    /// <param name="type">The type of content selected (artist, album, track)</param>
    /// <param name="values">The selected content IDs</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("search:*")]
    public async Task HandleSearchSelectionAsync(string type, string[] values)
    {
        await DeferAsync(ephemeral: true);
        if (IsOnCooldown(Context.User.Id, $"search:{type}"))
        {
            await FollowupAsync("Please wait a moment before selecting another item.", ephemeral: true);
            return;
        }
        try
        {
            if (values.Length == 0)
            {
                await FollowupAsync("No selection made.", ephemeral: true);
                return;
            }
            string selectedKey = values[0];
            Logs.Debug($"Search selection: type={type}, key={selectedKey}");
            switch (type.ToLowerInvariant())
            {
                case "track":
                    await HandleTrackSelectionAsync(selectedKey);
                    break;
                case "album":
                    await HandleAlbumSelectionAsync(selectedKey);
                    break;
                case "artist":
                    await HandleArtistSelectionAsync(selectedKey);
                    break;
                case "youtube":
                    await HandleYouTubeSelectionAsync(selectedKey);
                    break;
                default:
                    await FollowupAsync($"Unrecognized selection type: {type}", ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling search selection: {ex.Message}");
            await FollowupAsync("An error occurred while processing your selection. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles the pause/resume button click.
    /// Toggles playback state between playing and paused.</summary>
    /// <param name="action">The specific action (pause/resume)</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("pause_resume:*")]
    public async Task HandlePauseResumeAsync(string action)
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, "pause_resume"))
        {
            await FollowupAsync("Please wait a moment before clicking again.", ephemeral: true);
            return;
        }
        try
        {
            string result = await _playerService.TogglePauseResumeAsync(Context.Interaction);
            Logs.Info($"Playback {result.ToLowerInvariant()} by {Context.User.Username}");

            // The player UI is automatically updated by the player service
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling pause/resume: {ex.Message}");
            await FollowupAsync("An error occurred while toggling playback. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles the skip button click.
    /// Skips the current track and plays the next one in the queue.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("skip:*")]
    public async Task HandleSkipAsync()
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, "skip"))
        {
            await FollowupAsync("Please wait a moment before clicking again.", ephemeral: true);
            return;
        }
        try
        {
            await _playerService.SkipTrackAsync(Context.Interaction);
            Logs.Info($"Track skipped by {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling skip: {ex.Message}");
            await FollowupAsync("An error occurred while skipping the track. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles queue option button clicks.
    /// Displays and manages queue options like viewing, clearing, or shuffling.</summary>
    /// <param name="action">The specific queue action (options, view, etc.)</param>
    /// <param name="pageStr">The current page number (for pagination)</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("queue_options:*:*")]
    public async Task HandleQueueOptionsAsync(string action, string pageStr)
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, $"queue_options:{action}"))
        {
            await FollowupAsync("Please wait a moment before clicking again.", ephemeral: true);
            return;
        }
        try
        {
            // Get the player
            if (await _playerService.GetPlayerAsync(Context.Interaction, false) is not CustomPlayer player)
            {
                await FollowupAsync("No active player found.", ephemeral: true);
                return;
            }
            int currentPage = int.TryParse(pageStr, out int page) ? page : 1;
            switch (action.ToLowerInvariant())
            {
                case "options":
                    // Show queue option buttons
                    ComponentBuilder optionsComponents = new ComponentBuilder()
                        .WithButton("View Queue", $"queue_options:view:{currentPage}", ButtonStyle.Success)
                        .WithButton("Shuffle", $"queue_options:shuffle:{currentPage}", ButtonStyle.Primary)
                        .WithButton("Clear", $"queue_options:clear:{currentPage}", ButtonStyle.Danger)
                        .WithButton("Back", $"queue_options:back:{currentPage}", ButtonStyle.Secondary);
                    await player.UpdatePlayerComponentsAsync(optionsComponents);
                    break;
                case "view":
                    // Show the queue
                    await ShowQueueAsync(player, currentPage);
                    break;
                case "shuffle":
                    // Shuffle the queue
                    await player.Queue.ShuffleAsync();
                    await FollowupAsync("Queue shuffled.", ephemeral: true);
                    break;
                case "clear":
                    // Clear the queue
                    await player.Queue.ClearAsync();
                    await FollowupAsync("Queue cleared.", ephemeral: true);
                    break;
                case "back":
                    // Restore default player buttons
                    ComponentBuilder defaultComponents = new ComponentBuilder()
                        .WithButton(player.State == PlayerState.Playing ? "Pause" : "Resume",
                                    player.State == PlayerState.Playing ? "pause_resume:pause" : "pause_resume:resume",
                                    player.State == PlayerState.Playing ? ButtonStyle.Secondary : ButtonStyle.Success)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                        .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);
                    await player.UpdatePlayerComponentsAsync(defaultComponents);
                    break;
                default:
                    await FollowupAsync($"Unrecognized queue action: {action}", ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling queue options: {ex.Message}");
            await FollowupAsync("An error occurred while managing the queue. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles the repeat mode button click.
    /// Displays a select menu for choosing repeat mode.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("repeat:*")]
    public async Task HandleRepeatAsync()
    {
        await DeferAsync(ephemeral: true);
        if (IsOnCooldown(Context.User.Id, "repeat"))
        {
            await FollowupAsync("Please wait a moment before clicking again.", ephemeral: true);
            return;
        }
        try
        {
            // Create a select menu for repeat options
            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithCustomId("repeat_select")
                .WithPlaceholder("Select repeat mode")
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption("No Repeat", "none", "Play each track once")
                .AddOption("Repeat Track", "track", "Repeat the current track")
                .AddOption("Repeat Queue", "queue", "Repeat the entire queue");
            ComponentBuilder components = new ComponentBuilder()
                .WithSelectMenu(selectMenu);
            await FollowupAsync("Select a repeat mode:", components: components.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling repeat button: {ex.Message}");
            await FollowupAsync("An error occurred while showing repeat options. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles repeat mode selection from the select menu.
    /// Sets the player's repeat mode based on user selection.</summary>
    /// <param name="values">The selected repeat mode</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("repeat_select")]
    public async Task HandleRepeatSelectAsync(string[] values)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            if (values.Length == 0)
            {
                await FollowupAsync("No selection made.", ephemeral: true);
                return;
            }
            string selectedMode = values[0];
            // Convert to TrackRepeatMode
            TrackRepeatMode repeatMode = selectedMode.ToLowerInvariant() switch
            {
                "track" => TrackRepeatMode.Track,
                "queue" => TrackRepeatMode.Queue,
                _ => TrackRepeatMode.None
            };
            await _playerService.SetRepeatModeAsync(Context.Interaction, repeatMode);
            string modeDescription = repeatMode switch
            {
                TrackRepeatMode.None => "No repeat",
                TrackRepeatMode.Track => "Repeating current track",
                TrackRepeatMode.Queue => "Repeating the entire queue",
                _ => "Unknown repeat mode"
            };
            await FollowupAsync($"Repeat mode set to: {modeDescription}", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling repeat selection: {ex.Message}");
            await FollowupAsync("An error occurred while setting repeat mode. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles the kill button click.
    /// Stops playback and disconnects the bot from voice.</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [ComponentInteraction("kill:*")]
    public async Task HandleKillAsync()
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, "kill"))
        {
            await FollowupAsync("Please wait a moment before clicking again.", ephemeral: true);
            return;
        }
        try
        {
            await _playerService.StopAsync(Context.Interaction, true);
            Logs.Info($"Player killed by {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling kill: {ex.Message}");
            await FollowupAsync("An error occurred while stopping playback. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Handles track selection from search results.
    /// Plays a single selected track.</summary>
    /// <param name="trackKey">The track key to play</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleTrackSelectionAsync(string trackKey)
    {
        try
        {
            // Get track details
            Track? track = await _plexMusicService.GetTrackDetailsAsync(trackKey);
            if (track == null)
            {
                await FollowupAsync("Failed to retrieve track details.", ephemeral: true);
                return;
            }
            // Play the track
            await _playerService.PlayTrackAsync(Context.Interaction, track);
            await FollowupAsync($"Playing '{track.Title}' by {track.Artist}", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling track selection: {ex.Message}");
            throw; // Rethrow for the caller to handle
        }
    }

    /// <summary>Handles album selection from search results.
    /// Plays all tracks from the selected album.</summary>
    /// <param name="albumKey">The album key to play</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleAlbumSelectionAsync(string albumKey)
    {
        try
        {
            // Get tracks from the album
            List<Track> tracks = await _plexMusicService.GetTracksAsync(albumKey);
            if (tracks.Count == 0)
            {
                await FollowupAsync("The selected album has no tracks.", ephemeral: true);
                return;
            }
            // Add tracks to queue
            await _playerService.AddToQueueAsync(Context.Interaction, tracks);
            await FollowupAsync($"Playing {tracks.Count} tracks from '{tracks[0].Album}' by {tracks[0].Artist}", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling album selection: {ex.Message}");
            throw; // Rethrow for the caller to handle
        }
    }

    /// <summary>Handles artist selection from search results.
    /// Plays all tracks from all albums by the selected artist.</summary>
    /// <param name="artistKey">The artist key to play</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleArtistSelectionAsync(string artistKey)
    {
        try
        {
            // Get albums by the artist
            List<Album> albums = await _plexMusicService.GetAlbumsAsync(artistKey);
            if (albums.Count == 0)
            {
                await FollowupAsync("The selected artist has no albums.", ephemeral: true);
                return;
            }
            // Get all tracks from all albums
            List<Track> allTracks = [];
            foreach (Album album in albums)
            {
                List<Track> tracks = await _plexMusicService.GetTracksAsync(album.SourceKey);
                allTracks.AddRange(tracks);
            }
            if (allTracks.Count == 0)
            {
                await FollowupAsync("The selected artist has no tracks.", ephemeral: true);
                return;
            }
            // Add all tracks to queue
            await _playerService.AddToQueueAsync(Context.Interaction, allTracks);
            await FollowupAsync($"Playing {allTracks.Count} tracks by {allTracks[0].Artist}", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling artist selection: {ex.Message}");
            throw; // Rethrow for the caller to handle
        }
    }

    /// <summary>Handles YouTube track selection from search results.
    /// Plays a YouTube track selected from search results.</summary>
    /// <param name="trackUrl">The URL of the YouTube track to play</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleYouTubeSelectionAsync(string videoUrl)
    {
        try
        {
            Logs.Debug($"Handling YouTube selection for URL: {videoUrl}");

            // Get a player
            QueuedLavalinkPlayer? player = await _playerService.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync("Failed to initialize player for YouTube playback.", ephemeral: true);
                return;
            }

            // Create the proper load options
            TrackLoadOptions loadOptions = new()
            {
                SearchMode = TrackSearchMode.None // Use None since we have a direct URL
            };

            // Load the track just to test if it's playable
            LavalinkTrack? lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                videoUrl,
                loadOptions,
                cancellationToken: CancellationToken.None);

            if (lavalinkTrack == null)
            {
                await FollowupAsync("This YouTube video cannot be played. It may be age-restricted or requires login.", ephemeral: true);
                return;
            }

            // Create our custom track
            Track track = Track.CreateFromUrl(
                lavalinkTrack.Title ?? "YouTube Track",
                lavalinkTrack.Author ?? "YouTube",
                videoUrl,
                lavalinkTrack.ArtworkUri?.ToString() ?? "",
                "youtube"
            );

            // Use your existing player service
            await _playerService.AddToQueueAsync(Context.Interaction, [track]);

            await FollowupAsync($"Added '{lavalinkTrack.Title}' to the queue", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling YouTube selection: {ex.Message}");

            // Check for specific error message
            if (ex.Message.Contains("login") || ex.Message.Contains("This video requires login"))
            {
                await FollowupAsync("This YouTube video cannot be played as it requires login or is age-restricted.", ephemeral: true);
            }
            else
            {
                await FollowupAsync($"Error playing YouTube track: {ex.Message}", ephemeral: true);
            }
        }
    }

    /// <summary>Shows the current queue as an embed.
    /// Displays the currently playing track and upcoming tracks in the queue.</summary>
    /// <param name="player">The player to show the queue for</param>
    /// <param name="page">The page number to show</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task ShowQueueAsync(CustomPlayer player, int page)
    {
        try
        {
            // Get the current track and queue
            CustomTrackQueueItem currentTrack = player.CurrentItem as CustomTrackQueueItem;
            List<CustomTrackQueueItem> queue = player.Queue.Select(item => item as CustomTrackQueueItem).ToList();
            // Build the embed
            const int itemsPerPage = 10;
            EmbedBuilder embed = PlayerEmbedBuilder.BuildQueueEmbed(queue, currentTrack, page, itemsPerPage);
            // Calculate pagination info
            int totalTracks = queue.Count;
            int totalPages = (totalTracks + itemsPerPage - 1) / itemsPerPage;
            // Build pagination buttons
            ComponentBuilder components = new();
            if (totalPages > 1)
            {
                components.WithButton("Previous", $"queue_options:view:{Math.Max(1, page - 1)}",
                                   ButtonStyle.Secondary, disabled: page <= 1);
                components.WithButton("Next", $"queue_options:view:{Math.Min(totalPages, page + 1)}",
                                   ButtonStyle.Secondary, disabled: page >= totalPages);
            }
            components.WithButton("Back", "queue_options:back:1", ButtonStyle.Secondary);
            await FollowupAsync(embed: embed.Build(), components: components.Build(), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error showing queue: {ex.Message}");
            await FollowupAsync("An error occurred while showing the queue. Please try again later.", ephemeral: true);
        }
    }

    /// <summary>Checks if a user is on cooldown for a specific command.
    /// Prevents command spam by enforcing a cooldown period.</summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="commandId">The command ID to check</param>
    /// <returns>True if the user is on cooldown; otherwise, false</returns>
    private static bool IsOnCooldown(ulong userId, string commandId)
    {
        var key = (userId, commandId);
        if (_lastInteracted.TryGetValue(key, out DateTime lastTime))
        {
            if (DateTime.UtcNow - lastTime < _cooldownPeriod)
            {
                return true;
            }
        }
        _lastInteracted[key] = DateTime.UtcNow;
        return false;
    }
}