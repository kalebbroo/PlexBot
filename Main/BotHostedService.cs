using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Events;
using PlexBot.Core.Extensions;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;
using PlexBot.Utils.Http;

namespace PlexBot.Main;

/// <summary>Main hosted service for the bot application.
/// Manages the bot's lifecycle, including startup, extension loading,
/// connection to Discord, and graceful shutdown.</summary>
/// <remarks>Initializes a new instance of the <see cref="BotHostedService"/> class.
/// Sets up the hosted service with necessary dependencies.</remarks>
/// <param name="client">The Discord client</param>
/// <param name="eventHandler">The Discord event handler</param>
/// <param name="extensionManager">The extension manager</param>
/// <param name="serviceProvider">The service provider</param>
public class BotHostedService(DiscordSocketClient client, DiscordEventHandler eventHandler, ExtensionManager extensionManager,
    IServiceProvider serviceProvider, DiscordButtonBuilder buttonBuilder) : IHostedService
{
    private readonly string _discordToken = EnvConfig.Get("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is not set");

    private readonly TaskCompletionSource _gatewayReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _connectCts;
    private Task? _connectTask;

    /// <summary>Starts the bot service by initializing event handlers, loading extensions, and connecting to Discord and Lavalink.
    /// Discord login runs in the background and retries with backoff, so a temporary outage does not end the process.</summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests to safely abort startup operations</param>
    /// <returns>A task representing the asynchronous startup operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logs.Init("Starting bot service");
            client.Ready += () =>
            {
                _gatewayReady.TrySetResult();
                return Task.CompletedTask;
            };

            await eventHandler.InitializeAsync();

            IAudioService lavalinkNode = serviceProvider.GetRequiredService<IAudioService>();
            await RetryAsync("Lavalink", () => lavalinkNode.StartAsync(cancellationToken).AsTask(), cancellationToken);
            Logs.Init("Lavalink services initialized");

            int extensionsLoaded = await extensionManager.InitializeAllAsync(serviceProvider);
            Logs.Info($"Initialized {extensionsLoaded} extensions");

            MusicProviderRegistry providerRegistry = serviceProvider.GetRequiredService<MusicProviderRegistry>();
            foreach (IMusicProvider provider in serviceProvider.GetServices<IMusicProvider>())
            {
                providerRegistry.RegisterProvider(provider);
            }
            Logs.Init($"Registered {providerRegistry.GetAvailableProviders().Count} music providers");

            _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _connectTask = Task.Run(() => ConnectToDiscordAsync(_connectCts.Token));
            _ = _connectTask.ContinueWith(t => Logs.Error($"Discord connect loop ended: {t.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);

            _ = Task.Run(() => InitializeStaticPlayerChannelAsync(_connectCts.Token));
        }
        catch (Exception ex)
        {
            Logs.Error($"Error starting bot service: {ex.Message}");
            throw;
        }
    }

    /// <summary>Logs in and starts the Discord gateway. Retries with exponential backoff (capped at one minute)
    /// until it succeeds or the service stops.</summary>
    private async Task ConnectToDiscordAsync(CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.FromSeconds(5);
        for (int attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                Logs.Init($"Connecting to Discord (attempt {attempt})");
                await client.LoginAsync(TokenType.Bot, _discordToken);
                await client.StartAsync();
                Logs.Init("Bot service started");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logs.Error($"Discord login failed (attempt {attempt}): {ex.Message}. Retrying in {delay.TotalSeconds:N0}s");
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    /// <summary>Runs an async operation, retrying with backoff on failure. Used for Lavalink startup.</summary>
    private static async Task RetryAsync(string name, Func<Task> operation, CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.FromSeconds(3);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt < 10 && !ct.IsCancellationRequested)
            {
                Logs.Warning($"{name} startup failed (attempt {attempt}): {ex.Message}. Retrying in {delay.TotalSeconds:N0}s");
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    /// <summary>Sets up the static player channel once the gateway is ready. Waits for the gateway (not a fixed delay),
    /// then waits for the channel to appear in the cache, since guilds arrive after Ready.</summary>
    private async Task InitializeStaticPlayerChannelAsync(CancellationToken ct)
    {
        VisualPlayerStateManager stateManager = serviceProvider.GetRequiredService<VisualPlayerStateManager>();
        if (!stateManager.UseStaticChannel || !stateManager.StaticChannelId.HasValue)
        {
            Logs.Debug("Static player channel is not configured or invalid, skipping initialization");
            return;
        }
        try
        {
            ulong staticChannelId = stateManager.StaticChannelId.Value;
            Logs.Init($"Initializing static player channel ({staticChannelId})...");

            await _gatewayReady.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);

            // Guild data can arrive after Ready; poll the cache for up to 60 seconds
            ITextChannel? textChannel = null;
            for (int i = 0; i < 60 && textChannel is null && !ct.IsCancellationRequested; i++)
            {
                textChannel = client.GetChannel(staticChannelId) as ITextChannel;
                if (textChannel is null) await Task.Delay(1000, ct);
            }
            if (textChannel is null)
            {
                Logs.Warning($"Static player channel {staticChannelId} not found in the cache or is not a text channel");
                return;
            }

            IGuildUser currentUser = await textChannel.Guild.GetCurrentUserAsync();
            ChannelPermissions permissions = currentUser.GetPermissions(textChannel);
            if (!permissions.SendMessages || !permissions.EmbedLinks || !permissions.AttachFiles)
            {
                Logs.Warning($"Bot lacks required permissions in static player channel {staticChannelId}");
                return;
            }

            ulong guildId = textChannel.Guild.Id;
            stateManager.SetChannel(guildId, textChannel);

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
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to delete message: {ex.Message}");
                }
            }

            ButtonContext context = new();
            ComponentBuilder components = buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
            MessageComponent cv2 = ComponentV2Builder.BuildIdlePlayer(components);
            IUserMessage initPlayer = await textChannel.SendMessageAsync(components: cv2);
            stateManager.SetMessage(guildId, initPlayer);
            Logs.Init($"Static player channel initialized successfully (message {initPlayer.Id})");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logs.Error($"Failed to initialize static player channel: {ex.Message}");
        }
    }

    /// <summary>Gracefully shuts down the bot by disconnecting from Discord, unloading extensions, and releasing resources to prevent any data corruption</summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests to ensure timely shutdown</param>
    /// <returns>A task representing the asynchronous shutdown operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logs.Info("Stopping bot service");
            _connectCts?.Cancel();
            await client.StopAsync();
            await client.LogoutAsync();
            await extensionManager.UnloadAllExtensionsAsync();
            Logs.Info("Bot service stopped");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error stopping bot service: {ex.Message}");
        }
    }
}
