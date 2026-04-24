using System.Collections.Concurrent;
using Discord.Net;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.PlexApi;
using PlexBot.Utils;
using Discord.WebSocket;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Services;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Core.Services.Music;
using PlexBot.Core.Discord.Modals;
using PlexBot.Core.Models;

namespace PlexBot.Core.Discord.Interactions;

/// <summary>Handles interactive components for the music player.
/// This module processes button clicks, select menu choices, and other
/// interactive elements of the music player interface.</summary>
public class MusicInteractionHandler(IPlayerService playerService,
    VisualPlayer visualPlayer, DiscordButtonBuilder buttonBuilder,
    MusicProviderRegistry providerRegistry, IPlexSonicService plexSonicService,
    RadioSessionManager radioSessionManager, IPlexMusicService plexMusicService) : InteractionModuleBase<SocketInteractionContext>
{

    // Cooldown tracking to prevent spamming
    private static readonly ConcurrentDictionary<(ulong UserId, string CommandId), DateTime> _lastInteracted = new();
    private static readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(2);

    /// <summary>Routes select menu choices to the correct handler by decoding the provider ID and content type
    /// from the custom ID pattern search:{providerId}:{type}</summary>
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
            if (provider is null)
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
                case "radio_station":
                    await HandleRadioStationTrackSelectionAsync(selectedKey);
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

    /// <summary>Toggles between playing and paused states, with the button emoji
    /// swapping dynamically via the DiscordButtonBuilder context</summary>
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
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling pause/resume: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Playback Error", "An error occurred while toggling playback. Please try again later."), ephemeral: true);
        }
    }

    /// <summary>Advances to the next track in queue, triggering the player's TrackEnded event chain</summary>
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

    /// <summary>"options" creates a new ephemeral panel from the main player; all other actions
    /// (view, shuffle, clear, pagination) edit that same panel in-place via DEFERRED_UPDATE_MESSAGE</summary>
    [ComponentInteraction("queue_options:*:*")]
    public async Task HandleQueueOptionsAsync(string action, string pageStr)
    {
        string normalizedAction = action.ToLowerInvariant();
        bool isInitialOpen = normalizedAction == "options";

        if (isInitialOpen)
            await DeferAsync(ephemeral: true);
        else
            await DeferAsync(); // DEFERRED_UPDATE_MESSAGE — edits the ephemeral panel

        if (IsOnCooldown(Context.User.Id, $"queue_options:{action}"))
        {
            if (isInitialOpen)
                await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
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

    /// <summary>Adjusts volume in 10% steps and triggers a Visual Player image rebuild
    /// to reflect the new volume level in the overlay bar</summary>
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

    /// <summary>Cycles through None → Queue → Track → None, updating the button emoji
    /// to indicate the active mode</summary>
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

    /// <summary>Stops playback, disconnects from voice, clears any active radio session,
    /// and halts the progress bar timer</summary>
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
            if (Context.Guild is not null)
                radioSessionManager.StopSession(Context.Guild.Id);

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

    /// <summary>Extracts the rating key from the currently playing Plex track and opens an ephemeral
    /// panel with Replace Queue / Add to Queue / Similar Tracks options</summary>
    [ComponentInteraction("radio:start")]
    public async Task HandleRadioStartAsync()
    {
        await DeferAsync(ephemeral: true);
        if (IsOnCooldown(Context.User.Id, "radio:start"))
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
            if (player.CurrentItem is not CustomTrackQueueItem currentItem)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Track", "No track is currently playing."), ephemeral: true);
                return;
            }
            if (!currentItem.SourceTrack.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Not Supported", "Radio is only available for Plex tracks."), ephemeral: true);
                return;
            }
            string ratingKey = PlexJsonParser.ExtractRatingKey(currentItem.SourceTrack.SourceKey);
            if (string.IsNullOrEmpty(ratingKey))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Error", "Could not determine track key for radio."), ephemeral: true);
                return;
            }

            ComponentBuilder actionButtons = new();
            actionButtons.WithButton("Replace Queue", $"radio:replace:{ratingKey}", ButtonStyle.Primary);
            actionButtons.WithButton("Add to Queue", $"radio:append:{ratingKey}", ButtonStyle.Success);
            actionButtons.WithButton("Similar Tracks", $"radio:similar:{ratingKey}", ButtonStyle.Secondary);

            await FollowupAsync(components: ComponentV2Builder.BuildRadioOptions(
                currentItem.SourceTrack.Title ?? "Unknown",
                currentItem.SourceTrack.Artist ?? "Unknown",
                actionButtons), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling radio start: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Radio Error", "An error occurred while opening radio options."), ephemeral: true);
        }
    }

    /// <summary>Clears the existing queue, generates radio tracks from the seed, and starts
    /// a radio session for potential infinite refill based on config</summary>
    [ComponentInteraction("radio:replace:*")]
    public async Task HandleRadioReplaceAsync(string ratingKey)
    {
        await DeferAsync(); // Update the ephemeral panel
        try
        {
            int batchSize = BotConfig.GetInt("plex.radio.batchSize", 30);
            List<Track> tracks = await plexSonicService.GetRadioTracksAsync(ratingKey, batchSize);
            if (tracks.Count == 0)
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Warning("No Tracks", "No radio tracks were returned. This track may not have sonic analysis data.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return;
            }

            if (await playerService.GetPlayerAsync(Context.Interaction, false) is CustomLavaLinkPlayer player)
                await player.Queue.ClearAsync();

            await playerService.AddToQueueAsync(Context.Interaction, tracks);

            if (Context.Guild is not null)
                radioSessionManager.StartSession(Context.Guild.Id, ratingKey);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Components = ComponentV2Builder.Success("Radio Started", $"Replaced queue with {tracks.Count} radio tracks.");
                msg.Embed = null;
                msg.Flags = MessageFlags.ComponentsV2;
            });
            Logs.Info($"Radio started (replace) by {Context.User.Username}: {tracks.Count} tracks from key {ratingKey}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling radio replace: {ex.Message}");
            try
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Radio Error", "Failed to start radio. Please try again.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Appends radio tracks to the existing queue without clearing, and starts
    /// a radio session for potential infinite refill based on config</summary>
    [ComponentInteraction("radio:append:*")]
    public async Task HandleRadioAppendAsync(string ratingKey)
    {
        await DeferAsync(); // Update the ephemeral panel
        try
        {
            int batchSize = BotConfig.GetInt("plex.radio.batchSize", 30);
            List<Track> tracks = await plexSonicService.GetRadioTracksAsync(ratingKey, batchSize);
            if (tracks.Count == 0)
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Warning("No Tracks", "No radio tracks were returned. This track may not have sonic analysis data.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return;
            }

            await playerService.AddToQueueAsync(Context.Interaction, tracks);

            if (Context.Guild is not null)
                radioSessionManager.StartSession(Context.Guild.Id, ratingKey);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Components = ComponentV2Builder.Success("Radio Tracks Added", $"Added {tracks.Count} radio tracks to the queue.");
                msg.Embed = null;
                msg.Flags = MessageFlags.ComponentsV2;
            });
            Logs.Info($"Radio started (append) by {Context.User.Username}: {tracks.Count} tracks from key {ratingKey}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling radio append: {ex.Message}");
            try
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Radio Error", "Failed to add radio tracks. Please try again.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Fetches sonically similar tracks and replaces the ephemeral panel with a track select menu
    /// that reuses the search:plex:track interaction pattern for seamless queueing</summary>
    [ComponentInteraction("radio:similar:*")]
    public async Task HandleRadioSimilarAsync(string ratingKey)
    {
        await DeferAsync(); // Update the ephemeral panel
        try
        {
            List<Track> similarTracks = await plexSonicService.GetSimilarTracksAsync(ratingKey, 25);
            if (similarTracks.Count == 0)
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Warning("No Similar Tracks", "No sonically similar tracks were found for this track.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
                return;
            }

            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithCustomId("search:plex:track")
                .WithPlaceholder("Select a similar track to play")
                .WithMaxValues(1);

            foreach (Track track in similarTracks.Take(25))
            {
                string label = track.Title?.Length > 100 ? track.Title[..97] + "..." : track.Title ?? "Unknown";
                string desc = $"{track.Artist} - {track.Album}";
                if (desc.Length > 100) desc = desc[..97] + "...";
                selectMenu.AddOption(label, track.SourceKey, desc);
            }

            ComponentBuilder components = new();
            components.WithSelectMenu(selectMenu);

            components.WithButton("Play All Similar", $"sonic:playall:plex:{ratingKey}", ButtonStyle.Success, row: 1);

            await Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Components = ComponentV2Builder.BuildSonicResults(
                    "Similar Tracks",
                    $"Found {similarTracks.Count} sonically similar tracks.",
                    components);
                msg.Embed = null;
                msg.Flags = MessageFlags.ComponentsV2;
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling similar tracks: {ex.Message}");
            try
            {
                await Context.Interaction.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Components = ComponentV2Builder.Error("Error", "Failed to find similar tracks. Please try again.");
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                });
            }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Visual Player button — reads the currently playing Plex track and directly shows
    /// sonically similar tracks in an ephemeral select menu</summary>
    [ComponentInteraction("sonic:similar")]
    public async Task HandleSonicSimilarButtonAsync()
    {
        await DeferAsync(ephemeral: true);
        if (IsOnCooldown(Context.User.Id, "sonic:similar"))
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Cooldown", "Please wait a moment before clicking again."), ephemeral: true);
            return;
        }
        try
        {
            if (await playerService.GetPlayerAsync(Context.Interaction, false) is not CustomLavaLinkPlayer player
                || player.CurrentItem is not CustomTrackQueueItem currentItem)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Track", "No track is currently playing."), ephemeral: true);
                return;
            }
            if (!currentItem.SourceTrack.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Not Supported", "Similar tracks is only available for Plex tracks."), ephemeral: true);
                return;
            }
            string ratingKey = PlexJsonParser.ExtractRatingKey(currentItem.SourceTrack.SourceKey);
            if (string.IsNullOrEmpty(ratingKey))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Error", "Could not determine track key."), ephemeral: true);
                return;
            }

            List<Track> similarTracks = await plexSonicService.GetSimilarTracksAsync(ratingKey, 25);
            if (similarTracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Similar Tracks", $"No sonically similar tracks found for '{currentItem.SourceTrack.Title}'."), ephemeral: true);
                return;
            }

            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithCustomId("search:plex:track")
                .WithPlaceholder("Select a similar track to play")
                .WithMaxValues(1);

            foreach (Track track in similarTracks.Take(25))
            {
                string label = track.Title?.Length > 100 ? track.Title[..97] + "..." : track.Title ?? "Unknown";
                string desc = $"{track.Artist} - {track.Album}";
                if (desc.Length > 100) desc = desc[..97] + "...";
                selectMenu.AddOption(label, track.SourceKey, desc);
            }

            ComponentBuilder components = new();
            components.WithSelectMenu(selectMenu);
            components.WithButton("Play All Similar", $"sonic:playall:plex:{ratingKey}", ButtonStyle.Success, row: 1);

            await FollowupAsync(components: ComponentV2Builder.BuildSonicResults(
                $"Similar to: {currentItem.SourceTrack.Title}",
                $"Found {similarTracks.Count} sonically similar tracks to **{currentItem.SourceTrack.Artist}** - {currentItem.SourceTrack.Title}",
                components), ephemeral: true);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling similar button: {ex.Message}");
            await FollowupAsync(components: ComponentV2Builder.Error("Error", "Failed to find similar tracks."), ephemeral: true);
        }
    }

    /// <summary>Visual Player button — opens a modal for the user to enter a destination track name
    /// for a Sonic Adventure path from the currently playing track</summary>
    [ComponentInteraction("sonic:adventure")]
    public async Task HandleSonicAdventureButtonAsync()
    {
        try
        {
            if (await playerService.GetPlayerAsync(Context.Interaction, false) is not CustomLavaLinkPlayer player
                || player.CurrentItem is not CustomTrackQueueItem currentItem
                || !currentItem.SourceTrack.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase))
            {
                await RespondAsync(components: ComponentV2Builder.Error("Not Available", "Play a Plex track first to use Sonic Adventure."), ephemeral: true);
                return;
            }

            await Context.Interaction.RespondWithModalAsync<SonicAdventureModal>("sonic:adventure_modal");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error opening adventure modal: {ex.Message}");
            try { await RespondAsync(components: ComponentV2Builder.Error("Error", "Failed to open adventure dialog."), ephemeral: true); }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Processes the Sonic Adventure modal submission — takes the destination track name,
    /// searches the library, and builds a sonic path from the currently playing track</summary>
    [ModalInteraction("sonic:adventure_modal")]
    public async Task HandleSonicAdventureModalAsync(SonicAdventureModal modal)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            if (await playerService.GetPlayerAsync(Context.Interaction, false) is not CustomLavaLinkPlayer player
                || player.CurrentItem is not CustomTrackQueueItem currentItem
                || !currentItem.SourceTrack.SourceSystem.Equals("plex", StringComparison.OrdinalIgnoreCase))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Start Track", "The Plex track you were playing has stopped. Play a Plex track and try again."), ephemeral: true);
                return;
            }

            string startRatingKey = PlexJsonParser.ExtractRatingKey(currentItem.SourceTrack.SourceKey);
            if (string.IsNullOrEmpty(startRatingKey))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Error", "Could not identify the currently playing track."), ephemeral: true);
                return;
            }

            SearchResults searchResults = await plexMusicService.SearchLibraryAsync(modal.Destination);
            if (searchResults.Tracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Error("No Results", $"No tracks found for '{modal.Destination}'."), ephemeral: true);
                return;
            }

            Track endTrack = searchResults.Tracks.First();
            string endRatingKey = PlexJsonParser.ExtractRatingKey(endTrack.SourceKey);
            if (string.IsNullOrEmpty(endRatingKey))
            {
                await FollowupAsync(components: ComponentV2Builder.Error("Error", "Could not extract destination track identifier."), ephemeral: true);
                return;
            }

            List<Track> adventureTracks = await plexSonicService.GetSonicAdventureAsync(startRatingKey, endRatingKey);
            if (adventureTracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Info("No Path Found", $"Could not find a sonic path from '{currentItem.SourceTrack.Title}' to '{endTrack.Title}'."), ephemeral: true);
                return;
            }

            SelectMenuBuilder selectMenu = new SelectMenuBuilder()
                .WithCustomId("search:plex:track")
                .WithPlaceholder("Select a track from the adventure path")
                .WithMaxValues(1);

            foreach (Track track in adventureTracks.Take(25))
            {
                string label = track.Title?.Length > 100 ? track.Title[..97] + "..." : track.Title ?? "Unknown";
                string desc = $"{track.Artist} - {track.Album}";
                if (desc.Length > 100) desc = desc[..97] + "...";
                selectMenu.AddOption(label, track.SourceKey, desc);
            }

            ComponentBuilder components = new();
            components.WithSelectMenu(selectMenu);
            components.WithButton("Play All", $"sonic:playall:adventure:{startRatingKey}-{endRatingKey}", ButtonStyle.Success, row: 1);

            await FollowupAsync(components: ComponentV2Builder.BuildSonicResults(
                "Sonic Adventure",
                $"Path from **{currentItem.SourceTrack.Artist}** - {currentItem.SourceTrack.Title} to **{endTrack.Artist}** - {endTrack.Title} ({adventureTracks.Count} tracks)",
                components), ephemeral: true);

            Logs.Info($"Sonic adventure by {Context.User.Username}: {currentItem.SourceTrack.Title} → {endTrack.Title}, {adventureTracks.Count} tracks");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling adventure modal: {ex.Message}");
            try { await FollowupAsync(components: ComponentV2Builder.Error("Error", "Failed to build sonic adventure path."), ephemeral: true); }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Queues all tracks from a sonic result set. Routes to similar tracks or adventure path
    /// based on the provider ID encoded in the custom ID</summary>
    [ComponentInteraction("sonic:playall:*:*")]
    public async Task HandleSonicPlayAllAsync(string sonicType, string keyData)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            List<Track> tracks;
            if (sonicType.Equals("adventure", StringComparison.OrdinalIgnoreCase))
            {
                // keyData format: "startKey-endKey"
                string[] keys = keyData.Split('-', 2);
                if (keys.Length != 2)
                {
                    await FollowupAsync(components: ComponentV2Builder.Error("Error", "Invalid adventure path data."), ephemeral: true);
                    return;
                }
                tracks = await plexSonicService.GetSonicAdventureAsync(keys[0], keys[1]);
            }
            else
            {
                tracks = await plexSonicService.GetSimilarTracksAsync(keyData, 50);
            }

            if (tracks.Count == 0)
            {
                await FollowupAsync(components: ComponentV2Builder.Warning("No Tracks", "No tracks found to play."), ephemeral: true);
                return;
            }

            await playerService.AddToQueueAsync(Context.Interaction, tracks);
            await FollowupAsync(components: ComponentV2Builder.Success("Tracks Added", $"Added {tracks.Count} tracks to the queue."), ephemeral: true);
            Logs.Info($"Sonic play all ({sonicType}) by {Context.User.Username}: {tracks.Count} tracks");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling sonic play all: {ex.Message}");
            try { await FollowupAsync(components: ComponentV2Builder.Error("Error", "Failed to add tracks. Please try again."), ephemeral: true); }
            catch { /* interaction may be dead */ }
        }
    }

    /// <summary>Generates radio tracks from a station key selected via the search:plex:radio_station
    /// select menu, routed here through the search:*:* wildcard handler's radio_station case</summary>
    public async Task HandleRadioStationTrackSelectionAsync(string stationKey)
    {
        int batchSize = BotConfig.GetInt("plex.radio.batchSize", 30);
        List<Track> tracks = await plexSonicService.GetRadioTracksAsync(stationKey, batchSize);
        if (tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Warning("No Tracks", "No tracks returned for this station."), ephemeral: true);
            return;
        }

        await playerService.AddToQueueAsync(Context.Interaction, tracks);

        // Start radio session for potential infinite refill
        if (Context.Guild is not null)
            radioSessionManager.StartSession(Context.Guild.Id, stationKey);

        await FollowupAsync(components: ComponentV2Builder.Success("Station Playing", $"Added {tracks.Count} tracks from the radio station."), ephemeral: true);
        Logs.Info($"Radio station selected by {Context.User.Username}: {stationKey}, {tracks.Count} tracks");
    }

    /// <summary>Fetches full track details from the provider and queues it for playback</summary>
    public async Task HandleTrackSelectionAsync(IMusicProvider provider, string trackKey)
    {
        Track? track = await provider.GetTrackDetailsAsync(trackKey);
        if (track is null)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Track Error", "Failed to retrieve track details."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, [track]);
    }

    /// <summary>Loads all tracks from the selected album via the provider and queues them</summary>
    public async Task HandleAlbumSelectionAsync(IMusicProvider provider, string albumKey)
    {
        List<Track> tracks = await provider.GetTracksAsync(albumKey);
        if (tracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Empty Album", "The selected album has no tracks."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, tracks);
    }

    /// <summary>Fetches the full discography for the selected artist and queues all tracks</summary>
    public async Task HandleArtistSelectionAsync(IMusicProvider provider, string artistKey)
    {
        List<Track> allTracks = await provider.GetAllArtistTracksAsync(artistKey);
        if (allTracks.Count == 0)
        {
            await FollowupAsync(components: ComponentV2Builder.Error("Empty Artist", "The selected artist has no tracks."), ephemeral: true);
            return;
        }
        await playerService.AddToQueueAsync(Context.Interaction, allTracks);
    }

    /// <summary>Updates the existing ephemeral queue panel in-place with paginated track listing,
    /// keeping the same message so users don't get spammed with new panels</summary>
    public async Task ShowQueueAsync(CustomLavaLinkPlayer player, int page)
    {
        try
        {
            CustomTrackQueueItem? currentTrack = player.CurrentItem as CustomTrackQueueItem;
            List<CustomTrackQueueItem?> queue = player.Queue.Select(item => item as CustomTrackQueueItem).ToList();

            const int itemsPerPage = 10;
            int totalTracks = queue.Count;
            int totalPages = Math.Max(1, (totalTracks + itemsPerPage - 1) / itemsPerPage);
            page = Math.Clamp(page, 1, totalPages);

            string? nowPlayingLine = (page == 1 && currentTrack is not null)
                ? $"**\u25B6\uFE0F Now Playing:** {currentTrack.Title} - {currentTrack.Artist} ({currentTrack.Duration})"
                : null;

            int startIndex = (page - 1) * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, totalTracks);
            StringBuilder queueSb = new();
            for (int i = startIndex; i < endIndex; i++)
            {
                CustomTrackQueueItem? item = queue[i];
                if (item is not null)
                    queueSb.AppendLine($"**#{i + 1}:** {item.Title} - {item.Artist} ({item.Duration})");
            }
            string queueText = queueSb.ToString().TrimEnd();

            if (totalTracks == 0 && currentTrack is null)
                queueText = "The queue is currently empty.";

            string footerLine = $"Page {page} of {totalPages} | {totalTracks} tracks queued";

            ComponentBuilder components = new();
            if (totalPages > 1)
            {
                components.WithButton("Previous", $"queue_options:view:{Math.Max(1, page - 1)}",
                                   ButtonStyle.Secondary, disabled: page <= 1);
                components.WithButton("Next", $"queue_options:view:{Math.Min(totalPages, page + 1)}",
                                   ButtonStyle.Secondary, disabled: page >= totalPages);
            }

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

    /// <summary>Returns true if the user has interacted with this command within the cooldown window,
    /// auto-pruning stale entries when the dictionary exceeds 100 items to prevent unbounded growth</summary>
    public static bool IsOnCooldown(ulong userId, string commandId)
    {
        (ulong, string) key = (userId, commandId);
        DateTime now = DateTime.UtcNow;

        if (_lastInteracted.Count > 100)
        {
            foreach ((ulong, string) staleKey in _lastInteracted
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