using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;

namespace PlexBot.Services;

/// <summary>Comprehensive service that manages audio playback in Discord voice channels, handling player lifecycle, track queueing, and providing rich metadata integration with Plex</summary>
public class PlayerService : IPlayerService
{
    private readonly IAudioService _audioService;
    private readonly float _defaultVolume;
    private readonly TimeSpan _inactivityTimeout;

    /// <summary>Constructs the player service with necessary dependencies and loads configuration from environment variables to ensure consistent playback settings</summary>
    /// <param name="audioService">The Lavalink audio service that provides the underlying audio streaming capabilities</param>
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
    public async Task<QueuedLavalinkPlayer?> GetPlayerAsync(IDiscordInteraction interaction, bool connectToVoiceChannel = true,
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
            PlayerChannelBehavior channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            PlayerRetrieveOptions retrieveOptions = new(channelBehavior);
            // Create player factory
            static ValueTask<CustomPlayer> CreatePlayerAsync(
                IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties,
                CancellationToken token = default)
            {
                return ValueTask.FromResult(new CustomPlayer(properties));
            }
            // Create player options
            CustomPlayerOptions playerOptions = new()
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
            if (result.Status == PlayerRetrieveStatus.Success)
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
            TrackLoadOptions loadOptions = new()
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
            CustomTrackQueueItem queueItem = new()
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
            TrackPlayProperties playProperties = new()
            {
                NoReplace = true // If something's already playing, add to queue instead of replacing
            };
            // Play the track
            Logs.Debug($"Playing track: {track.Title} by {track.Artist} (requested by {interaction.User.Username})");
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
    public async Task AddToQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks,
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
            int totalCount = trackList.Count;
            Logs.Debug($"Adding {totalCount} tracks to queue");
            // Process tracks in smaller batches
            const int batchSize = 3; // Process just 3 tracks at a time
            int successCount = 0;
            bool firstTrackProcessed = false;
            // Send a preliminary message for long playlists
            if (totalCount > 10)
            {
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = DiscordEmbedBuilder.Info("Processing Tracks", $"Processing {totalCount} tracks. This may take a moment...");
                });
            }
            // Keep track of failed tracks for a retry
            List<Track> failedTracks = [];
            // Process in batches
            for (int batchStart = 0; batchStart < totalCount; batchStart += batchSize)
            {
                // Get the current batch
                int currentBatchSize = Math.Min(batchSize, totalCount - batchStart);
                List<Track> batch = trackList.Skip(batchStart).Take(currentBatchSize).ToList();
                Logs.Debug($"Processing batch {batchStart / batchSize + 1} of {Math.Ceiling((double)totalCount / batchSize)} ({batch.Count} tracks)");
                // Process one track at a time within the batch - more reliable than parallel in small batches
                foreach (Track track in batch)
                {
                    try
                    {
                        Logs.Debug($"Loading track: {track.Title}");
                        LavalinkTrack? lavalinkTrack;
                        // Handle YouTube tracks differently
                        if (track.SourceSystem.Equals("youtube", StringComparison.OrdinalIgnoreCase))
                        {
                            // Same YouTube handling as before
                            TrackLoadOptions directOptions = new()
                            {
                                SearchMode = TrackSearchMode.None
                            };
                            lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                                track.PlaybackUrl,
                                directOptions,
                                cancellationToken: cancellationToken);
                            if (lavalinkTrack == null)
                            {
                                TrackLoadOptions searchOptions = new()
                                {
                                    SearchMode = TrackSearchMode.YouTube
                                };
                                lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                                    track.PlaybackUrl,
                                    searchOptions,
                                    cancellationToken: cancellationToken);
                            }
                        }
                        else
                        {
                            // For Plex tracks
                            TrackLoadOptions loadOptions = new()
                            {
                                SearchMode = TrackSearchMode.None
                            };
                            lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                                track.PlaybackUrl,
                                loadOptions,
                                cancellationToken: cancellationToken);
                        }
                        if (lavalinkTrack == null)
                        {
                            Logs.Warning($"Failed to load track: {track.Title} - will retry later");
                            failedTracks.Add(track);
                            continue;
                        }
                        Logs.Debug($"Successfully loaded track: {track.Title ?? lavalinkTrack.Title}");
                        // Create queue item
                        CustomTrackQueueItem queueItem = new()
                        {
                            // Prioritize Plex metadata over Lavalink metadata
                            Title = track.Title ?? lavalinkTrack.Title ?? "Unknown Title",
                            Artist = track.Artist ?? lavalinkTrack.Author ?? "Unknown Artist",
                            Album = track.Album,
                            ReleaseDate = track.ReleaseDate,
                            Artwork = track.ArtworkUrl ?? lavalinkTrack.ArtworkUri?.ToString() ?? "",
                            Url = track.PlaybackUrl,
                            ArtistUrl = track.ArtistUrl,
                            Duration = track.DurationDisplay,
                            Studio = track.Studio,
                            RequestedBy = interaction.User.Username,
                            Reference = new TrackReference(lavalinkTrack)
                        };
                        // Play first track or add to queue
                        if (!firstTrackProcessed && player.State != PlayerState.Playing && player.State != PlayerState.Paused)
                        {
                            Logs.Debug($"Playing first track: {queueItem.Title} by {queueItem.Artist}");
                            await player.PlayAsync(queueItem, cancellationToken: cancellationToken);
                            firstTrackProcessed = true;
                        }
                        else
                        {
                            await player.Queue.AddAsync(queueItem, cancellationToken);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Error processing track {track.Title}: {ex.Message}");
                        failedTracks.Add(track);
                    }
                    // Small delay between individual tracks for stability
                    await Task.Delay(100, cancellationToken);
                }
                // Longer delay between batches
                if (batchStart + batchSize < totalCount)
                {
                    await Task.Delay(300, cancellationToken); // 300ms pause between batches
                }
            }
            // Try to recover failed tracks (one retry attempt)
            if (failedTracks.Count > 0 && successCount > 0)  // Only retry if we had some successes
            {
                Logs.Debug($"Attempting to recover {failedTracks.Count} failed tracks");

                foreach (Track track in failedTracks)
                {
                    try
                    {
                        // Wait a bit longer before retry
                        await Task.Delay(500, cancellationToken);
                        Logs.Debug($"Retrying track: {track.Title}");
                        TrackLoadOptions loadOptions = new()
                        {
                            SearchMode = track.SourceSystem.Equals("youtube", StringComparison.OrdinalIgnoreCase)
                                ? TrackSearchMode.YouTube
                                : TrackSearchMode.None
                        };
                        LavalinkTrack? lavalinkTrack = await _audioService.Tracks.LoadTrackAsync(
                            track.PlaybackUrl,
                            loadOptions,
                            cancellationToken: cancellationToken);
                        if (lavalinkTrack == null)
                        {
                            Logs.Warning($"Failed to load track on retry: {track.Title}");
                            continue;
                        }
                        // Create queue item and add to queue
                        CustomTrackQueueItem queueItem = new()
                        {
                            // Prioritize Plex metadata over Lavalink metadata
                            Title = track.Title ?? lavalinkTrack.Title ?? "Unknown Title",
                            Artist = track.Artist ?? lavalinkTrack.Author ?? "Unknown Artist",
                            Album = track.Album,
                            ReleaseDate = track.ReleaseDate,
                            Artwork = track.ArtworkUrl ?? lavalinkTrack.ArtworkUri?.ToString() ?? "",
                            Url = track.PlaybackUrl,
                            ArtistUrl = track.ArtistUrl,
                            Duration = track.DurationDisplay,
                            Studio = track.Studio,
                            RequestedBy = interaction.User.Username,
                            Reference = new TrackReference(lavalinkTrack)
                        };
                        await player.Queue.AddAsync(queueItem, cancellationToken);
                        successCount++;
                        // Pause between retries
                        await Task.Delay(300, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Error retrying track {track.Title}: {ex.Message}");
                    }
                }
            }
            // Final message
            if (successCount > 0)
            {
                string message = $"Added {successCount} of {totalCount} tracks to the queue";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = DiscordEmbedBuilder.Success("Tracks Added", message);
                });
            }
            else
            {
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Embed = DiscordEmbedBuilder.Error("Failed to Add Tracks", "No tracks were added to the queue.");
                });
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error adding tracks to queue: {ex.Message}");
            throw new PlayerException($"Failed to add tracks to queue: {ex.Message}", "Queue", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> TogglePauseResumeAsync(IDiscordInteraction interaction,
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
            string result;
            // Toggle state based on current state
            if (player.State == PlayerState.Paused)
            {
                await player.ResumeAsync(cancellationToken);
                Logs.Debug($"Playback resumed by {interaction.User.Username}");
                result = "Resumed";
            }
            else if (player.State == PlayerState.Playing)
            {
                await player.PauseAsync(cancellationToken);
                Logs.Debug($"Playback paused by {interaction.User.Username}");
                result = "Paused";
            }
            else
            {
                throw new PlayerException("No track is currently playing", "Pause");
            }
            // Update player UI if it's our custom player
            if (player is CustomPlayer customPlayer)
            {
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = DiscordButtonBuilder.Instance.BuildButtons(ButtonFlag.VisualPlayer, context);
                await customPlayer.UpdateVisualPlayerAsync(components);
            }
            return result;
        }
        catch (Exception ex) when (ex is not PlayerException)
        {
            Logs.Error($"Error toggling pause/resume: {ex.Message}");
            throw new PlayerException($"Failed to toggle pause/resume: {ex.Message}", "Pause", ex);
        }
    }

    /// <inheritdoc />
    public async Task SkipTrackAsync(IDiscordInteraction interaction, CancellationToken cancellationToken = default)
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
            Logs.Debug($"Track skipped by {interaction.User.Username}");
            await interaction.FollowupAsync($"Skipped {trackTitle}.", ephemeral: true);
        }
        catch (Exception ex) when (ex is not PlayerException)
        {
            Logs.Error($"Error skipping track: {ex.Message}");
            throw new PlayerException($"Failed to skip track: {ex.Message}", "Skip", ex);
        }
    }

    /// <inheritdoc />
    public async Task SetRepeatModeAsync(IDiscordInteraction interaction, TrackRepeatMode repeatMode,
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
            if (player is CustomPlayer customPlayer)
            {
                // Update player UI if it's our custom player
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = DiscordButtonBuilder.Instance.BuildButtons(ButtonFlag.VisualPlayer, context);
                await customPlayer.UpdateVisualPlayerAsync(components, true); // Update Visual Player image
            }
            Logs.Debug($"Repeat mode set to {repeatMode} by {interaction.User.Username}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error setting repeat mode: {ex.Message}");
            throw new PlayerException($"Failed to set repeat mode: {ex.Message}", "Repeat", ex);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(IDiscordInteraction interaction, bool disconnect = false,
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
                Logs.Debug($"Player stopped and disconnected by {interaction.User.Username}");
            }
            else
            {
                Logs.Debug($"Player stopped by {interaction.User.Username}");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error stopping player: {ex.Message}");
            throw new PlayerException($"Failed to stop player: {ex.Message}", "Stop", ex);
        }
    }
}
