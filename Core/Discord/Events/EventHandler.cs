using PlexBot.Utils;

namespace PlexBot.Core.Discord.Events;

/// <summary>
/// Handles Discord events and interaction routing.
/// This class is responsible for registering, setting up, and managing
/// Discord event handlers, including slash commands and interactive components.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EventHandler"/> class.
/// Sets up the event handler with necessary dependencies.
/// </remarks>
/// <param name="client">The Discord client</param>
/// <param name="interactions">The interaction service</param>
/// <param name="services">The service provider</param>
public class EventHandler(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services)
{
    private readonly DiscordSocketClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly InteractionService _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Initializes Discord event handlers.
    /// Sets up event handlers for client log events, ready events, and interaction events.
    /// </summary>
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

    /// <summary>
    /// Handles the client ready event.
    /// Registers slash commands and sets up the bot's status.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task ReadyAsync()
    {
        try
        {
            // In development, uncomment this to register commands to a specific test guild
            // If in production, uncomment this to register commands globally
            // await _interactions.RegisterCommandsGloballyAsync();

            // Register commands to all current guilds
            foreach (var guild in _client.Guilds)
            {
                await _interactions.RegisterCommandsToGuildAsync(guild.Id);
                Logs.Info($"Registered commands to guild: {guild.Name} ({guild.Id})");
            }

            // Register modules
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

            // Set bot status
            await _client.SetGameAsync("/help", type: ActivityType.Listening);

            Logs.Init($"Bot is ready. Connected to {_client.Guilds.Count} guilds");
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in ReadyAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles incoming interactions.
    /// Routes interactions to the appropriate handler based on their type.
    /// </summary>
    /// <param name="interaction">The interaction to handle</param>
    /// <returns>A task representing the asynchronous operation</returns>
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Create an execution context for the interaction
            var context = new SocketInteractionContext(_client, interaction);

            // Execute the interaction handler
            var result = await _interactions.ExecuteCommandAsync(context, _services);

            if (!result.IsSuccess)
            {
                Logs.Warning($"Interaction failed: {result.Error} - {result.ErrorReason}");

                // Handle specific error cases
                switch (result.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        await interaction.RespondAsync($"You don't have permission to use this command: {result.ErrorReason}", ephemeral: true);
                        break;

                    case InteractionCommandError.UnknownCommand:
                        await interaction.RespondAsync("This command is not recognized. It may have been removed or updated.", ephemeral: true);
                        break;

                    case InteractionCommandError.BadArgs:
                        await interaction.RespondAsync("Invalid command arguments. Please check your input and try again.", ephemeral: true);
                        break;

                    case InteractionCommandError.Exception:
                        await interaction.RespondAsync("An error occurred while processing your command. Please try again later.", ephemeral: true);
                        break;

                    default:
                        await interaction.RespondAsync("An unknown error occurred. Please try again later.", ephemeral: true);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error handling interaction: {ex.Message}");

            // Try to respond with an error message if we haven't already
            if (!interaction.HasResponded)
            {
                await interaction.RespondAsync("An error occurred while processing your command. Please try again later.", ephemeral: true);
            }
        }
    }

    /// <summary>
    /// Handles Discord client log events.
    /// Routes log messages to the application's logging system.
    /// </summary>
    /// <param name="message">The log message</param>
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