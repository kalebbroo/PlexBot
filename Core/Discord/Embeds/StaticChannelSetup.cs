using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Models.Players;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Embeds;

/// <summary>Sets up each server's static player channel: finds the configured channel, clears the bot's old messages
/// there, and posts the idle card. Runs at startup for every configured channel, and again when the bot joins a server
/// that has one.</summary>
public sealed class StaticChannelSetup(DiscordSocketClient client, VisualPlayerStateManager stateManager,
    DiscordButtonBuilder buttonBuilder, EmojiRegistry emojis)
{
    // Longest the idle card waits for the application emoji sync. Past this it posts with unicode fallbacks and says so.
    private static readonly TimeSpan EmojiSyncWait = TimeSpan.FromSeconds(60);

    // Channels with a setup loop running, so a join and the startup pass never post into the same channel twice
    private readonly ConcurrentDictionary<ulong, byte> _inFlight = new();

    /// <summary>Sets up every configured static channel once the gateway is ready. Each channel has its own retry loop, so
    /// a channel that is slow to appear does not hold up the others.</summary>
    /// <param name="gatewayReady">Completes when the gateway is ready</param>
    /// <param name="ct">Token that ends the retry loops on shutdown</param>
    public async Task RunAtStartupAsync(Task gatewayReady, CancellationToken ct)
    {
        if (!stateManager.UseStaticChannel)
        {
            Logs.Debug("Static player channel is disabled, skipping initialization");
            return;
        }
        // The retry loops only end on success or shutdown, so an empty list has to be rejected before they start.
        if (stateManager.StaticChannelIds.Count == 0)
        {
            Logs.Warning("visualPlayer.staticChannel.enabled is true but no channel is set in visualPlayer.staticChannel.channelId or channelIds; skipping static player channel setup");
            return;
        }

        try
        {
            await gatewayReady.WaitAsync(ct);
            // Let the guild cache fill, so each configured channel's server is known before one channel per server is picked
            await WaitForChannelsAsync(stateManager.StaticChannelIds, TimeSpan.FromSeconds(60), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        List<(ulong ChannelId, ulong? GuildId)> resolved = stateManager.StaticChannelIds
            .Select(id => (id, (client.GetChannel(id) as ITextChannel)?.GuildId))
            .ToList();
        IReadOnlyList<ulong> chosen = FirstPerServer(resolved);
        foreach ((ulong channelId, ulong? guildId) in resolved)
        {
            if (guildId is not null && !chosen.Contains(channelId))
                Logs.Warning($"Static player channel {channelId} is skipped: server {guildId} already has a configured static channel earlier in the list");
        }

        await Task.WhenAll(chosen.Select(channelId => SetupWithRetryAsync(channelId, ct)));
    }

    /// <summary>Picks one channel per server: the first in configuration order. A channel whose server is not known yet is
    /// kept, so its own setup can wait for it.</summary>
    /// <param name="channels">Configured channel ids in order, with their server id, or null while the server is unknown</param>
    /// <returns>The channel ids to set up</returns>
    public static IReadOnlyList<ulong> FirstPerServer(IEnumerable<(ulong ChannelId, ulong? GuildId)> channels)
    {
        HashSet<ulong> servers = new();
        List<ulong> chosen = new();
        foreach ((ulong channelId, ulong? guildId) in channels)
        {
            if (guildId is null)
                chosen.Add(channelId);
            else if (servers.Add(guildId.Value))
                chosen.Add(channelId);
        }
        return chosen;
    }

    /// <summary>Waits until every configured channel is in the cache, or the timeout passes. Whatever is still missing is
    /// left to its own retry loop.</summary>
    private async Task WaitForChannelsAsync(IReadOnlyList<ulong> channelIds, TimeSpan timeout, CancellationToken ct)
    {
        for (int waited = 0; waited < timeout.TotalSeconds; waited++)
        {
            if (channelIds.All(id => client.GetChannel(id) is ITextChannel))
                return;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    /// <summary>Sets up a configured static channel in a server the bot has just joined, so it works without a restart.
    /// Does nothing for a server with no configured channel, or one that is already set up.</summary>
    /// <param name="guild">The server that was joined</param>
    public Task SetupGuildAsync(SocketGuild guild)
    {
        if (!stateManager.UseStaticChannel || stateManager.StaticChannelIdFor(guild.Id) is not null)
            return Task.CompletedTask;

        // The first configured channel that is in this server, in configuration order, so a second one in the same
        // server is never set up alongside it
        IEnumerable<ulong> channelIds = stateManager.StaticChannelIds
            .Where(id => guild.GetTextChannel(id) is not null)
            .Take(1);
        return Task.WhenAll(channelIds.Select(channelId => SetupWithRetryAsync(channelId, CancellationToken.None)));
    }

    /// <summary>Tries setup for one channel until it succeeds, with capped backoff. A channel that already has a loop running
    /// is left to that loop.</summary>
    private async Task SetupWithRetryAsync(ulong staticChannelId, CancellationToken ct)
    {
        if (!_inFlight.TryAdd(staticChannelId, 0))
            return;

        try
        {
            TimeSpan delay = TimeSpan.FromSeconds(5);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (await TrySetupAsync(staticChannelId, ct))
                        return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to initialize static player channel {staticChannelId}: {ex.Message}");
                }

                Logs.Warning($"Static player channel {staticChannelId} not ready; retrying in {delay.TotalSeconds:N0}s");
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
            }
        }
        finally
        {
            _inFlight.TryRemove(staticChannelId, out _);
        }
    }

    /// <summary>One attempt at static channel setup. Returns false if the channel is not in the cache yet or the bot
    /// lacks permissions there, so the caller can retry.</summary>
    private async Task<bool> TrySetupAsync(ulong staticChannelId, CancellationToken ct)
    {
        Logs.Init($"Initializing static player channel ({staticChannelId})...");

        // Guild data can arrive after Ready; poll the cache for up to 60 seconds per attempt
        ITextChannel? textChannel = null;
        for (int i = 0; i < 60 && textChannel is null && !ct.IsCancellationRequested; i++)
        {
            textChannel = client.GetChannel(staticChannelId) as ITextChannel;
            if (textChannel is null) await Task.Delay(1000, ct);
        }
        if (textChannel is null)
        {
            Logs.Warning($"Static player channel {staticChannelId} not found in the cache or is not a text channel");
            return false;
        }

        IGuildUser currentUser = await textChannel.Guild.GetCurrentUserAsync();
        ChannelPermissions permissions = currentUser.GetPermissions(textChannel);
        if (!permissions.SendMessages || !permissions.EmbedLinks || !permissions.AttachFiles)
        {
            Logs.Warning($"Bot lacks required permissions in static player channel {staticChannelId}");
            return false;
        }

        ulong guildId = textChannel.Guild.Id;
        if (!stateManager.TryClaimStaticChannel(guildId, staticChannelId))
        {
            Logs.Warning($"Static player channel {staticChannelId} is not used: server {guildId} already has another static player channel. Use one per server.");
            return true;
        }
        stateManager.SetChannel(guildId, textChannel);

        // The idle card keeps the buttons it was built with until the next player event, so it is built after the
        // application emoji have synced. A card posted first kept unicode fallbacks for the rest of the session.
        if (!await emojis.WaitForSyncAsync(EmojiSyncWait, ct))
        {
            Logs.Warning($"Application emoji still syncing after {EmojiSyncWait.TotalSeconds:N0}s; the idle card uses unicode fallbacks until its next update");
        }

        Logs.Info("Cleaning up static player channel...");
        var messages = await textChannel.GetMessagesAsync(50).FlattenAsync();
        List<IMessage> botMessages = messages.Where(m => m.Author.Id == client.CurrentUser.Id).ToList();
        foreach (IMessage message in botMessages)
        {
            try
            {
                await message.DeleteAsync();
                Logs.Debug($"Deleted message: {message.Id}");
                await Task.Delay(100, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logs.Warning($"Failed to delete message: {ex.Message}");
            }
        }

        ButtonContext context = new();
        ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.Idle, context);
        FileAttachment? banner = VisualPlayer.IdleBanner();
        MessageComponent cv2 = ComponentV2Builder.BuildIdlePlayer(components, banner is not null);
        IUserMessage initPlayer = banner is null
            ? await textChannel.SendMessageAsync(components: cv2)
            : await textChannel.SendFileAsync(banner.Value, components: cv2);
        stateManager.SetMessage(guildId, initPlayer);
        // Published once the card exists, so a concurrent update cannot post into the static channel before it
        stateManager.SetStaticChannel(textChannel);
        Logs.Init($"Static player channel initialized successfully (message {initPlayer.Id})");
        return true;
    }

}
