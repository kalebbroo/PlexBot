using System.Collections.Concurrent;
using Lavalink4NET;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Services;
using PlexBot.Core.Services.LavaLink;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>Handles interactive components for the music player.
/// This module processes button clicks, select menu choices, and other
/// interactive elements of the music player interface.</summary>
/// <remarks>Initializes a new instance of the <see cref="MusicInteractionHandler"/> class.
/// Sets up the interaction handler with necessary services.</remarks>
/// <param name="plexMusicService">Service for interacting with Plex music</param>
/// <param name="playerService">Service for managing audio playback</param>
/// <param name="audioService">Service for managing audio playback</param>
public class MusicInteractionHandler(IPlexMusicService plexMusicService, IPlayerService playerService,
    IAudioService audioService, VisualPlayer visualPlayer, DiscordButtonBuilder buttonBuilder) : InteractionModuleBase<SocketInteractionContext>
{

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
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before selecting another item."), ephemeral: true);
            return;
        }
        try
        {
            if (values.Length == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Selection", "No selection made."), ephemeral: true);
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
                    await FollowupAsync(components: ComponentV2Builder.Error("Unknown Type", $"Unrecognized selection type: {type}"), ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling search selection: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Error", "An error occurred while processing your selection. Please try again later."), ephemeral: true);
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
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            string result = await playerService.TogglePauseResumeAsync(Context.Interaction);
            Logs.Info($"Playback {result.ToLowerInvariant()} by {Context.User.Username}");

            // The player UI is automatically updated by the player service
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling pause/resume: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "An error occurred while toggling playback. Please try again later."), ephemeral: true);
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
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            await playerService.SkipTrackAsync(Context.Interaction);
            Logs.Info($"Track skipped by {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling skip: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Skip Error", "An error occurred while skipping the track. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Handles queue option button clicks via ephemeral messages.
    /// Displays and manages queue options like viewing, clearing, or shuffling.</summary>
    /// <param name="action">The specific queue action (options, view, etc.)</param>
    /// <param name="pageStr">The current page number (for pagination)</param>
    [ComponentInteraction("queue_options:*:*")]
    public async Task HandleQueueOptionsAsync(string action, string pageStr)
    {
        await DeferAsync(ephemeral: true);
        if (IsOnCooldown(Context.User.Id, $"queue_options:{action}"))
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        SocketInteraction interaction = Context.Interaction;
        try
        {
            if (await playerService.GetPlayerAsync(interaction, false) is not CustomLavaLinkPlayer player)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Player", "No active player found."), ephemeral: true);
                return;
            }
            ButtonContext context = new()
            {
                Player = player,
                Interaction = interaction
            };
            int currentPage = int.TryParse(pageStr, out int page) ? page : 1;
            switch (action.ToLowerInvariant())
            {
                case "options":
                    // Send ephemeral queue management panel
                    string nowPlaying = (player.CurrentItem as CustomTrackQueueItem)?.Title ?? "Nothing";
                    string artist = (player.CurrentItem as CustomTrackQueueItem)?.Artist ?? "";
                    string displayNow = string.IsNullOrEmpty(artist) ? nowPlaying : $"{nowPlaying} - {artist}";
                    context.CustomData["currentPage"] = currentPage;
                    ComponentBuilder optionsComponents = buttonBuilder.BuildButtons(ButtonFlag.QueueOptions, context);
                    await FollowupAsync(components: ComponentV2Builder.BuildQueueOptions(
                        displayNow, player.Queue.Count, optionsComponents), ephemeral: true);
                    break;
                case "view":
                    await ShowQueueAsync(player, currentPage);
                    break;
                case "shuffle":
                    int countBefore = player.Queue.Count;
                    await player.Queue.ShuffleAsync();
                    await FollowupAsync(components: ComponentV2Builder.Success(
                        "Queue Shuffled", $"Shuffled {countBefore} tracks."), ephemeral: true);
                    break;
                case "clear":
                    int cleared = player.Queue.Count;
                    await player.Queue.ClearAsync();
                    await FollowupAsync(components: ComponentV2Builder.Success(
                        "Queue Cleared", $"Removed {cleared} tracks from the queue."), ephemeral: true);
                    break;
                default:
                    await FollowupAsync(components: ComponentV2Builder.Error("Unknown Action", $"Unrecognized queue action: {action}"), ephemeral: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling queue options: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Queue Error", "An error occurred while managing the queue. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Handles volume up/down button clicks.
    /// Adjusts volume by 10% increments.</summary>
    /// <param name="direction">The direction (up/down)</param>
    [ComponentInteraction("volume:*")]
    public async Task HandleVolumeAsync(string direction)
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, "volume"))
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            if (await playerService.GetPlayerAsync(Context.Interaction, false) is not CustomLavaLinkPlayer player)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Player", "No active player found."), ephemeral: true);
                return;
            }
            float currentVolume = player.Volume;
            float step = 0.1f;
            float newVolume = direction.ToLowerInvariant() == "up"
                ? Math.Min(1.0f, currentVolume + step)
                : Math.Max(0.0f, currentVolume - step);

            await player.SetVolumeAsync(newVolume);
            Logs.Debug($"Volume set to {newVolume * 100:F0}% by {Context.User.Username}");

            // Update player UI (recreate image to update volume bar overlay)
            ButtonContext context = new() { Player = player, Interaction = Context.Interaction };
            ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
            await visualPlayer.AddOrUpdateVisualPlayerAsync(components, recreateImage: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling volume: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Volume Error", "An error occurred while adjusting volume."), ephemeral: true);
        }
    }

    /// <summary>Handles the repeat button click.
    /// Cycles through repeat modes: None → Queue → Track → None.</summary>
    [ComponentInteraction("repeat:*")]
    public async Task HandleRepeatAsync()
    {
        await DeferAsync();
        if (IsOnCooldown(Context.User.Id, "repeat"))
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            if (await playerService.GetPlayerAsync(Context.Interaction, false) is not CustomLavaLinkPlayer player)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Player", "No active player found."), ephemeral: true);
                return;
            }
            // Cycle: None → Queue → Track → None
            TrackRepeatMode nextMode = player.RepeatMode switch
            {
                TrackRepeatMode.None => TrackRepeatMode.Queue,
                TrackRepeatMode.Queue => TrackRepeatMode.Track,
                _ => TrackRepeatMode.None
            };
            await playerService.SetRepeatModeAsync(Context.Interaction, nextMode);
            Logs.Debug($"Repeat mode cycled to {nextMode} by {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling repeat: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Repeat Error", "An error occurred while changing repeat mode."), ephemeral: true);
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
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            visualPlayer.StopProgressTimer();
            await playerService.StopAsync(Context.Interaction, true);
            Logs.Info($"Player killed by {Context.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling kill: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "An error occurred while stopping playback. Please try again later."), ephemeral: true);
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
            Track? track = await plexMusicService.GetTrackDetailsAsync(trackKey);
            if (track == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Track Error", "Failed to retrieve track details."), ephemeral: true);
                return;
            }
            // Play the track
            await playerService.AddToQueueAsync(Context.Interaction, [track]);
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
            List<Track> tracks = await plexMusicService.GetTracksAsync(albumKey);
            if (tracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Empty Album", "The selected album has no tracks."), ephemeral: true);
                return;
            }
            // Add tracks to queue
            await playerService.AddToQueueAsync(Context.Interaction, tracks);
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
            List<Album> albums = await plexMusicService.GetAlbumsAsync(artistKey);
            if (albums.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Empty Artist", "The selected artist has no albums."), ephemeral: true);
                return;
            }
            // Get all tracks from all albums
            List<Track> allTracks = [];
            foreach (Album album in albums)
            {
                List<Track> tracks = await plexMusicService.GetTracksAsync(album.SourceKey);
                allTracks.AddRange(tracks);
            }
            if (allTracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Empty Artist", "The selected artist has no tracks."), ephemeral: true);
                return;
            }
            // Add all tracks to queue
            await playerService.AddToQueueAsync(Context.Interaction, allTracks);
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
            QueuedLavalinkPlayer? player = await playerService.GetPlayerAsync(Context.Interaction, true);
            if (player == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Player Error", "Failed to initialize player for YouTube playback."), ephemeral: true);
                return;
            }
            // Create the proper load options
            TrackLoadOptions loadOptions = new()
            {
                SearchMode = TrackSearchMode.None // Use None since we have a direct URL
            };
            // Load the track just to test if it's playable
            LavalinkTrack? lavalinkTrack = await audioService.Tracks.LoadTrackAsync(
                videoUrl,
                loadOptions,
                cancellationToken: CancellationToken.None);
            if (lavalinkTrack == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("YouTube Error", "This YouTube video cannot be played. It may be age-restricted or requires login."), ephemeral: true);
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
            await playerService.AddToQueueAsync(Context.Interaction, [track]);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling YouTube selection: {ex.Message}");
            // Check for specific error message
            if (ex.Message.Contains("login") || ex.Message.Contains("This video requires login"))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("YouTube Error", "This YouTube video cannot be played as it requires login or is age-restricted."), ephemeral: true);
            }
            else
            {
                await FollowupAsync(components: ComponentV2Builder.Error("YouTube Error", $"Error playing YouTube track: {ex.Message}"), ephemeral: true);
            }
        }
    }

    /// <summary>Shows the current queue as an embed.
    /// Displays the currently playing track and upcoming tracks in the queue.</summary>
    /// <param name="player">The player to show the queue for</param>
    /// <param name="page">The page number to show</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task ShowQueueAsync(CustomLavaLinkPlayer player, int page)
    {
        try
        {
            // Get the current track and queue
            CustomTrackQueueItem? currentTrack = player.CurrentItem as CustomTrackQueueItem;
            List<CustomTrackQueueItem?> queue = player.Queue.Select(item => item as CustomTrackQueueItem).ToList();

            const int itemsPerPage = 10;
            int totalTracks = queue.Count;
            int totalPages = Math.Max(1, (totalTracks + itemsPerPage - 1) / itemsPerPage);
            page = Math.Clamp(page, 1, totalPages);

            // Build now playing line
            string? nowPlayingLine = (page == 1 && currentTrack != null)
                ? $"**\u25B6\uFE0F Now Playing:** {currentTrack.Title} - {currentTrack.Artist} ({currentTrack.Duration})"
                : null;

            // Build queue text for current page
            int startIndex = (page - 1) * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, totalTracks);
            StringBuilder queueSb = new();
            for (int i = startIndex; i < endIndex; i++)
            {
                CustomTrackQueueItem? item = queue[i];
                if (item != null)
                    queueSb.AppendLine($"**#{i + 1}:** {item.Title} - {item.Artist} ({item.Duration})");
            }
            string queueText = queueSb.ToString().TrimEnd();

            if (totalTracks == 0 && currentTrack == null)
                queueText = "The queue is currently empty.";

            string footerLine = $"Page {page} of {totalPages} | {totalTracks} tracks queued";

            // Build pagination buttons
            ComponentBuilder components = new();
            if (totalPages > 1)
            {
                components.WithButton("Previous", $"queue_options:view:{Math.Max(1, page - 1)}",
                                   ButtonStyle.Secondary, disabled: page <= 1);
                components.WithButton("Next", $"queue_options:view:{Math.Min(totalPages, page + 1)}",
                                   ButtonStyle.Secondary, disabled: page >= totalPages);
            }
            await FollowupAsync(components: ComponentV2Builder.BuildQueueDisplay(
                nowPlayingLine, queueText, footerLine, components), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error showing queue: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Queue Error", "An error occurred while showing the queue. Please try again later."), ephemeral: true);
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