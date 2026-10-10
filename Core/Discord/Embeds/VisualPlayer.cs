using System.Collections.Concurrent;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Embeds;

public class VisualPlayer(
    VisualPlayerStateManager stateManager,
    IOptions<PlayerOptions> playerOptions,
    IAudioService audioService,
    DiscordButtonBuilder buttonBuilder,
    ITrackPrefetchService prefetchService) : IDisposable
{
    // Progress updates are throttled to this interval. Discord's per-channel message edit limit is
    // roughly 5 per 5 seconds, so a 1-second loop queues behind rate limits and the clock drifts.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _progressTimers = new();
    private readonly ConcurrentDictionary<ulong, ProgressAnchor> _progressAnchors = new();

    // One update at a time per guild. Without this, a track start and a queue refresh that land together both see
    // no message yet and each send a new one, leaving two players in the channel.
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _updateLocks = new();

    // An update that waits this long for the guild's previous update is skipped, so a slow Discord edit cannot stall
    // the callers behind it. Skipping does not throw: the batch that asked for the update has already queued its tracks.
    private static readonly TimeSpan UpdateLockWait = TimeSpan.FromSeconds(15);

    /// <summary>Updates or creates the visual player for a guild with current track information and buttons using Components V2</summary>
    public async Task AddOrUpdateVisualPlayerAsync(ulong guildId, ComponentBuilder components, bool recreateImage = false)
    {
        SemaphoreSlim updateLock = _updateLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        if (!await updateLock.WaitAsync(UpdateLockWait).ConfigureAwait(false))
        {
            Logs.Warning($"[guild {guildId}] Skipped a player card update: the previous update was still running after {UpdateLockWait.TotalSeconds:N0}s");
            return;
        }
        try
        {
            await AddOrUpdateCoreAsync(guildId, components, recreateImage).ConfigureAwait(false);
        }
        finally
        {
            updateLock.Release();
        }
    }

    private async Task AddOrUpdateCoreAsync(ulong guildId, ComponentBuilder components, bool recreateImage)
    {
        try
        {
            CustomLavaLinkPlayer? player = await audioService.Players.GetPlayerAsync(guildId) as CustomLavaLinkPlayer;
            IUserMessage? message = stateManager.GetMessage(guildId);

            string? statusLine = stateManager.UseProgressBar ? BuildStatusLine(guildId, player) : null;

            // Button/status-only update (no image regeneration needed)
            if (!recreateImage && message != null)
            {
                MessageComponent cv2 = stateManager.UseModernPlayer
                    ? ComponentV2Builder.BuildModernPlayer(statusLine, components)
                    : BuildClassicCV2(player, statusLine, components);

                await message.ModifyAsync(msg =>
                {
                    msg.Components = cv2;
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
                Logs.Debug($"[guild {guildId}] Updated player via CV2 successfully");
                return;
            }

            if (player?.CurrentItem is not CustomTrackQueueItem currentTrack)
            {
                Logs.Warning($"[guild {guildId}] Cannot update visual player: No current track");
                return;
            }

            // Start progress timer on new track (only when progress bar is enabled)
            if (stateManager.UseProgressBar)
                StartProgressTimer(guildId);

            // Get upcoming tracks from the queue for the "Next Up" display
            var upcomingTracks = player.Queue
                .Take(2)
                .OfType<CustomTrackQueueItem>()
                .ToList();

            // Update existing message with new image/content
            if (message != null)
            {
                try
                {
                    if (stateManager.UseModernPlayer)
                    {
                        using MemoryStream memoryStream = await ImageBuilder.BuildPlayerPngAsync(currentTrack, player, upcomingTracks, prefetchService);
                        FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                        MessageComponent cv2 = ComponentV2Builder.BuildModernPlayer(statusLine, components);
                        await message.ModifyAsync(msg =>
                        {
                            msg.Attachments = new[] { fileAttachment };
                            msg.Components = cv2;
                            msg.Embed = null;
                            msg.Flags = MessageFlags.ComponentsV2;
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        MessageComponent cv2 = BuildClassicCV2(player, statusLine, components);
                        await message.ModifyAsync(msg =>
                        {
                            msg.Components = cv2;
                            msg.Embed = null;
                            msg.Attachments = new List<FileAttachment>();
                            msg.Flags = MessageFlags.ComponentsV2;
                        }).ConfigureAwait(false);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Logs.Warning($"[guild {guildId}] Failed to update existing player, creating new one: {ex.Message}");
                    try
                    {
                        await message.DeleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore delete failures
                    }
                    stateManager.SetMessage(guildId, null);
                }
            }

            ITextChannel? channel = stateManager.GetChannel(guildId);
            if (channel is null)
            {
                Logs.Warning($"[guild {guildId}] No channel known for visual player; skipping message creation");
                return;
            }

            // Create new player message
            if (stateManager.UseModernPlayer)
            {
                using MemoryStream memoryStream = await ImageBuilder.BuildPlayerPngAsync(currentTrack, player, upcomingTracks, prefetchService);
                FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                MessageComponent cv2 = ComponentV2Builder.BuildModernPlayer(statusLine, components);
                stateManager.SetMessage(guildId, await channel.SendFileAsync(fileAttachment, components: cv2).ConfigureAwait(false));
            }
            else
            {
                MessageComponent cv2 = BuildClassicCV2(player, statusLine, components);
                stateManager.SetMessage(guildId, await channel.SendMessageAsync(components: cv2).ConfigureAwait(false));
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"[guild {guildId}] Error updating visual player: {ex.Message}");
        }
    }

    /// <summary>Called when a guild's player disconnects (Kill button or inactivity timeout). Replaces the player message so
    /// its controls can't be clicked against a player that no longer exists. With a static channel the idle card returns
    /// in place; otherwise the message becomes a stopped card and the next /play posts a fresh player.</summary>
    public async Task HandlePlayerDestroyedAsync(ulong guildId)
    {
        StopProgressTimer(guildId);
        SemaphoreSlim updateLock = _updateLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await updateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            IUserMessage? message = stateManager.GetMessage(guildId);
            if (message is null) return;

            MessageComponent cv2;
            FileAttachment? banner = null;
            if (stateManager.UseStaticChannel)
            {
                ComponentBuilder buttons = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, new ButtonContext());
                banner = IdleBanner();
                cv2 = ComponentV2Builder.BuildIdlePlayer(buttons, banner is not null);
            }
            else
            {
                cv2 = ComponentV2Builder.BuildStoppedPlayer();
            }

            await message.ModifyAsync(msg =>
            {
                msg.Components = cv2;
                msg.Embed = null;
                msg.Attachments = banner is null ? new List<FileAttachment>() : new[] { banner.Value };
                msg.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);

            if (!stateManager.UseStaticChannel)
                stateManager.SetMessage(guildId, null);
            Logs.Debug($"[guild {guildId}] Player message replaced after disconnect");
        }
        catch (Exception ex)
        {
            Logs.Warning($"[guild {guildId}] Could not update player after disconnect: {ex.Message}");
        }
        finally
        {
            updateLock.Release();
        }
    }

    /// <summary>The banner shown on the idle card, or null when the image isn't installed. Each call returns a new attachment.</summary>
    public static FileAttachment? IdleBanner()
    {
        string? path = AssetPaths.FindFile("Images", ComponentV2Builder.BannerFileName);
        return path is null ? null : new FileAttachment(path, ComponentV2Builder.BannerFileName);
    }

    /// <summary>Stops the progress timer for a guild (call when the player is killed or stopped)</summary>
    public void StopProgressTimer(ulong guildId)
    {
        if (_progressTimers.TryRemove(guildId, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        _progressAnchors.TryRemove(guildId, out _);
    }

    /// <summary>Starts the background progress bar update loop for a guild, replacing any previous loop</summary>
    private void StartProgressTimer(ulong guildId)
    {
        StopProgressTimer(guildId);
        CancellationTokenSource cts = new();
        _progressTimers[guildId] = cts;
        _ = RunProgressUpdateLoop(guildId, cts.Token);
    }

    /// <summary>Periodically updates the status line with current track progress for a guild</summary>
    private async Task RunProgressUpdateLoop(ulong guildId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ProgressInterval, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                IUserMessage? message = stateManager.GetMessage(guildId);
                if (message == null) continue;

                var player = await audioService.Players.GetPlayerAsync(guildId).ConfigureAwait(false) as CustomLavaLinkPlayer;
                if (player == null || player.State == PlayerState.NotPlaying || player.State == PlayerState.Destroyed)
                {
                    StopProgressTimer(guildId);
                    return;
                }

                // Skip update if paused (position isn't moving)
                if (player.State == PlayerState.Paused) continue;

                try
                {
                    ButtonContext context = new() { Player = player };
                    ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
                    string statusLine = BuildStatusLine(guildId, player);

                    MessageComponent cv2 = stateManager.UseModernPlayer
                        ? ComponentV2Builder.BuildModernPlayer(statusLine, components)
                        : BuildClassicCV2(player, statusLine, components);

                    await message.ModifyAsync(msg =>
                    {
                        msg.Components = cv2;
                        msg.Embed = null;
                        msg.Flags = MessageFlags.ComponentsV2;
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logs.Debug($"[guild {guildId}] Progress update skipped: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logs.Debug($"[guild {guildId}] Progress timer stopped: {ex.Message}");
        }
    }

    /// <summary>Builds the status line for a guild. Lavalink reports position only every few seconds, so
    /// between reports the position is extrapolated from a monotonic clock while the track is playing.</summary>
    private string BuildStatusLine(ulong guildId, CustomLavaLinkPlayer? player)
    {
        TimeSpan? position = DisplayPosition(guildId, player);
        TimeSpan? duration = player?.CurrentTrack?.Duration;
        return ComponentV2Builder.BuildPlayerStatusLine(
            player?.State ?? PlayerState.NotPlaying,
            position,
            duration,
            player is null ? null : (int)Math.Round(player.Volume * 100),
            player?.RepeatMode ?? TrackRepeatMode.None);
    }

    private TimeSpan? DisplayPosition(ulong guildId, CustomLavaLinkPlayer? player)
    {
        if (player is null) return null;

        TimeSpan? reported = player.Position?.Position;
        if (reported is null) return null;

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        bool playing = player.State == PlayerState.Playing;
        ProgressAnchor anchor = _progressAnchors.AddOrUpdate(guildId,
            _ => new ProgressAnchor(reported.Value, now, player.CurrentItem, playing, reported.Value),
            (_, existing) =>
            {
                // A new Lavalink report, or a new track, re-anchors the clock to what Lavalink says
                if (existing.Track != player.CurrentItem || existing.Reported != reported.Value)
                    return new ProgressAnchor(reported.Value, now, player.CurrentItem, playing, reported.Value);

                // Pause or resume with no new report: freeze the estimate so far and restart the clock from it.
                // Compared against the raw report, not the estimate, so an extrapolated position is not reset.
                if (existing.Playing != playing)
                {
                    TimeSpan estimateNow = existing.Playing
                        ? existing.Position + System.Diagnostics.Stopwatch.GetElapsedTime(existing.Timestamp, now)
                        : existing.Position;
                    return new ProgressAnchor(estimateNow, now, existing.Track, playing, existing.Reported);
                }
                return existing;
            });

        if (!playing)
            return anchor.Position;

        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(anchor.Timestamp, now);
        TimeSpan estimate = anchor.Position + elapsed;
        TimeSpan? duration = player.CurrentTrack?.Duration;
        return duration is { } total && estimate > total ? total : estimate;
    }

    private static MessageComponent BuildClassicCV2(CustomLavaLinkPlayer? player, string? statusLine, ComponentBuilder buttons)
    {
        CustomTrackQueueItem? currentTrack = player?.CurrentItem as CustomTrackQueueItem;
        string trackInfo = currentTrack != null
            ? $"**▶️ Now Playing**\n{currentTrack.Artist ?? "Unknown Artist"} - {currentTrack.Title ?? "Unknown Title"}\n" +
              $"{currentTrack.Album ?? "Unknown Album"} | {currentTrack.Duration ?? "0:00"}"
            : "**No track playing**";
        return ComponentV2Builder.BuildClassicPlayer(trackInfo, currentTrack?.Artwork, statusLine, buttons);
    }

    public void Dispose()
    {
        foreach (ulong guildId in _progressTimers.Keys.ToList())
            StopProgressTimer(guildId);
        GC.SuppressFinalize(this);
    }

    /// <summary>Last position Lavalink reported for a guild, and when this process first saw it (monotonic)</summary>
    private sealed record ProgressAnchor(TimeSpan Position, long Timestamp, object? Track, bool Playing, TimeSpan Reported);
}
