using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Events;
using PlexBot.Core.Extensions;
using PlexBot.Core.Models.Extensions;
using PlexBot.Core.Models.Players;
using PlexBot.Services.LavaLink;
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
    IServiceProvider serviceProvider) : IHostedService
{
    private static readonly DiscordButtonBuilder _buttonBuilder = DiscordButtonBuilder.Instance;
    private readonly string _discordToken = EnvConfig.Get("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is not set");

    /// <summary>Starts the bot service by initializing event handlers, loading extensions, and establishing connection to Discord and Lavalink</summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests to safely abort startup operations</param>
    /// <returns>A task representing the asynchronous startup operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logs.Init("Starting bot service");
            // Initialize event handlers
            await eventHandler.InitializeAsync();
            ServiceCollection serviceDescriptors = new();
            // Initialize Lavalink services
            IAudioService lavalinkNode = serviceProvider.GetRequiredService<IAudioService>();
            await lavalinkNode.StartAsync(cancellationToken);
            Logs.Init("Lavalink services initialized");
            // Initialize extension handler
            string extensionsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Extensions");
            Logs.Info($"Loading extensions from {extensionsDir}");
            // Load user extensions from the Extensions directory
            int extensionsLoaded = await ExtensionHandler.LoadAllExtensionsAsync(serviceDescriptors, serviceProvider);
            Logs.Info($"Loaded {extensionsLoaded} extensions");
            // Connect to Discord and start the bot
            Logs.Init("Connecting to Discord");
            await client.LoginAsync(TokenType.Bot, _discordToken);
            await client.StartAsync();
            Logs.Init("Bot service started");
            await InitializeStaticPlayerChannelAsync();
        }
        catch (Exception ex)
        {
            Logs.Error($"Error starting bot service: {ex.Message}");
            throw;
        }
    }

    /// <summary>Initializes the static player channel if enabled in configuration</summary>
    /// <returns>A task representing the initialization operation</returns>
    private async Task InitializeStaticPlayerChannelAsync()
    {
        VisualPlayerStateManager stateManager = serviceProvider.GetRequiredService<VisualPlayerStateManager>();
        // Early return if static channel is not configured
        if (!stateManager.UseStaticChannel || !stateManager.StaticChannelId.HasValue)
        {
            Logs.Debug("Static player channel is not configured or invalid, skipping initialization");
            return;
        }
        try
        {
            ulong staticChannelId = stateManager.StaticChannelId.Value;
            Logs.Init($"Initializing static player channel ({staticChannelId})...");
            // Wait a moment for the client to be fully ready
            await Task.Delay(2000);
            // Get the channel from the client
            if (client.GetChannel(staticChannelId) is not ITextChannel textChannel)
            {
                Logs.Warning($"Static player channel with ID {staticChannelId} not found or is not a text channel");
                return;
            }
            IGuildUser currentUser = await textChannel.Guild.GetCurrentUserAsync();
            ChannelPermissions permissions = currentUser.GetPermissions(textChannel);
            if (!permissions.SendMessages || !permissions.EmbedLinks || !permissions.AttachFiles)
            {
                Logs.Warning($"Bot lacks required permissions in static player channel {staticChannelId}");
                return;
            }
            stateManager.CurrentPlayerChannel = textChannel; // Set the current player channel
            Logs.Info("Cleaning up static player channel...");
            var messages = await textChannel.GetMessagesAsync(50).FlattenAsync();
            List<IMessage> botMessages = messages.Where(m => m.Author.Id == client.CurrentUser.Id).ToList();
            foreach (IMessage message in botMessages)
            {
                try
                {
                    await message.DeleteAsync();
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to delete message: {ex.Message}");
                }
            }
            // TODO: Create a dynamic embed system that can be overridden by extensions
            Embed embed = new EmbedBuilder()
                .WithTitle("🎵 PlexBot Music Player")
                .WithDescription("No track is currently playing. Use a `/play` command in any channel to start playing music!")
                .WithColor(new Discord.Color(138, 43, 226))
                .WithFooter("The player will appear here when music begins playing")
                .WithCurrentTimestamp()
                .Build();
            IUserMessage infoMessage = await textChannel.SendMessageAsync(embed: embed);
            ButtonContext context = new() { CustomData = new Dictionary<string, object> { { "message", infoMessage } } };
            ComponentBuilder components = _buttonBuilder.BuildButtons(ButtonFlag.VisualPlayer, context);
            // TODO: Send the initial player message with a placeholder image
            IUserMessage initPlayer = await textChannel.SendMessageAsync("test", components: components.Build());
            stateManager.CurrentPlayerMessage = initPlayer;

            Logs.Init($"Static player channel initialized successfully");
        }
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
            // Disconnect from Discord
            await client.StopAsync();
            await client.LogoutAsync();
            // Unload extensions
            await extensionManager.UnloadAllExtensionsAsync();
            Logs.Info("Bot service stopped");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error stopping bot service: {ex.Message}");
        }
    }
}