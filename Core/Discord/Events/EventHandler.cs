using PlexBot.Utils;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Events;
using PlexBot.Core.Extensions;

namespace PlexBot.Core.Discord.Events;

/// <summary>Handles Discord events and interaction routing for slash commands and interactive components</summary>
/// <remarks>Initializes a new instance of the DiscordEventHandler class with necessary dependencies</remarks>
/// <param name="client">The Discord client for connecting to Discord's API</param>
/// <param name="interactions">The interaction service for handling slash commands</param>
/// <param name="services">The service provider for dependency injection</param>
public class DiscordEventHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
{
    private bool _modulesRegistered;

    /// <summary>Initializes Discord event handlers for logging, ready events, and interactions</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task InitializeAsync()
    {
        // Set up logging
        client.Log += LogAsync;
        interactions.Log += LogAsync;

        // Set up ready event
        client.Ready += ReadyAsync;

        // Set up interaction created event
        client.InteractionCreated += HandleInteractionAsync;

        Logs.Init("Discord event handlers initialized");

        return Task.CompletedTask;
    }

    /// <summary>Handles the client ready event by registering slash commands and setting the bot's status</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task ReadyAsync()
    {
        try
        {
            // Only register modules once — ReadyAsync fires on every reconnect
            // but AddModulesAsync would create duplicate handlers
            if (!_modulesRegistered)
            {
                // Register modules from main assembly
                await interactions.AddModulesAsync(Assembly.GetEntryAssembly(), services);

                // Register modules from extension assemblies
                ExtensionManager extensionManager = services.GetRequiredService<ExtensionManager>();
                foreach (Extension ext in extensionManager.GetAllExtensions())
                {
                    if (ext.SourceAssembly != null && ext.SourceAssembly != Assembly.GetEntryAssembly())
                    {
                        await interactions.AddModulesAsync(ext.SourceAssembly, services);
                        Logs.Info($"Registered commands from extension: {ext.Name}");
                    }
                }

                _modulesRegistered = true;
            }

            // Log discovered modules
            var modules = interactions.Modules.ToList();
            Logs.Info($"Discovered {modules.Count} interaction modules");
            foreach (ModuleInfo module in modules)
            {
                Logs.Info($"Module: {module.Name}, Commands: {module.SlashCommands.Count}");
                foreach (SlashCommandInfo cmd in module.SlashCommands)
                {
                    Logs.Info($"  Command: {cmd.Name}");
                }
            }

            // In development, use guild commands (faster updates)
            if (BotConfig.GetString("bot.environment") == "Development")
            {
                // Register to specific test guild if needed
                // ulong testGuildId = 123456789012345678;
                // await interactions.RegisterCommandsToGuildAsync(testGuildId);

                // Or register to all current guilds
                foreach (SocketGuild guild in client.Guilds)
                {
                    try
                    {
                        await interactions.RegisterCommandsToGuildAsync(guild.Id);
                        Logs.Info($"Registered commands to guild: {guild.Name} ({guild.Id})");
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Failed to register commands for guild {guild.Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                // In production, use global commands
                await interactions.RegisterCommandsGloballyAsync();
                Logs.Info("Registered commands globally");
            }

            // Set bot status
            await client.SetGameAsync("/help", type: ActivityType.Listening);

            Logs.Init($"Bot is ready. Connected to {client.Guilds.Count} guilds");

            // Publish bot ready event for extensions
            BotEventBus eventBus = services.GetRequiredService<BotEventBus>();
            _ = eventBus.PublishAsync(new BotEvent
            {
                EventType = BotEvents.BotReady,
                Data = new Dictionary<string, object>
                {
                    ["guildCount"] = client.Guilds.Count
                }
            });
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in ReadyAsync: {ex.Message}");
            Logs.Error($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>Routes incoming interactions to appropriate handlers and manages error responses</summary>
    /// <param name="interaction">The interaction to handle from Discord</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Log how much of the 3-second interaction deadline has already elapsed
            TimeSpan elapsed = DateTimeOffset.UtcNow - interaction.CreatedAt;
            Logs.Debug($"Interaction received: type={interaction.Type}, elapsed={elapsed.TotalMilliseconds:F0}ms since creation");

            // Create an execution context for the interaction
            SocketInteractionContext context = new(client, interaction);

            // Execute the interaction handler
            IResult result = await interactions.ExecuteCommandAsync(context, services);

            if (!result.IsSuccess)
            {
                Logs.Warning($"Interaction failed: {result.Error} - {result.ErrorReason}");

                // Create a standardized error using our CV2 utility
                var errorComponents = result.Error.HasValue
                    ? ComponentV2Builder.CommandError(result.Error.Value, result.ErrorReason)
                    : ComponentV2Builder.Error("Command Error", result.ErrorReason);

                // Respond with the error
                if (!interaction.HasResponded)
                {
                    await interaction.RespondAsync(components: errorComponents, ephemeral: true);
                }
                else
                {
                    await interaction.FollowupAsync(components: errorComponents, ephemeral: true);
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling interaction: {ex.Message}");

            // Create a standardized error for exceptions
            var exceptionComponents = ComponentV2Builder.Error("Command Error",
                "An unexpected error occurred while processing your command. Please try again later.");

            // Try to respond with an error message if we haven't already
            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync(components: exceptionComponents, ephemeral: true);
            }
            else
            {
                await interaction.FollowupAsync(components: exceptionComponents, ephemeral: true);
            }
        }
    }

    /// <summary>Processes Discord client log events and routes them to the application's logging system</summary>
    /// <param name="message">The log message from Discord</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private Task LogAsync(LogMessage message)
    {
        // Map Discord log severity to our log levels
        switch (message.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                Logs.Error($"[Discord] {message.Source}: {message.Message} {message.Exception}");
                break;

            case LogSeverity.Warning:
                Logs.Warning($"[Discord] {message.Source}: {message.Message}");
                break;

            case LogSeverity.Info:
                Logs.Info($"[Discord] {message.Source}: {message.Message}");
                break;

            case LogSeverity.Verbose:
            case LogSeverity.Debug:
                Logs.Debug($"[Discord] {message.Source}: {message.Message}");
                break;
        }

        return Task.CompletedTask;
    }
}