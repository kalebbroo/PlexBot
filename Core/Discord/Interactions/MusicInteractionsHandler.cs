using System.Collections.Concurrent;
using Discord.Net;
using PlexBot.Core.Models.Media;
using PlexBot.Utils;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Services;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Core.Services.Music;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>Handles interactive components for the music player.
/// This module processes button clicks, select menu choices, and other
/// interactive elements of the music player interface.</summary>
/// <remarks>Initializes a new instance of the <see cref="MusicInteractionHandler"/> class.
/// Sets up the interaction handler with necessary services.</remarks>
/// <param name="plexMusicService">Service for interacting with Plex music</param>
/// <param name="playerService">Service for managing audio playback</param>
/// <param name="audioService">Service for managing audio playback</param>
public class MusicInteractionHandler(IPlayerService playerService,
    VisualPlayer visualPlayer, DiscordButtonBuilder buttonBuilder,
    MusicProviderRegistry providerRegistry) : InteractionModuleBase<SocketInteractionContext>
{

    // Cooldown tracking to prevent spamming
    private static readonly ConcurrentDictionary<(ulong UserId, string CommandId), DateTime> _lastInteracted = new();
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(2);

    /// <summary>Handles search result selection from search command menu.
    /// Routes through the music provider registry based on the encoded provider ID.</summary>
    /// <param name="providerId">The music provider that produced these results</param>
    /// <param name="type">The type of content selected (artist, album, track)</param>
    /// <param name="values">The selected content IDs</param>
    [ComponentInteraction("search:*:*")]
    public async Task HandleSearchSelectionAsync(string providerId, string type, string[] values)
    {
        try
        {
            await DeferAsync(ephemeral: true);
            if (IsOnCooldown(Context.User.Id, $"search:{providerId}:{type}"))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before selecting another item."), ephemeral: true);
                return;
            }

            if (values.Length == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Selection", "No selection made."), ephemeral: true);
                return;
            }
            string selectedKey = values[0];
            Logs.Debug($"Search selection: provider={providerId}, type={type}, key={selectedKey}");

            IMusicProvider? provider = providerRegistry.GetProvider(providerId);
            if (provider == null)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Unknown Source", $"Music source '{providerId}' is no longer available."), ephemeral: true);
                return;
            }

            switch (type.ToLowerInvariant())
            {
                case "track":
                    await HandleTrackSelectionAsync(provider, selectedKey);
                    break;
                case "album":
                    await HandleAlbumSelectionAsync(provider, selectedKey);
                    break;
                case "artist":
                    await HandleArtistSelectionAsync(provider, selectedKey);
                    break;
                default:
                    await FollowupAsync(components: ComponentV2Builder.Error("Unknown Type", $"Unrecognized selection type: {type}"), ephemeral: true);
                    break;
            }
        }
        catch (HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.UnknownInteraction)
        {
            Logs.Warning($"Search selection hit 10062 (Unknown interaction) — Discord-side timing issue");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling search selection: {ex.Message}");
            try { await FollowupAsync(components: ComponentV2Builder.Error("Error", "An error occurred while processing your selection. Please try again later."), ephemeral: true); }
            catch { /* interaction may be dead */ }
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
    /// "options" creates a new ephemeral panel; all other actions (view, shuffle, clear, pagination)
    /// update that same panel in-place so the user sees a single coherent message.</summary>
    /// <param name="action">The specific queue action (options, view, etc.)</param>
    /// <param name="pageStr">The current page number (for pagination)</param>
    [ComponentInteraction("queue_options:*:*")]
    public async Task HandleQueueOptionsAsync(string action, string pageStr)
    {
        string normalizedAction = action.ToLowerInvariant();
        bool isInitialOpen = normalizedAction == "options";

        // "options" creates a new ephemeral panel from the main player;
        // all other actions update the existing ephemeral panel in-place
        if (isInitialOpen)
            await DeferAsync(ephemeral: true);
        else
            await DeferAsync(); // DEFERRED_UPDATE_MESSAGE — edits the ephemeral panel

        if (IsOnCooldown(Context.User.Id, $"queue_options:{action}"))
        {
            if (isInitialOpen)
                await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            // For updates, silently ignore the cooldown (don't replace the panel with an error)
            return;
        }
        SocketInteraction interaction = Context.Interaction;
        try
        {
            if (await playerService.GetPlayerAsync(interaction, false) is not CustomLavaLinkPlayer player)
            {
                MessageComponent errorMsg = ComponentV2Builder.Error("No Player", "No active player found.");
                if (isInitialOpen)
                    await FollowupAsync(components: errorMsg, ephemeral: true);
                else
                    await interaction.ModifyOriginalResponseAsync(msg => { msg.Components = errorMsg; msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                return;
            }
            ButtonContext context = new()
            {
                Player = player,
                Interaction = interaction
            };
            int currentPage = int.TryParse(pageStr, out int page) ? page : 1;
            switch (normalizedAction)
            {
                case "options":
                    // Send new ephemeral queue management panel
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
                    await interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Components = ComponentV2Builder.Success("Queue Shuffled", $"Shuffled {countBefore} tracks.");
                        msg.Embed = null;
                        msg.Flags = MessageFlags.ComponentsV2;
                    });
                    break;
                case "clear":
                    int cleared = player.Queue.Count;
                    await player.Queue.ClearAsync();
                    await interaction.ModifyOriginalResponseAsync(msg =>
                    {
                        msg.Components = ComponentV2Builder.Success("Queue Cleared", $"Removed {cleared} tracks from the queue.");
                        msg.Embed = null;
                        msg.Flags = MessageFlags.ComponentsV2;
                    });
                    break;
                default:
                    MessageComponent unknownMsg = ComponentV2Builder.Error("Unknown Action", $"Unrecognized queue action: {action}");
                    if (isInitialOpen)
                        await FollowupAsync(components: unknownMsg, ephemeral: true);
                    else
                        await interaction.ModifyOriginalResponseAsync(msg => { msg.Components = unknownMsg; msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
                    break;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling queue options: {ex.Message}");
            MessageComponent errorMsg = ComponentV2Builder.Error("Queue Error", "An error occurred while managing the queue. Please try again later.");
            try
            {
                if (isInitialOpen)
                    await FollowupAsync(components: errorMsg, ephemeral: true);
                else
                    await interaction.ModifyOriginalResponseAsync(msg => { msg.Components = errorMsg; msg.Embed = null; msg.Flags = MessageFlags.ComponentsV2; });
            }
            catch { /* Ignore if the error response itself fails */ }
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

    /// <summary>Handles track selection from search results using the appropriate provider</summary>
    private async Task HandleTrackSelectionAsync(IMusicProvider provider, string trackKey)
    {
        Track? track = await provider.GetTrackDetailsAsync(trackKey);
        if (track == null)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Track Error", "Failed to retrieve track details."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, [track]);
    }

    /// <summary>Handles album selection from search results using the appropriate provider</summary>
    private async Task HandleAlbumSelectionAsync(IMusicProvider provider, string albumKey)
    {
        List<Track> tracks = await provider.GetTracksAsync(albumKey);
        if (tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Empty Album", "The selected album has no tracks."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, tracks);
    }

    /// <summary>Handles artist selection from search results using the appropriate provider</summary>
    private async Task HandleArtistSelectionAsync(IMusicProvider provider, string artistKey)
    {
        List<Track> allTracks = await provider.GetAllArtistTracksAsync(artistKey);
        if (allTracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Empty Artist", "The selected artist has no tracks."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, allTracks);
    }

    /// <summary>Shows the current queue by updating the existing ephemeral panel in-place.
    /// Displays the currently playing track and upcoming tracks with pagination.</summary>
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

            // Update the existing ephemeral panel in-place
            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Components = ComponentV2Builder.BuildQueueDisplay(
                    nowPlayingLine, queueText, footerLine, components);
                msg.Embed = null;
                msg.Flags = MessageFlags.ComponentsV2;
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"Error showing queue: {ex.Message}");
            try
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Queue Error", "An error occurred while showing the queue.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            catch { /* Ignore if the error response itself fails */ }
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
        DateTime now = DateTime.UtcNow;

        // Prune stale entries periodically to prevent unbounded growth
        if (_lastInteracted.Count > 100)
        {
            foreach (var staleKey in _lastInteracted
                .Where(kvp => now - kvp.Value > TimeSpan.FromMinutes(5))
                .Select(kvp => kvp.Key)
                .ToList())
            {
                _lastInteracted.TryRemove(staleKey, out _);
            }
        }

        if (_lastInteracted.TryGetValue(key, out DateTime lastTime))
        {
            if (now - lastTime < _cooldownPeriod)
            {
                return true;
            }
        }
        _lastInteracted[key] = now;
        return false;
    }
}