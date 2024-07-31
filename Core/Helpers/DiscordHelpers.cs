namespace PlexBot.Core.Helpers;

public static class DiscordHelpers
{
    internal static async Task ClientReady(IServiceProvider serviceProvider)
    {
        ILogger<Program> logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            DiscordSocketClient client = serviceProvider.GetRequiredService<DiscordSocketClient>();
            InteractionService interactions = serviceProvider.GetRequiredService<InteractionService>();

            await interactions!.RegisterCommandsGloballyAsync(true); // Clear all global commands DEBUG
                                                                     // Things to be run when the bot is ready
            if (client!.Guilds.Count != 0)
            {
                // Register command modules with the InteractionService.
                // Scans the whole assembly for classes that define slash commands.
                await interactions!.AddModulesAsync(Assembly.GetEntryAssembly(), serviceProvider);
                foreach (SocketGuild? guild in client.Guilds)
                {
                    await interactions.RegisterCommandsToGuildAsync(guild.Id, true);
                }
            }
            else
            {
                logger?.LogWarning("No guilds found");
            }

            logger?.LogInformation("Logged in as {Username}", client.CurrentUser.Username);
            logger?.LogInformation("Registered {Count} slash commands", interactions!.SlashCommands.Count);
            logger?.LogInformation("Bot is a member of {Count} guilds", client.Guilds.Count);
            await client.SetGameAsync("/help", null, ActivityType.Listening);
        }
        catch (Exception ex)
        {
            logger.LogError("{Exception}", ex.Message);
            throw;
        }
    }

    /// <summary>Logs a message to the console and Serilog.</summary>
    /// <param name="message">The LogMessage object to log.</param>
    internal static async Task LogMessageAsync(LogMessage message)
    {
        LogEventLevel severity = message.Severity switch
        {
            LogSeverity.Critical => LogEventLevel.Fatal,
            LogSeverity.Error => LogEventLevel.Error,
            LogSeverity.Warning => LogEventLevel.Warning,
            LogSeverity.Info => LogEventLevel.Information,
            LogSeverity.Verbose => LogEventLevel.Verbose,
            LogSeverity.Debug => LogEventLevel.Debug,
            _ => LogEventLevel.Information
        };
        Log.Write(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        await Task.CompletedTask;
    }

    internal static Func<Task> ReadyAsync(IServiceProvider services)
    {
        throw new NotImplementedException();
    }
}
