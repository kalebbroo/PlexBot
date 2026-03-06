using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Discord.Events;
using PlexBot.Core.Extensions;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Core.Services.Music;
using PlexBot.Core.Events;
using PlexBot.Core.Services.PlexApi;
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

            // Add music provider services
            AddMusicProviderServices(services);

            // Add event bus
            services.AddSingleton<BotEventBus>();

            // Add extension services (must be last — extensions may depend on all above)
            AddExtensionServices(services);

            Logs.Init("Services registered");
            return services;
        }

        /// <summary>Configures Discord client, interaction service, and event handlers for bot communication</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddDiscordServices(IServiceCollection services)
        {
            // Configure Discord client
            // LogSeverity.Debug ensures all Discord/Lavalink messages reach our Logs class,
            // which handles console filtering (LOGGING_LEVEL_ROOT) and always saves everything to file
            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                    | GatewayIntents.GuildMessages
                    | GatewayIntents.GuildVoiceStates
                    | GatewayIntents.GuildMessageReactions
                    | GatewayIntents.DirectMessages
                    | GatewayIntents.MessageContent,
                AlwaysDownloadUsers = false,
                MessageCacheSize = 100,
                LogLevel = LogSeverity.Debug
            }));

            // Configure interaction service
            services.AddSingleton(provider => new InteractionService(
                provider.GetRequiredService<DiscordSocketClient>(),
                new InteractionServiceConfig
                {
                    DefaultRunMode = RunMode.Async,
                    LogLevel = LogSeverity.Debug
                }));
            services.AddSingleton<DiscordEventHandler>();
            services.AddSingleton<VisualPlayer>();
            services.AddSingleton<DiscordButtonBuilder>();
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
                string port = EnvConfig.Get("LAVALINK_SERVER_PORT", "2333");
                bool secure = EnvConfig.GetBool("LAVALINK_SECURE", false);
                string scheme = secure ? "https" : "http";

                options.Label = "PlexBot";
                options.Passphrase = password;
                options.HttpClientName = host;
                options.BufferSize = 1024 * 1024 * 4;
                options.BaseAddress = new Uri($"{scheme}://{host}:{port}");
                options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });

            // Add inactivity tracking - auto-disconnects when no users in voice or player idle
            TimeSpan inactivityTimeout = TimeSpan.FromMinutes(BotConfig.GetDouble("visualPlayer.inactivityTimeout", 2.0));
            services.AddInactivityTracking();
            services.ConfigureInactivityTracking(options =>
            {
                options.DefaultTimeout = inactivityTimeout;
                options.DefaultPollInterval = TimeSpan.FromSeconds(5);
                options.UseDefaultTrackers = true;
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
            // Add player services
            services.AddSingleton<ITrackResolverService, TrackResolverService>();
            services.AddSingleton<ITrackPrefetchService, TrackPrefetchService>();
            services.AddSingleton<IPlayerService, PlayerService>();
            // Register the state manager as a singleton
            services.AddSingleton<VisualPlayerStateManager>();
            // Add caching for better performance with Lavalink
            services.AddMemoryCache();
        }

        /// <summary>Registers the music provider registry and built-in Plex provider.
        /// Additional providers (YouTube, SoundCloud, etc.) are loaded via extensions.</summary>
        private static void AddMusicProviderServices(IServiceCollection services)
        {
            services.AddSingleton<MusicProviderRegistry>();
            services.AddSingleton<IMusicProvider, PlexMusicProvider>();
        }

        /// <summary>Sets up the extension system with two-phase startup: discover and register services
        /// before the container is built, then initialize after</summary>
        /// <param name="services">The service collection to add services to</param>
        private static void AddExtensionServices(IServiceCollection services)
        {
            // Source: extension .csproj folders. Configurable for Docker where source is mounted elsewhere.
            string extensionsSourceDir = System.IO.Path.GetFullPath(
                EnvConfig.Get("EXTENSIONS_SOURCE_DIR", "Extensions"));
            // Bin: compiled extension DLLs go next to the host output
            string extensionsBinDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Extensions");

            ExtensionManager manager = new(extensionsSourceDir, extensionsBinDir);
            services.AddSingleton(manager);

            // Build, discover, and let extensions register services into THIS collection
            IReadOnlyList<Extension> extensions = manager.DiscoverAndInstantiateAsync()
                .GetAwaiter().GetResult();
            foreach (Extension ext in extensions)
            {
                ext.RegisterServices(services);
            }
        }
    }
}