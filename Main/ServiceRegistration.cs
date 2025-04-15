using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Events;
using PlexBot.Core.Models.Extensions;
using PlexBot.Core.Models.Players;
using PlexBot.Services;
using PlexBot.Services.LavaLink;
using PlexBot.Services.PlexApi;
using PlexBot.Utils;

namespace PlexBot.Main
{
    /// <summary>Extension methods for registering services with the DI container for centralized service configuration</summary>
    public static class ServiceRegistration
    {
        /// <summary>Adds all required services to the service collection for the application's dependency injection</summary>
        /// <param name="services">The service collection to add services to</param>
        /// <returns>The modified service collection</returns>
        public static IServiceCollection AddServices(this IServiceCollection services)
        {
            Logs.Init("Registering services");

            // Add HTTP client factory
            services.AddHttpClient();

            // Add Discord services
            AddDiscordServices(services);

            // Add Plex services
            AddPlexServices(services);

            // Add player services
            AddPlayerServices(services);

            // Add extension services
            AddExtensionServices(services);

            Logs.Init("Services registered");
            return services;
        }

        /// <summary>Configures Discord client, interaction service, and event handlers for bot communication</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddDiscordServices(IServiceCollection services)
        {
            // Configure Discord client
            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 100,
                LogLevel = LogSeverity.Info
            }));

            // Configure interaction service
            services.AddSingleton(provider => new InteractionService(
                provider.GetRequiredService<DiscordSocketClient>(),
                new InteractionServiceConfig
                {
                    DefaultRunMode = RunMode.Async,
                    LogLevel = LogSeverity.Info
                }));
            services.AddSingleton<DiscordEventHandler>();
            services.AddSingleton<VisualPlayer>();
        }

        /// <summary>Registers Plex API services for authentication, data retrieval, and music functionality</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddPlexServices(IServiceCollection services)
        {
            // Add Plex HTTP client
            services.AddHttpClient("PlexApi", client =>
            {
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Add Plex services
            services.AddSingleton<IPlexAuthService, PlexAuthService>();
            services.AddSingleton<IPlexApiService, PlexApiService>();
            services.AddSingleton<IPlexMusicService, PlexMusicService>();
        }

        /// <summary>Configures Lavalink audio streaming services and player management for music playback</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddPlayerServices(IServiceCollection services)
        {
            // Add Lavalink services
            services.AddLavalink();
            services.ConfigureLavalink(options =>
            {
                string password = EnvConfig.Get("LAVALINK_SERVER_PASSWORD", "youshallnotpass");
                string host = EnvConfig.Get("LAVALINK_HOST", "lavalink");
                string port = EnvConfig.Get("SERVER_PORT", "2333"); // Match your .env file

                options.Label = "PlexBot";
                options.Passphrase = password;
                options.HttpClientName = host;
                options.BufferSize = 1024 * 1024 * 4;
                options.BaseAddress = new Uri($"http://{host}:{port}");
                options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });
            // Register options
            services.Configure<PlayerOptions>(options => {
                options.DefaultVolume = 0.2f;
                options.DisconnectAfterPlayback = false;
                options.InactivityTimeout = TimeSpan.FromMinutes(20);
                options.AnnounceNowPlaying = true;
                options.ShowThumbnails = true;
                options.DeleteOutdatedMessages = true;
                options.MaxQueueItemsToShow = 10;
                options.UsePremiumFeatures = false;
                options.DefaultRepeatMode = TrackRepeatMode.None;
            });
            // Add player service
            services.AddSingleton<IPlayerService, PlayerService>();
            // Register the state manager as a singleton
            services.AddSingleton<VisualPlayerStateManager>();
            // Add caching for better performance with Lavalink
            services.AddMemoryCache();
        }

        /// <summary>Sets up the extension system for plugin management and dynamic feature loading</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddExtensionServices(IServiceCollection services)
        {
            // Get extensions directory
            string extensionsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Extensions");

            // Add extension manager
            services.AddSingleton(provider => new ExtensionManager(provider, extensionsDir));
        }
    }
}