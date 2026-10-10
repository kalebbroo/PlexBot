using System.Collections.Concurrent;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Messages;
using PlexBot.Core.Events;
using PlexBot.Core.Exceptions;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services;
using PlexBot.Core.Services.Music;
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

    public static long CurrentGeneration(ulong guildId) => _queueGenerations.GetOrAdd(guildId, 0);

    // Queue additions are applied in the order they were requested. Batches resolve in parallel, but each one
    // waits for its turn before it touches the queue, so a later request cannot land in the middle of an earlier
    // playlist. A batch that fails still releases its turn, so it never blocks the batches behind it.
    private static readonly ConcurrentDictionary<ulong, OrderedTurns> _queueTurns = new();

    /// <summary>How long a queue request may wait for its turn or for the queue lock before it gives up. Resolving the
    /// first track is not counted.</summary>
    internal static readonly TimeSpan BatchDeadline = TimeSpan.FromMinutes(5);

    private static long BumpGeneration(ulong guildId) => _queueGenerations.AddOrUpdate(guildId, 1, (_, value) => value + 1);

    /// <inheritdoc />
    public async Task<QueuedLavalinkPlayer?> GetPlayerAsync(IDiscordInteraction interaction, bool connectToVoiceChannel = true,
        CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, _) = await TryGetPlayerAsync(interaction, connectToVoiceChannel, cancellationToken);
        return player;
    }

    /// <inheritdoc />
    public CustomLavaLinkPlayer? TryGetCachedPlayer(ulong guildId) =>
        audioService.Players.TryGetPlayer<CustomLavaLinkPlayer>(guildId, out CustomLavaLinkPlayer? player) ? player : null;

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
        await AddToQueueAsync(interaction, [track], cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> AddToQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks, bool playNext = false,
        CancellationToken cancellationToken = default)
        => AddTracksAsync(interaction, tracks, replaceQueue: false, playNext, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> PreviousTrackAsync(IDiscordInteraction interaction, CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player is not CustomLavaLinkPlayer custom)
            throw new PlayerException($"No player for previous: {failure}", "Previous", failure ?? Notices.NoPlayer.Body);

        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(custom.GuildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            CustomTrackQueueItem? previous = custom.TakePreviousTrack();
            if (previous is null)
                return false;

            CustomTrackQueueItem? playing = custom.CurrentItem as CustomTrackQueueItem;
            bool active = custom.State is PlayerState.Playing or PlayerState.Paused;
            if (playing is not null && active)
            {
                // The playing track goes back into the queue, so Next returns to it. Then the earlier track is put in
                // front of it and the current track is skipped, which plays the earlier one.
                await custom.Queue.InsertAsync(0, CopyOf(playing), cancellationToken);
                await custom.Queue.InsertAsync(0, CopyOf(previous), cancellationToken);
                await custom.SkipAsync(1, cancellationToken);
            }
            else
            {
                await custom.PlayAsync(CopyOf(previous), cancellationToken: cancellationToken);
            }
            Logs.Debug($"[guild {custom.GuildId}] Went back to {previous.Title}");
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A new queue item for the same track, so the queue never holds an item that is also playing</summary>
    private static CustomTrackQueueItem CopyOf(CustomTrackQueueItem item) => new()
    {
        SourceTrack = item.SourceTrack,
        RequestedBy = item.RequestedBy,
        Reference = item.Reference
    };

    /// <inheritdoc />
    public Task<bool> ReplaceQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default)
        => AddTracksAsync(interaction, tracks, replaceQueue: true, playNext: false, cancellationToken);

    /// <inheritdoc />
    public async Task<int> ClearQueueAsync(IDiscordInteraction interaction, CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for clear: {failure}", "Queue", failure ?? Notices.NoPlayer.Body);
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
        bool playNext, CancellationToken cancellationToken)
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
            TrackResolution firstResolution = await trackResolver.ResolveTrackAsync(firstTrack, cancellationToken);

            if (firstResolution.Track is not LavalinkTrack firstResolved)
            {
                // Nothing was queued, so this request must not hold up the ones behind it while its error is sent
                turns.Release(ticket);
                Logs.Error($"[guild {guildId}] Failed to load track ({firstResolution.Outcome}): {PlexUrlHelper.Describe(firstTrack)}");
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Load Failed", $"Couldn't play **{firstTrack.Title}**: {firstResolution.FailureReason}");
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
            await WaitWithinDeadlineAsync(ct => turns.WaitTurnAsync(ticket, ct), cancellationToken, "its turn in the queue");

            // The generation is checked under the gate, so a clear, stop, or replace that takes the gate first is
            // seen here. A superseded request is dropped before its first insertion, so it cannot repopulate a
            // queue the user was told was cleared.
            bool shouldPlay = false;
            long generation = 0;
            bool superseded = false;
            await WaitWithinDeadlineAsync(ct => gate.WaitAsync(ct), cancellationToken, "the queue lock");
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
                    else if (playNext)
                        await player.Queue.InsertAsync(0, firstItem, cancellationToken);
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
                turns.Release(ticket);
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
                await WaitWithinDeadlineAsync(ct => gate.WaitAsync(ct), cancellationToken, "the queue lock");
                try
                {
                    if (CurrentGeneration(guildId) != generation)
                        superseded = true;
                    else if (playNext)
                        // Right behind the first track: at the front when it is playing, otherwise after the one just inserted
                        await player.Queue.InsertRangeAsync(shouldPlay ? 0 : 1, placeholders, cancellationToken);
                    else
                        await player.Queue.AddRangeAsync(placeholders, cancellationToken);
                }
                finally
                {
                    gate.Release();
                }

                // The rest are queued (or dropped), so the batches behind this one may go in now
                turns.Release(ticket);

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
                    : playNext ? $"Added {totalCount} tracks to play next" : $"Added {totalCount} tracks to the queue";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Success("Tracks Added", summary);
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            else
            {
                turns.Release(ticket);
                string message = shouldPlay
                    ? $"Playing: {firstTrack.Title} by {firstTrack.Artist}"
                    : playNext ? $"Up next: {firstTrack.Title} by {firstTrack.Artist}" : $"Added to queue: {firstTrack.Title} by {firstTrack.Artist}";
                await interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Success("Track Added", message);
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            return true;
        }
        catch (PlayerException ex)
        {
            // A queue wait ran past its deadline: keep that message rather than wrapping it as a generic failure
            Logs.Warning($"[guild {guildId}] Queue request not added: {ex.Message}");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs.Error($"[guild {guildId}] Error adding tracks to queue: {ex.Message}");
            throw new PlayerException($"Failed to add tracks to queue: {ex.Message}", "Queue", ex);
        }
        finally
        {
            // Frees this batch's place in the order on every path that did not already do so (early return, failure, a
            // wait that timed out or was cancelled). Releasing twice is harmless, and it never waits.
            turns.Release(ticket);
        }
    }

    /// <summary>Waits for a queue turn or lock, giving up after <see cref="BatchDeadline"/>. The caller's own cancellation
    /// passes through unchanged; only the deadline becomes a PlayerException.</summary>
    private static async Task WaitWithinDeadlineAsync(Func<CancellationToken, Task> wait, CancellationToken cancellationToken, string what)
    {
        using CancellationTokenSource deadline = new(BatchDeadline, TimeProvider.System);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await wait(linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new PlayerException($"Gave up waiting for {what} after {BatchDeadline.TotalMinutes:N0} minutes", "Queue",
                "The queue is busy right now. Please try again in a moment.");
        }
    }

    /// <inheritdoc />
    public async Task<string> TogglePauseResumeAsync(IDiscordInteraction interaction,
    CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for pause: {failure}", "Pause", failure ?? Notices.NoPlayer.Body);
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
            throw new PlayerException($"No player for skip: {failure}", "Skip", failure ?? Notices.NoPlayer.Body);
        try
        {
            if (player.State != PlayerState.Playing && player.State != PlayerState.Paused)
            {
                throw new PlayerException("No track is currently playing", "Skip");
            }
            // Skip the current track. CustomLavaLinkPlayer resolves the next item first if it is still a placeholder;
            // the player UI updates automatically via NotifyTrackStartedAsync.
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
            throw new PlayerException($"No player for repeat: {failure}", "Repeat", failure ?? Notices.NoPlayer.Body);
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
    public async Task<int> AppendRadioTracksAsync(ulong guildId, IReadOnlyList<Track> tracks, long generation,
        CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
            return 0;
        if (await audioService.Players.GetPlayerAsync(guildId, cancellationToken: cancellationToken) is not QueuedLavalinkPlayer player)
            return 0;

        List<ITrackQueueItem> placeholders = tracks
            .Select(t => (ITrackQueueItem)CustomTrackQueueItem.Placeholder(t, "Radio"))
            .ToList();

        // Same gate as every other queue change: a clear, stop or replace that took the gate first bumps the generation
        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentGeneration(guildId) != generation)
            {
                Logs.Debug($"[guild {guildId}] Dropping radio refill: the queue changed during the fetch");
                return 0;
            }
            await player.Queue.AddRangeAsync(placeholders, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        queueResolver.Wake(guildId);
        return placeholders.Count;
    }

    /// <inheritdoc />
    public async Task StopAsync(IDiscordInteraction interaction, bool disconnect = false,
        CancellationToken cancellationToken = default)
    {
        (QueuedLavalinkPlayer? player, string? failure) = await TryGetPlayerAsync(interaction, false, cancellationToken);
        if (player == null)
            throw new PlayerException($"No player for stop: {failure}", "Stop", failure ?? Notices.NoPlayer.Body);
        SemaphoreSlim gate = _guildQueueLocks.GetOrAdd(player.GuildId, _ => new SemaphoreSlim(1, 1));
        try
        {
            // A kill announces the player is gone even when stopping or disconnecting throws, so the card is replaced
            await PlayerTeardown.RunAsync(async () =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    BumpGeneration(player.GuildId);
                    queueResolver.Stop(player.GuildId);
                    serviceProvider.GetRequiredService<RadioSessionManager>().StopSession(player.GuildId);
                    await player.StopAsync(cancellationToken);
                    await player.Queue.ClearAsync(cancellationToken);
                }
                finally
                {
                    gate.Release();
                }

                if (disconnect)
                    await player.DisconnectAsync(cancellationToken);
            }, () =>
            {
                if (disconnect)
                    serviceProvider.GetRequiredService<BotEventBus>().PublishPlayerDestroyed(player.GuildId);
            });

            if (disconnect)
                Logs.Debug($"Player stopped and disconnected by {interaction.User.Username}");
            else
                Logs.Debug($"Player stopped by {interaction.User.Username}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logs.Error($"Error stopping player: {ex.Message}");
            throw new PlayerException($"Failed to stop player: {ex.Message}", "Stop", ex);
        }
    }
}
