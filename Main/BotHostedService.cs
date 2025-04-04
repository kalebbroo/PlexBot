﻿using PlexBot.Core.Extensions;
using PlexBot.Core.Models.Extensions;
using PlexBot.Utils;

namespace PlexBot.Main;

/// <summary>
/// Main hosted service for the bot application.
/// Manages the bot's lifecycle, including startup, extension loading,
/// connection to Discord, and graceful shutdown.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BotHostedService"/> class.
/// Sets up the hosted service with necessary dependencies.
/// </remarks>
/// <param name="client">The Discord client</param>
/// <param name="eventHandler">The Discord event handler</param>
/// <param name="extensionManager">The extension manager</param>
/// <param name="serviceProvider">The service provider</param>
public class BotHostedService(
    DiscordSocketClient client,
    EventHandler eventHandler,
    ExtensionManager extensionManager,
    IServiceProvider serviceProvider) : IHostedService
{
    private readonly DiscordSocketClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly EventHandler _eventHandler = eventHandler ?? throw new ArgumentNullException(nameof(eventHandler));
    private readonly ExtensionManager _extensionManager = extensionManager ?? throw new ArgumentNullException(nameof(extensionManager));
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly string _discordToken = Environment.GetEnvironmentVariable("DISCORD_TOKEN")
            ?? throw new InvalidOperationException("DISCORD_TOKEN environment variable is not set");

    /// <summary>
    /// Starts the bot service.
    /// Initializes event handlers, loads extensions, and connects to Discord.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Logs.Init("Starting bot service");

            // Initialize event handlers
            await _eventHandler.InitializeAsync();
            ServiceCollection serviceDescriptors = new();

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
        }
        catch (Exception ex)
        {
            Logs.Error($"Error starting bot service: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Stops the bot service.
    /// Disconnects from Discord, unloads extensions, and performs cleanup.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
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