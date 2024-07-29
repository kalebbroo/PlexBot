namespace PlexBot.Core.Helpers;

public static class DiscordHelpers
{
    internal static Func<Task> ClientReady(IServiceProvider services)
    {
        throw new NotImplementedException();
    }

    /// <summary>Logs a message to the console and Serilog.</summary>
    /// <param name="message">The LogMessage object to log.</param>
    internal static async Task LogMessageAsync(LogMessage message)
    {
        var severity = message.Severity switch
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

    /// <summary>Executes tasks when the bot client is ready, such as command registration and initialization. 
    /// It registers commands to guilds and sets the bot status.</summary>
    /// <returns>A Task representing the asynchronous operation.</returns>
    internal static async Task ReadyAsync(IServiceProvider serviceProvider)
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
                foreach (var guild in client.Guilds)
                {
                    await interactions.RegisterCommandsToGuildAsync(guild.Id, true);
                }
            }
            else
            {
                logger?.LogWarning("No guilds found");
            }

            StringBuilder sb = new StringBuilder()
                .AppendLine("Logged in as " + client.CurrentUser.Username)
                .AppendLine("Registered " + interactions!.SlashCommands.Count + " slash commands")
                .AppendLine("Bot is a member of " + client.Guilds.Count + " guilds");
            logger?.LogInformation("{ReadyMessage}", sb.ToString());

            await client.SetGameAsync("/help", null, ActivityType.Listening);
        }
        catch (Exception ex)
        {
            logger.LogError("{Exception}" , ex.Message);
            throw;
        }
    }
}