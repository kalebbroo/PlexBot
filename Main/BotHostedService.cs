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
            string extensionsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "extensions");
            Logs.Info($"Loading extensions from {extensionsDir}");
            // Load extensions
            int extensionsLoaded = await ExtensionHandler.LoadAllExtensionsAsync(serviceDescriptors, _serviceProvider);
            Logs.Info($"Loaded {extensionsLoaded} extensions");
            // Connect to Discord
            Logs.Init("Connecting to Discord");
            await _client.LoginAsync(TokenType.Bot, _discordToken);
            await _client.StartAsync();
            Logs.Init("Bot service started");
            Logs.Init($"Testing connection to Lavalink server...");
            try
            {
                // Create a temporary HttpClientWrapper for the Lavalink connection test
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);
                HttpClientWrapper httpClientWrapper = new(httpClient, "LavalinkTest");
                string lavalinkUrl = $"http://{EnvConfig.Get("LAVALINK_HOST")}:{EnvConfig.Get("SERVER_PORT")}/version";
                string response = await httpClientWrapper.GetAsync<string>(lavalinkUrl);
                Logs.Init($"Lavalink connection test successful");
            }
            catch (Exception ex)
            {
                Logs.Error($"Lavalink connection test failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error starting bot service: {ex.Message}");
            throw;
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