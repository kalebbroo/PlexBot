using PlexBot.Core.Discord.Events;
using PlexBot.Core.Extensions;
using PlexBot.Core.Models.Extensions;
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
    private readonly DiscordSocketClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly DiscordEventHandler _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
    private readonly ExtensionManager _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            await _eventHandler.InitializeAsync();
            ServiceCollection serviceDescriptors = new();
            // Initialize Lavalink services - ADD THIS PART
            IAudioService lavalinkNode = _serviceProvider.GetRequiredService<IAudioService>();
            await lavalinkNode.StartAsync(cancellationToken);
            Logs.Init("Lavalink services initialized");
            // Initialize extension handler
            string extensionsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Extensions");
            Logs.Info($"Loading extensions from {extensionsDir}");
            // Load user extensions from the Extensions directory
            int extensionsLoaded = await ExtensionHandler.LoadAllExtensionsAsync(serviceDescriptors, _serviceProvider);
            Logs.Info($"Loaded {extensionsLoaded} extensions");
            // Connect to Discord and start the bot
            Logs.Init("Connecting to Discord");
            await _client.LoginAsync(TokenType.Bot, _discordToken);
            await _client.StartAsync();
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
        bool useStaticChannel = EnvConfig.GetBool("USE_STATIC_PLAYER_CHANNEL", false);
        string staticChannelIdStr = EnvConfig.Get("STATIC_PLAYER_CHANNEL_ID", "");
        if (!useStaticChannel || string.IsNullOrEmpty(staticChannelIdStr) || !ulong.TryParse(staticChannelIdStr, out ulong staticChannelId))
        {
            Logs.Debug("Static player channel is not configured or invalid, skipping initialization");
            return;
        }
        try
        {
            Logs.Init($"Initializing static player channel ({staticChannelId})...");
            // Wait a moment for the client to be fully ready
            await Task.Delay(2000);
            // Get the channel from the client
            if (_client.GetChannel(staticChannelId) is not ITextChannel textChannel)
            {
                Logs.Warning($"Static player channel with ID {staticChannelId} not found or is not a text channel");
                return;
            }
            // Check channel permissions
            IGuildUser currentUser = await textChannel.Guild.GetCurrentUserAsync();
            ChannelPermissions permissions = currentUser.GetPermissions(textChannel);
            if (!permissions.SendMessages || !permissions.EmbedLinks || !permissions.AttachFiles)
            {
                Logs.Warning($"Bot lacks required permissions in static player channel {staticChannelId}");
                return;
            }
            IMessage existingMessage = null;
            var messages = await textChannel.GetMessagesAsync(20).FlattenAsync();
            foreach (IMessage message in messages)
            {
                // Check if message is from the bot and has a player embed
                if (message.Author.Id == _client.CurrentUser.Id && message.Embeds.Count > 0)
                {
                    IEmbed embeds = message.Embeds.First();
                    existingMessage = message;
                    Logs.Debug("Found existing player message in static channel");
                    break;
                }
            }
            // Only create a new placeholder if no existing message was found
            if (existingMessage == null)
            {
                // TODO: Make this look better and more informative. Create button systemn for all the slash commands
                Embed embed = new EmbedBuilder()
                    .WithTitle("🎵 PlexBot Music Player")
                    .WithDescription("No track is currently playing. Use a `/play` command in any channel to start playing music!")
                    .WithColor(new Discord.Color(138, 43, 226)) // Purple
                    .WithFooter("The player will appear here when music begins playing")
                    .WithCurrentTimestamp()
                    .Build();
                await textChannel.SendMessageAsync(embed: embed);
                Logs.Init("Created new placeholder message in static player channel");
            }
            else
            {
                Logs.Init("Using existing message in static player channel");
            }
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
            await _client.StopAsync();
            await _client.LogoutAsync();
            // Unload extensions
            await _extensionManager.UnloadAllExtensionsAsync();
            Logs.Info("Bot service stopped");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error stopping bot service: {ex.Message}");
        }
    }
}