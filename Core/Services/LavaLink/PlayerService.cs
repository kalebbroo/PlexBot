using System.Collections.Concurrent;
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
public class PlayerService(VisualPlayerStateManager stateManager, IAudioService audioService, VisualPlayer visualPlayer, IServiceProvider serviceProvider, DiscordButtonBuilder buttonBuilder, ITrackResolverService trackResolver, QueueResolveService queueResolver)
    : IPlayerService
{
    // Serializes queue mutations per guild. Without this, two concurrent adds can both see "not playing"
    // and both call PlayAsync, and a replace can interleave with an add.
    private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> _guildQueueLocks = new();

    // Bumped by every clear, stop, and replace. A batch that resolves after one of these must not append its
    // remaining tracks, or the queue the user just cleared or replaced would come back.
    private static readonly ConcurrentDictionary<ulong, long> _queueGenerations = new();

    internal static long CurrentGeneration(ulong guildId) => _queueGenerations.GetOrAdd(guildId, 0);

    // Queue additions are applied in the order they were requested. Batches resolve in parallel, but each one
    // waits for its turn before it touches the queue, so a later request cannot land in the middle of an earlier
    // playlist. A batch that fails still releases its turn, so it never blocks the batches behind it.
    private static readonly ConcurrentDictionary<ulong, OrderedTurns> _queueTurns = new();

    private sealed class OrderedTurns
    {
        private readonly object _sync = new();
        private readonly Dictionary<long, TaskCompletionSource> _waiting = new();
        private long _issued;
        private long _next;

        public long Take()
        {
            lock (_sync) return _issued++;
        }

        public Task WaitTurn(long ticket)
        {
            lock (_sync)
            {
                if (ticket == _next) return Task.CompletedTask;
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[ticket] = waiter;
                return waiter.Task;
            }
        }

        public void Done(long ticket)
        {
            lock (_sync)
            {
                if (ticket != _next) return; // only the holder of the current turn may finish it
                _next++;
                if (_waiting.Remove(_next, out var waiter))
                    waiter.SetResult();
            }
        }
    }

    private static long BumpGeneration(ulong guildId) => _queueGenerations.AddOrUpdate(guildId, 1, (_, value) => value + 1);

    /// <inheritdoc />
    public async Task<QueuedLavalinkPlayer?> GetPlayerAsync(IDiscordInteraction interaction, bool connectToVoiceChannel = true,
        CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, _) = await TryGetPlayerAsync(interaction, connectToVoiceChannel, cancellationToken);
        return player;
    }

    /// <summary>Retrieves the guild's player and returns the reason it could not be retrieved, if it could not.
    /// Does not send any Discord response: callers decide how to report the failure, once.</summary>
    private async Task<(QueuedLavalinkPlayer? Player, string? Failure)> TryGetPlayerAsync(IDiscordInteraction interaction,
        bool connectToVoiceChannel, CancellationToken cancellationToken)
    {
        if (interaction.User is not IGuildUser user || user.VoiceChannel == null)
        {
            Logs.Warning($"Player lookup: user {interaction.User?.Id} has no voice channel in the gateway cache");
            return (null, "You must be in a voice channel to use the music player.");
        }
        try
        {
            ulong guildId = user.Guild.Id;
            ulong voiceChannelId = user.VoiceChannel.Id;
            PlayerChannelBehavior channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            PlayerRetrieveOptions retrieveOptions = new(channelBehavior);
            float defaultVolume = 0.2f;
            CustomPlayerOptions playerOptions = new()
            {
                DisconnectOnStop = false,
                SelfDeaf = true,
                TextChannel = interaction is SocketInteraction socketInteraction
                    ? socketInteraction.Channel as ITextChannel
                    : null,
                DefaultVolume = defaultVolume,
                InitialVolume = defaultVolume,
            };
            var optionsWrapper = Options.Create(playerOptions);
            PlayerResult<CustomLavaLinkPlayer> result = await audioService.Players
                .RetrieveAsync<CustomLavaLinkPlayer, CustomPlayerOptions>(guildId, voiceChannelId,
                    (properties, token) => ValueTask.FromResult(new CustomLavaLinkPlayer(properties, serviceProvider)),
                    optionsWrapper, retrieveOptions, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                // Log the real status: the user-facing text below is deliberately coarse
                Logs.Warning($"Player lookup failed: status={result.Status}, guild={guildId}, userVoice={voiceChannelId}, connect={connectToVoiceChannel}");
                string friendly = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "No active player. Start playback with /play first.",
                    _ => $"The player is unavailable right now ({result.Status}). Please try again."
                };
                return (null, friendly);
            }
            return (result.Player, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
    public Task<bool> AddToQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default)
        => AddTracksAsync(interaction, tracks, replaceQueue: false, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ReplaceQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default)
        => AddTracksAsync(interaction, tracks, replaceQueue: true, cancellationToken);

    /// <inheritdoc />
    public async Task<int> ClearQueueAsync(IDiscordInteraction interaction, CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for clear: {failure}", "Queue", failure ?? "No active player found.");
        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(player.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            BumpGeneration(player.GuildId);
            int removed = player.Queue.Count;
            await player.Queue.ClearAsync(cancellationToken);
            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Resolves the first track, then starts or queues it under the guild lock. The rest are resolved
    /// outside the lock and appended in order. The existing queue is cleared only after the first track has
    /// resolved, so a failed resolve leaves the queue intact.</summary>
    /// <returns>True if the tracks were applied to the queue; false if they were not (the first track failed to
    /// load, or a clear, stop, or replace superseded this request).</returns>
    private async Task<bool> AddTracksAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks, bool replaceQueue,
        CancellationToken cancellationToken)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, true, cancellationToken);
        if (player == null)
            throw new PlayerException($"Queue add failed: {failure}", "Connect", failure ?? "The player is unavailable right now.");

        ulong guildId = player.GuildId;
        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        OrderedTurns turns = _queueTurns.GetOrAdd(guildId, _ => new OrderedTurns());
        long ticket = turns.Take();
        // A replace bumps the generation when it is issued, so earlier pending work becomes stale, while later
        // requests carry the new generation and are not dropped by it. An add captures the current generation.
        long issuedGeneration = replaceQueue ? BumpGeneration(guildId) : CurrentGeneration(guildId);
        bool turnHeld = false;

        try
        {
            IUserMessage response = await interaction.GetOriginalResponseAsync();
            if (response.Channel is ITextChannel channel)
                stateManager.SetChannel(guildId, channel);

            List<Track> trackList = tracks.ToList();
            int totalCount = trackList.Count;
            Logs.Debug($"[guild {guildId}] Adding {totalCount} tracks to queue (replace={replaceQueue})");

            if (totalCount == 0) return true;

            // === STEP 1: Resolve and play the first track immediately ===
            Track firstTrack = trackList[0];
            LavalinkTrack? firstResolved = await trackResolver.ResolveTrackAsync(firstTrack, cancellationToken);

            if (firstResolved == null)
            {
                Logs.Error($"[guild {guildId}] Failed to load track: {firstTrack.Title}");
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Load Failed", $"Failed to load: {firstTrack.Title}");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return false;
            }

            CustomTrackQueueItem firstItem = new()
            {
                SourceTrack = firstTrack,
                RequestedBy = interaction.User.Username,
                Reference = new TrackReference(firstResolved)
            };

            // Wait for this batch's place in the request order before touching the queue
            await turns.WaitTurn(ticket);
            turnHeld = true;

            // The generation is checked under the gate, so a clear, stop, or replace that takes the gate first is
            // seen here. A superseded request is dropped before its first insertion, so it cannot repopulate a
            // queue the user was told was cleared.
            bool shouldPlay = false;
            long generation = 0;
            bool superseded = false;
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (CurrentGeneration(guildId) != issuedGeneration)
                {
                    superseded = true;
                }
                else
                {
                    if (replaceQueue)
                        await player.Queue.ClearAsync(cancellationToken);
                    generation = CurrentGeneration(guildId);

                    // Decide under the lock: a concurrent add may have just started playback
                    shouldPlay = player.State != PlayerState.Playing && player.State != PlayerState.Paused;
                    if (shouldPlay)
                        await player.PlayAsync(firstItem, cancellationToken: cancellationToken);
                    else
                        await player.Queue.AddAsync(firstItem, cancellationToken);
                }
            }
            finally
            {
                gate.Release();
            }

            if (superseded)
            {
                Logs.Warning($"[guild {guildId}] Dropping a queue request: the queue was cleared or replaced after it was issued");
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Info("Request Cancelled", "The queue was cleared or replaced before this request could be added.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return false;
            }

            // === STEP 2: Queue the rest as placeholders, resolved just in time ===
            // Only the first few items in the queue are loaded through Lavalink (QueueResolveService), so a large
            // playlist costs no more Plex requests than the tracks actually played, and the next request is not
            // held behind it.
            if (totalCount > 1)
            {
                List<ITrackQueueItem> placeholders = trackList.Skip(1)
                    .Select(t => (ITrackQueueItem)CustomTrackQueueItem.Placeholder(t, interaction.User.Username))
                    .ToList();

                // If the queue was cleared, stopped, or replaced since the first track went in, drop the rest
                // rather than resurrect stale tracks.
                await gate.WaitAsync(cancellationToken);
                try
                {
                    if (CurrentGeneration(guildId) != generation)
                        superseded = true;
                    else
                        await player.Queue.AddRangeAsync(placeholders, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }

                if (superseded)
                {
                    // The first track was already applied; the rest were dropped. Say so, and report it as not applied
                    // so the caller does not treat the request as a success.
                    Logs.Warning($"[guild {guildId}] Discarding {placeholders.Count} tracks: the queue changed after the first was added");
                    await interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Components = ComponentV2Builder.Info("Request Cancelled", "The queue was cleared or replaced before the rest of these tracks were added.");
                        msg.Embed = null;
                        msg.Flags = MessageFlags.ComponentsV2;
                    });
                    return false;
                }

                queueResolver.Wake(guildId);
                Logs.Info($"[guild {guildId}] Queued {totalCount} tracks ({placeholders.Count} to resolve as they come up)");

                // Rebuild the player image now that the queue is populated (for Next Up display)
                if (player is CustomLavaLinkPlayer customPlayerRefresh)
                {
                    ButtonContext ctx = new() { Player = customPlayerRefresh, Interaction = interaction };
                    ComponentBuilder refreshComponents = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, ctx);
                    await visualPlayer.AddOrUpdateVisualPlayerAsync(guildId, refreshComponents, recreateImage: true);
                }

                string summary = shouldPlay
                    ? $"Playing {firstTrack.Title} by {firstTrack.Artist}, and queued {placeholders.Count} more"
                    : $"Added {totalCount} tracks to the queue";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Success("Tracks Added", summary);
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            else
            {
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
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs.Error($"[guild {guildId}] Error adding tracks to queue: {ex.Message}");
            throw new PlayerException($"Failed to add tracks to queue: {ex.Message}", "Queue", ex);
        }
        finally
        {
            // Release this batch's place in the order, even on early return or failure. If it never reached the
            // queue step, wait for its turn first so the order stays intact.
            if (!turnHeld)
                await turns.WaitTurn(ticket);
            turns.Done(ticket);
        }
    }

    /// <inheritdoc />
    public async Task<string> TogglePauseResumeAsync(IDiscordInteraction interaction,
    CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for pause: {failure}", "Pause", failure ?? "No active player found.");
        try
        {
            string result;
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
            if (player is CustomLavaLinkPlayer customPlayer)
            {
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
                await visualPlayer.AddOrUpdateVisualPlayerAsync(customPlayer.GuildId, components);
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
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for skip: {failure}", "Skip", failure ?? "No active player found.");
        try
        {
            if (player.State != PlayerState.Playing && player.State != PlayerState.Paused)
            {
                throw new PlayerException("No track is currently playing", "Skip");
            }
            // Load the next track first if it's still a placeholder, so the skip lands on a checked track
            await queueResolver.EnsureHeadResolvedAsync(player, TimeSpan.FromSeconds(10), cancellationToken);
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
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for repeat: {failure}", "Repeat", failure ?? "No active player found.");
        try
        {
            player.RepeatMode = repeatMode;
            if (player is CustomLavaLinkPlayer customPlayer)
            {
                ButtonContext context = new()
                {
                    Player = customPlayer,
                    Interaction = interaction
                };
                ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
                await visualPlayer.AddOrUpdateVisualPlayerAsync(customPlayer.GuildId, components, true);
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
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for stop: {failure}", "Stop", failure ?? "No active player found.");
        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(player.GuildId, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                BumpGeneration(player.GuildId);
                queueResolver.Stop(player.GuildId);
                await player.StopAsync(cancellationToken);
                await player.Queue.ClearAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs.Error($"Error stopping player: {ex.Message}");
            throw new PlayerException($"Failed to stop player: {ex.Message}", "Stop", ex);
        }
    }
}
