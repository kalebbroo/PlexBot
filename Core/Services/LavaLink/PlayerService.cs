using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Comprehensive service that manages audio playback in Discord voice channels, handling player lifecycle, track queueing, and providing rich metadata integration with Plex</summary>
/// <remarks>Constructs the player service with necessary dependencies and loads configuration from environment variables to ensure consistent playback settings</remarks>
/// <param name="audioService">The Lavalink audio service that provides the underlying audio streaming capabilities</param>
public class PlayerService(VisualPlayerStateManager stateManager, IAudioService audioService, VisualPlayer visualPlayer, IServiceProvider serviceProvider, DiscordButtonBuilder buttonBuilder, ITrackResolverService trackResolver)
    : IPlayerService
{
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
            float defaultVolume = 0.2f;
            // Create player options
            CustomPlayerOptions playerOptions = new()
            {
                DisconnectOnStop = false,
                SelfDeaf = true,
                // Get text channel based on interaction type
                TextChannel = interaction is SocketInteraction socketInteraction
                    ? socketInteraction.Channel as ITextChannel
                    : null,
                DefaultVolume = defaultVolume,
            };
            // Wrap options for DI
            var optionsWrapper = Options.Create(playerOptions);
            // Retrieve or create the player
            PlayerResult<CustomLavaLinkPlayer> result = await audioService.Players
            .RetrieveAsync<CustomLavaLinkPlayer, CustomPlayerOptions>(guildId, voiceChannelId,
                (properties, token) => ValueTask.FromResult(new CustomLavaLinkPlayer(properties, serviceProvider)),
                optionsWrapper, retrieveOptions, cancellationToken).ConfigureAwait(false);
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
                await result.Player.SetVolumeAsync(defaultVolume, cancellationToken);
                Logs.Debug($"Created new player for guild {guildId} with volume {defaultVolume * 100:F0}%");
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
        await AddToQueueAsync(interaction, [track], cancellationToken);
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
            IUserMessage response = await interaction.GetOriginalResponseAsync();
            ITextChannel? channel = response.Channel as ITextChannel;
            stateManager.CurrentPlayerChannel = channel ?? throw new InvalidOperationException("CurrentPlayerChannel is not set");

            List<Track> trackList = tracks.ToList();
            int totalCount = trackList.Count;
            Logs.Debug($"Adding {totalCount} tracks to queue");

            if (totalCount == 0) return;

            // === STEP 1: Resolve and play the first track immediately ===
            Track firstTrack = trackList[0];
            LavalinkTrack? firstResolved = await trackResolver.ResolveTrackAsync(firstTrack, cancellationToken);

            if (firstResolved == null)
            {
                Logs.Error($"Failed to load track: {firstTrack.Title}");
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Load Failed", $"Failed to load: {firstTrack.Title}");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return;
            }

            CustomTrackQueueItem firstItem = new()
            {
                SourceTrack = firstTrack,
                RequestedBy = interaction.User.Username,
                Reference = new TrackReference(firstResolved)
            };

            // Start playback if nothing is playing, otherwise add to queue
            bool shouldPlay = player.State != PlayerState.Playing && player.State != PlayerState.Paused;
            if (shouldPlay)
            {
                Logs.Debug($"Playing first track: {firstTrack.Title} by {firstTrack.Artist}");
                await player.PlayAsync(firstItem, cancellationToken: cancellationToken);
            }
            else
            {
                await player.Queue.AddAsync(firstItem, cancellationToken);
            }

            // === STEP 2: Resolve remaining tracks in parallel ===
            if (totalCount > 1)
            {
                List<Track> remaining = trackList.Skip(1).ToList();

                // Show progress for large playlists
                if (totalCount > 10)
                {
                    await interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Components = ComponentV2Builder.Info("Loading Tracks",
                            $"Playing first track. Resolving {remaining.Count} more in background...");
                        msg.Embed = null;
                        msg.Flags = MessageFlags.ComponentsV2;
                    });
                }

                // Use a lock to ensure thread-safe queue insertion
                using SemaphoreSlim queueLock = new(1, 1);

                int successCount = await trackResolver.ResolveTracksParallelAsync(
                    remaining,
                    onResolved: async (Track track, LavalinkTrack resolved, int index) =>
                    {
                        CustomTrackQueueItem item = new()
                        {
                            SourceTrack = track,
                            RequestedBy = interaction.User.Username,
                            Reference = new TrackReference(resolved)
                        };

                        await queueLock.WaitAsync(cancellationToken);
                        try
                        {
                            await player.Queue.AddAsync(item, cancellationToken);
                        }
                        finally
                        {
                            queueLock.Release();
                        }
                    },
                    maxConcurrency: 5,
                    cancellationToken: cancellationToken);

                int totalSuccess = successCount + 1; // +1 for the first track

                // Rebuild the player image now that the queue is fully populated (for Next Up display)
                if (player is CustomLavaLinkPlayer customPlayerRefresh)
                {
                    ButtonContext ctx = new() { Player = customPlayerRefresh, Interaction = interaction };
                    ComponentBuilder refreshComponents = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, ctx);
                    await visualPlayer.AddOrUpdateVisualPlayerAsync(refreshComponents, recreateImage: true);
                }

                // Final status message
                string message = totalSuccess == totalCount
                    ? $"Added {totalSuccess} tracks to the queue"
                    : $"Added {totalSuccess} of {totalCount} tracks to the queue";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Success("Tracks Added", message);
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            else
            {
                // Single track — show appropriate message
                string message = shouldPlay
                    ? $"Playing: {firstTrack.Title} by {firstTrack.Artist}"
                    : $"Added to queue: {firstTrack.Title} by {firstTrack.Artist}";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Success("Track Added", message);
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
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
            if (player is CustomLavaLinkPlayer customPlayer)
            {
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
                await visualPlayer.AddOrUpdateVisualPlayerAsync(components);
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
            // Skip the current track — the player UI updates automatically via NotifyTrackStartedAsync
            await player.SkipAsync(1, cancellationToken);
            Logs.Debug($"Track skipped by {interaction.User.Username}");
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
            if (player is CustomLavaLinkPlayer customPlayer)
            {
                // Update player UI if it's our custom player
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
                await visualPlayer.AddOrUpdateVisualPlayerAsync(components, true); // Update Visual Player image
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
