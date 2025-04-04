using PlexBot.Utils;

namespace PlexBot.Main;

/// <summary>
/// Main entry point for the application.
/// Configures and starts the host, initializes logging, and handles startup.
/// </summary>
public class PlexBotMain
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static async Task Main(string[] args)
    {
        try
        {
            // Initialize configuration
            EnvConfig.Initialize();

            // Initialize logging
            Dictionary<string, string> logSettings = new Dictionary<string, string>
            {
                ["SaveToFile"] = EnvConfig.Get("LOG_SAVE_TO_FILE", "true"),
                ["LogPath"] = EnvConfig.Get("LOG_PATH", "logs/plex-bot-[year]-[month]-[day].log")
            };

            Logs.StartLogSaving(logSettings);

            string logLevel = EnvConfig.Get("LOG_LEVEL", "Info");
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

    /// <summary>
    /// Creates the host builder for the application.
    /// Configures services, logging, and DI for the application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>A configured host builder</returns>
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