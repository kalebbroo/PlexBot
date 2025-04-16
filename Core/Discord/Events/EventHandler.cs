using PlexBot.Utils;
using PlexBot.Core.Discord.Embeds;

namespace PlexBot.Core.Discord.Events;

/// <summary>Handles Discord events and interaction routing for slash commands and interactive components</summary>
/// <remarks>Initializes a new instance of the DiscordEventHandler class with necessary dependencies</remarks>
/// <param name="client">The Discord client for connecting to Discord's API</param>
/// <param name="interactions">The interaction service for handling slash commands</param>
/// <param name="services">The service provider for dependency injection</param>
public class DiscordEventHandler(DiscordSocketClient client, InteractionService interactions, IServiceProvider services)
{
    private readonly DiscordSocketClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly InteractionService _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>Initializes Discord event handlers for logging, ready events, and interactions</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    public Task InitializeAsync()
    {
        // Set up logging
        _client.Log += LogAsync;
        _interactions.Log += LogAsync;

        // Set up ready event
        _client.Ready += ReadyAsync;

        // Set up interaction created event
        _client.InteractionCreated += HandleInteractionAsync;

        Logs.Init("Discord event handlers initialized");

        return Task.CompletedTask;
    }

    /// <summary>Handles the client ready event by registering slash commands and setting the bot's status</summary>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task ReadyAsync()
    {
        try
        {
            // Register modules first
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            // Log discovered modules
            var modules = _interactions.Modules.ToList();
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
            if (EnvConfig.Get("ENVIRONMENT") == "Development")
            {
                // Register to specific test guild if needed
                // ulong testGuildId = 123456789012345678;
                // await _interactions.RegisterCommandsToGuildAsync(testGuildId);

                // Or register to all current guilds
                foreach (SocketGuild guild in _client.Guilds)
                {
                    try
                    {
                        await _interactions.RegisterCommandsToGuildAsync(guild.Id);
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
                await _interactions.RegisterCommandsGloballyAsync();
                Logs.Info("Registered commands globally");
            }

            // Set bot status
            await _client.SetGameAsync("/help", type: ActivityType.Listening);

            Logs.Init($"Bot is ready. Connected to {_client.Guilds.Count} guilds");
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
            // Create an execution context for the interaction
            SocketInteractionContext context = new(_client, interaction);

            // Execute the interaction handler
            IResult result = await _interactions.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
            {
                Logs.Warning($"Interaction failed: {result.Error} - {result.ErrorReason}");

                // Create a standardized error embed using our utility
                var errorEmbed = result.Error.HasValue 
                    ? DiscordEmbedBuilder.CommandError(result.Error.Value, result.ErrorReason)
                    : DiscordEmbedBuilder.Error("Command Error", result.ErrorReason);
                
                // Respond with the error embed
                if (!interaction.HasResponded)
                {
                    await interaction.RespondAsync(embed: errorEmbed, ephemeral: true);
                }
                else
                {
                    await interaction.FollowupAsync(embed: errorEmbed, ephemeral: true);
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling interaction: {ex.Message}");

            // Create a standardized error embed for exceptions
            var exceptionEmbed = DiscordEmbedBuilder.Error("Command Error", 
                "An unexpected error occurred while processing your command. Please try again later.");

            // Try to respond with an error message if we haven't already
            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync(embed: exceptionEmbed, ephemeral: true);
            }
            else
            {
                await interaction.FollowupAsync(embed: exceptionEmbed, ephemeral: true);
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