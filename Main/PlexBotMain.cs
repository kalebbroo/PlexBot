using PlexBot.Utils;

namespace PlexBot.Main;

/// <summary>Main application class that handles bot initialization, configuration loading, and service startup</summary>
public class PlexBotMain
{
    /// <summary>Application entry point. Sets up logging configuration, initializes environment variables, and manages the application lifecycle</summary>
    /// <param name="args">Command line arguments passed to the application</param>
    /// <returns>A task representing the asynchronous operation of the entire bot runtime</returns>
    public static async Task Main(string[] args)
    {
        try
        {
            // Initialize configuration
            EnvConfig.Initialize();

            // Initialize logging
            Dictionary<string, string> logSettings = new()
            {
                ["SaveToFile"] = EnvConfig.Get("LOG_SAVE_TO_FILE", "true"), // TODO: Add these to the ENV
                ["LogPath"] = EnvConfig.Get("LOG_PATH", "logs/plex-bot-[year]-[month]-[day].log")
            };

            Logs.StartLogSaving(logSettings);

            string logLevel = EnvConfig.Get("LOG_LEVEL", "Debug");
            Logs.MinimumLevel = logLevel.ToLowerInvariant() switch
            {
                "verbose" => Logs.LogLevel.Verbose,
                "debug" => Logs.LogLevel.Debug,
                "info" => Logs.LogLevel.Info,
                "init" => Logs.LogLevel.Init,
                "warning" => Logs.LogLevel.Warning,
                "error" => Logs.LogLevel.Error,
                _ => Logs.LogLevel.Info
            };

            Logs.Init($"Starting Plex Music Bot");

            // Ensure the application directory is set correctly
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // Create and run the host
            using IHost host = CreateHostBuilder(args).Build();

            Logs.Init("Host built, starting services");

            // Start the host
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Logs.Error($"Fatal error: {ex.Message}");
            Logs.Error(ex.StackTrace ?? "No stack trace available");
        }
        finally
        {
            // Ensure logs are saved and resources are cleaned up
            Logs.Info("Shutting down");
            // Wait a moment for logs to flush
            await Task.Delay(1000);
        }
    }

    /// <summary>Creates and configures the host builder with necessary services and dependency injection for the bot to function</summary>
    /// <param name="args">Command line arguments to pass to the host builder</param>
    /// <returns>A configured host builder ready to start all bot services</returns>
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                // Register services
                services.AddServices();

                // Add the main hosted service
                services.AddHostedService<BotHostedService>();
            });
}