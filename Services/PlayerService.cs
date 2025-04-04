using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Services;

/// <summary>
/// Service for managing audio players in Discord voice channels.
/// This service handles the creation, retrieval, and control of CustomPlayer instances,
/// providing a high-level interface for playing music from various sources
/// in Discord voice channels with rich metadata and visual interfaces.
/// </summary>
public class PlayerService : IPlayerService
{
    private readonly IAudioService _audioService;
    private readonly float _defaultVolume;
    private readonly TimeSpan _inactivityTimeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerService"/> class.
    /// Sets up the service with necessary dependencies and configuration.
    /// </summary>
    /// <param name="audioService">The Lavalink audio service for playback</param>
    public PlayerService(IAudioService audioService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

        // Get configuration from environment variables
        _defaultVolume = (float)(EnvConfig.GetDouble("PLAYER_DEFAULT_VOLUME", 50.0) / 100.0f);
        _defaultVolume = Math.Clamp(_defaultVolume, 0.0f, 1.0f);

        _inactivityTimeout = TimeSpan.FromMinutes(EnvConfig.GetDouble("PLAYER_INACTIVITY_TIMEOUT", 2.0));

        Logs.Init($"PlayerService initialized with default volume: {_defaultVolume * 100:F0}% and inactivity timeout: {_inactivityTimeout.TotalMinutes} minutes");
    }

    /// <inheritdoc />
    public async Task<QueuedLavalinkPlayer?> GetPlayerAsync(
        IDiscordInteraction interaction,
        bool connectToVoiceChannel = true,
        CancellationToken cancellationToken = default)
    {
        // Check if the user is in a voice channel
        if (interaction.User is not IGuildUser user || user.VoiceChannel == null)
        {
            await interaction.FollowupAsync("You must be in a voice channel to use the music player.", ephemeral: true);
            return null;
        }

        try
        {
            // Get guild and channel information
            ulong guildId = user.Guild.Id;
            ulong voiceChannelId = user.VoiceChannel.Id;

            // Determine channel behavior based on connectToVoiceChannel parameter
            var channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            var retrieveOptions = new PlayerRetrieveOptions(channelBehavior);

            // Create player factory
            static ValueTask<CustomPlayer> CreatePlayerAsync(
                IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties,
                CancellationToken token = default)
            {
                return ValueTask.FromResult(new CustomPlayer(properties));
            }

            // Create player options
            var playerOptions = new CustomPlayerOptions
            {
                DisconnectOnStop = false,
                SelfDeaf = true,
                // Get text channel based on interaction type
                TextChannel = interaction is SocketInteraction socketInteraction
                    ? socketInteraction.Channel as ITextChannel
                    : null,
                InactivityTimeout = _inactivityTimeout,
                DefaultVolume = _defaultVolume
            };

            // Wrap options for DI
            var optionsWrapper = Options.Create(playerOptions);

            // Retrieve or create the player
            PlayerResult<CustomPlayer> result = await _audioService.Players
                .RetrieveAsync<CustomPlayer, CustomPlayerOptions>(
                    guildId,
                    voiceChannelId,
                    CreatePlayerAsync,
                    optionsWrapper,
                    retrieveOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            // Handle retrieval failures
            if (!result.IsSuccess)
            {
                string errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected to a voice channel.",
                    _ => "An unknown error occurred while trying to retrieve the player."
                };

                await interaction.FollowupAsync(errorMessage, ephemeral: true);
                return null;
            }

            // Set volume if it's a new player
            if (result.Status == PlayerRetrieveStatus.Created)
            {
                await result.Player.SetVolumeAsync(_defaultVolume, cancellationToken);
                Logs.Debug($"Created new player for guild {guildId} with volume {_defaultVolume * 100:F0}%");
            }

            return result.Player;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error getting player: {ex.Message}");
            throw new PlayerException($"Failed to get player: {ex.Message}", "Connect", ex);
        }
    }

    /// <inheritdoc />
    public async Task PlayTrackAsync(
        IDiscordInteraction interaction,
        Track track,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for playback");
            return;
        }

        try
        {
            // Load the track through Lavalink
            Logs.Debug($"Loading track: {track.Title} from URL: {track.PlaybackUrl}");

            // Create track load options
            var loadOptions = new TrackLoadOptions
            {
                SearchMode = TrackSearchMode.None
            };

            LavalinkTrack? lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                track.PlaybackUrl,
                loadOptions,
                cancellationToken: cancellationToken);

            if (lavalinkTrack == null)
            {
                Logs.Error($"Failed to load track: {track.Title} from URL: {track.PlaybackUrl}");
                await interaction.FollowupAsync($"Failed to load track: {track.Title}", ephemeral: true);
                return;
            }

            // Create custom queue item with rich metadata
            CustomTrackQueueItem queueItem = new CustomTrackQueueItem
            {
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                ReleaseDate = track.ReleaseDate,
                Artwork = track.ArtworkUrl,
                Url = track.PlaybackUrl,
                ArtistUrl = track.ArtistUrl,
                Duration = track.DurationDisplay,
                Studio = track.Studio,
                RequestedBy = interaction.User.Username,
                Reference = new TrackReference(lavalinkTrack)
            };

            // Set playback options
            TrackPlayProperties playProperties = new TrackPlayProperties
            {
                NoReplace = true // If something's already playing, add to queue instead of replacing
            };

            // Play the track
            Logs.Info($"Playing track: {track.Title} by {track.Artist} (requested by {interaction.User.Username})");
            await player.PlayAsync(queueItem, playProperties, cancellationToken);

            await interaction.FollowupAsync($"Playing: {track.Title} by {track.Artist}", ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error playing track: {ex.Message}");
            throw new PlayerException($"Failed to play track: {ex.Message}", "Play", ex);
        }
    }

    /// <inheritdoc />
    public async Task AddToQueueAsync(
        IDiscordInteraction interaction,
        IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for queueing");
            return;
        }

        try
        {
            List<Track> trackList = tracks.ToList();
            int addedCount = 0;
            int totalCount = trackList.Count;

            foreach (Track track in trackList)
            {
                try
                {
                    Logs.Debug($"Loading track for queue: {track.Title}");

                    // Create track load options
                    var loadOptions = new TrackLoadOptions
                    {
                        SearchMode = TrackSearchMode.None
                    };

                    // Load the track through Lavalink
                    LavalinkTrack? lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                        track.PlaybackUrl,
                        loadOptions,
                        cancellationToken: cancellationToken);

                    if (lavalinkTrack == null)
                    {
                        Logs.Warning($"Failed to load track for queue: {track.Title}");
                        continue;
                    }

                    // Create custom queue item with rich metadata
                    CustomTrackQueueItem queueItem = new CustomTrackQueueItem
                    {
                        Title = track.Title,
                        Artist = track.Artist,
                        Album = track.Album,
                        ReleaseDate = track.ReleaseDate,
                        Artwork = track.ArtworkUrl,
                        Url = track.PlaybackUrl,
                        ArtistUrl = track.ArtistUrl,
                        Duration = track.DurationDisplay,
                        Studio = track.Studio,
                        RequestedBy = interaction.User.Username,
                        Reference = new TrackReference(lavalinkTrack)
                    };

                    // Add to queue or play if first track
                    if (player.State != PlayerState.Playing && player.State != PlayerState.Paused && addedCount == 0)
                    {
                        // Play the first track immediately
                        await player.PlayAsync(queueItem, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        // Add to queue
                        await player.Queue.AddAsync(queueItem, cancellationToken);
                    }

                    addedCount++;
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Error adding track to queue: {track.Title} - {ex.Message}");
                    // Continue with other tracks
                }
            }

            Logs.Info($"Added {addedCount} of {totalCount} tracks to queue");

            if (addedCount > 0)
            {
                string message = addedCount == 1
                    ? $"Added '{trackList[0].Title}' to the queue"
                    : $"Added {addedCount} tracks to the queue";

                await interaction.FollowupAsync(message, ephemeral: true);
            }
            else
            {
                await interaction.FollowupAsync("Failed to add any tracks to queue.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error adding tracks to queue: {ex.Message}");
            throw new PlayerException($"Failed to add tracks to queue: {ex.Message}", "Queue", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> TogglePauseResumeAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, false, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for pause/resume");
            throw new PlayerException("No active player found", "Pause");
        }

        try
        {
            if (player.State == PlayerState.Paused)
            {
                // Resume playback
                await player.ResumeAsync(cancellationToken);
                Logs.Info($"Playback resumed by {interaction.User.Username}");

                // Update player UI if it's our custom player
                if (player is CustomPlayer customPlayer)
                {
                    var components = new ComponentBuilder()
                        .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                        .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                    await customPlayer.UpdatePlayerComponentsAsync(components);
                }

                return "Resumed";
            }
            else if (player.State == PlayerState.Playing)
            {
                // Pause playback
                await player.PauseAsync(cancellationToken);
                Logs.Info($"Playback paused by {interaction.User.Username}");

                // Update player UI if it's our custom player
                if (player is CustomPlayer customPlayer)
                {
                    var components = new ComponentBuilder()
                        .WithButton("Resume", "pause_resume:resume", ButtonStyle.Success)
                        .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                        .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                        .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                        .WithButton("Kill", "kill:kill", ButtonStyle.Danger);

                    await customPlayer.UpdatePlayerComponentsAsync(components);
                }

                return "Paused";
            }
            else
            {
                // Not playing or paused
                throw new PlayerException("No track is currently playing", "Pause");
            }
        }
        catch (Exception ex) when (ex is not PlayerException)
        {
            Logs.Error($"Error toggling pause/resume: {ex.Message}");
            throw new PlayerException($"Failed to toggle pause/resume: {ex.Message}", "Pause", ex);
        }
    }

    /// <inheritdoc />
    public async Task SkipTrackAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, false, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for skip");
            throw new PlayerException("No active player found", "Skip");
        }

        try
        {
            if (player.State != PlayerState.Playing && player.State != PlayerState.Paused)
            {
                throw new PlayerException("No track is currently playing", "Skip");
            }

            // Get current track info for the message
            string trackTitle = "the current track";
            if (player is CustomPlayer customPlayer && customPlayer.CurrentItem is CustomTrackQueueItem currentTrack)
            {
                trackTitle = currentTrack.Title ?? "the current track";
            }

            // Skip the current track - default to 1 track to fix argument type error
            await player.SkipAsync(1, cancellationToken);
            Logs.Info($"Track skipped by {interaction.User.Username}");

            await interaction.FollowupAsync($"Skipped {trackTitle}.", ephemeral: true);
        }
        catch (Exception ex) when (ex is not PlayerException)
        {
            Logs.Error($"Error skipping track: {ex.Message}");
            throw new PlayerException($"Failed to skip track: {ex.Message}", "Skip", ex);
        }
    }

    /// <inheritdoc />
    public async Task SetRepeatModeAsync(
        IDiscordInteraction interaction,
        TrackRepeatMode repeatMode,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, false, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for setting repeat mode");
            throw new PlayerException("No active player found", "Repeat");
        }

        try
        {
            // Set the repeat mode
            player.RepeatMode = repeatMode;

            string modeDescription = repeatMode switch
            {
                TrackRepeatMode.None => "Repeat mode disabled",
                TrackRepeatMode.Track => "Now repeating current track",
                TrackRepeatMode.Queue => "Now repeating the entire queue",
                _ => "Unknown repeat mode"
            };

            Logs.Info($"Repeat mode set to {repeatMode} by {interaction.User.Username}");
            await interaction.FollowupAsync(modeDescription, ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error setting repeat mode: {ex.Message}");
            throw new PlayerException($"Failed to set repeat mode: {ex.Message}", "Repeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(
        IDiscordInteraction interaction,
        bool disconnect = false,
        CancellationToken cancellationToken = default)
    {
        QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, false, cancellationToken);

        if (player == null)
        {
            Logs.Warning("Failed to get player for stop");
            throw new PlayerException("No active player found", "Stop");
        }

        try
        {
            // Stop playback
            await player.StopAsync(cancellationToken);

            // Clear the queue
            await player.Queue.ClearAsync(cancellationToken);

            // Disconnect if requested
            if (disconnect)
            {
                await player.DisconnectAsync(cancellationToken);
                Logs.Info($"Player stopped and disconnected by {interaction.User.Username}");
                await interaction.FollowupAsync("Playback stopped and bot disconnected from voice channel.", ephemeral: true);
            }
            else
            {
                Logs.Info($"Player stopped by {interaction.User.Username}");
                await interaction.FollowupAsync("Playback stopped and queue cleared.", ephemeral: true);
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error stopping player: {ex.Message}");
            throw new PlayerException($"Failed to stop player: {ex.Message}", "Stop", ex);
        }
    }
}
