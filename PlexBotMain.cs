using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using dotenv.net;
using Lavalink4NET;
using Microsoft.Extensions.Logging;
using Lavalink4NET.Extensions;
using PlexBot.Core.LavaLink;
using Microsoft.Extensions.Hosting;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.InteractionComponents;
using PlexBot.Core.AutoComplete;
using PlexBot.Core.EventHandlers;
using PlexBot.Core.Commands;
using PlexBot.Core.Players;
using Newtonsoft.Json.Linq;

namespace PlexBot
{
    internal class PlexBotMain
    {
        private DiscordSocketClient? _client;
        private InteractionService? _interactions;
        private IServiceProvider? _serviceProvider;

        /// <summary>Main entry point for the bot.</summary>
        static void Main() => new PlexBotMain().MainAsync().GetAwaiter().GetResult();
        public async Task MainAsync()
        {
            // Try to get the bot token from environment variables
            string token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "";
            // If token is not found in environment variables, load from .env file
            if (string.IsNullOrEmpty(token))
            {
                string envFilePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../../../.env"));
                Console.WriteLine("Env not found, attempting to load from .env file in: " + envFilePath);
                if (File.Exists(envFilePath))
                {
                    DotEnvOptions envOptions = new(envFilePaths: [envFilePath]);
                    DotEnv.Load(envOptions);
                    token = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? "";
                }
                else
                {
                    Console.WriteLine("ERROR: .env file not found. Please create a .env file in the root directory.");
                    return;
                }
            }
            _serviceProvider = ConfigureServices();
            // Start all IHostedService instances
            foreach (IHostedService hostedService in _serviceProvider.GetServices<IHostedService>())
            {
                await hostedService.StartAsync(CancellationToken.None);
            }
            _client = _serviceProvider.GetRequiredService<DiscordSocketClient>();
            _interactions = _serviceProvider.GetRequiredService<InteractionService>();
            _client.InteractionCreated += async interaction =>
            {
                SocketInteractionContext ctx = new(_client, interaction);
                await _interactions.ExecuteCommandAsync(ctx, _serviceProvider);
            };
            _client.Log += Log;
            _interactions.Log += Log;
            _client.Ready += () => ReadyAsync();
            // Initialize and register event handlers
            UserEvents eventHandlers = new(_client);
            eventHandlers.RegisterHandlers();
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("ERROR: Bot token is null or empty. Check your .env file.");
                return;
            }
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        /// <summary>Configures and provides the services used by the bot. This includes setting up the Discord client
        /// with specified intents and log level, the interaction service for handling commands, various bot
        /// components like buttons and commands, and integrating the Lavalink audio service for music playback.
        /// Logging services are also configured to output to the console with a specified minimum log level.</summary>
        /// <remarks>
        /// The method utilizes dependency injection to register services such as <see cref="DiscordSocketClient"/>,
        /// <see cref="InteractionService"/>, and <see cref="Lavalink4NET.Services.IAudioService"/>. This setup ensures
        /// that all components are readily available throughout the application via the built-in service provider.
        /// </remarks>
        /// <returns>A <see cref="IServiceProvider"/> containing the configured services, ready for application use.</returns>
        public static IServiceProvider ConfigureServices()
        {
            ServiceCollection services = new();
            // Configure Discord client
            services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All,
                LogLevel = LogSeverity.Debug
            }));
            // Configure InteractionService for handling interactions from commands, buttons, modals, and selects
            services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>()));
            // Add other bot components so they can be passed between each other
            services.AddSingleton<Buttons>();
            services.AddSingleton<SlashCommands>();
            services.AddSingleton<AutoComplete>();
            services.AddSingleton<UserEvents>();
            services.AddSingleton<PlexApi>();
            services.AddSingleton<SelectMenus>();
            services.AddSingleton<Players>();
            services.AddSingleton<LavaLinkCommands>();
            services.AddSingleton<PlexApi>();
            // Add Lavalink and configure it
            services.AddLavalink();
            services.ConfigureLavalink(options =>
            {
                string password = Environment.GetEnvironmentVariable("LAVALINK_SERVER_PASSWORD") ?? "youshallnotpass";
                string host = Environment.GetEnvironmentVariable("LAVALINK_HOST") ?? "lavalink";
                string port = Environment.GetEnvironmentVariable("SERVER_PORT") ?? "2333";
                options.Label = "PlexBot";
                options.Passphrase = password;
                options.HttpClientName = host;
                options.BufferSize = 1024 * 1024 * 4;
                options.BaseAddress = new Uri($"http://{host}:{port}");
                options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
            });
            // Setup logging
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Debug);
            });
            return services.BuildServiceProvider();
        }

        /// <summary>Executes tasks when the bot client is ready, such as command registration and initialization. 
        /// It registers commands to guilds and sets the bot status.</summary>
        /// <returns>A Task representing the asynchronous operation.</returns>
        private async Task ReadyAsync()
        {
            try
            {
                // Things to be run when the bot is ready
                if (_client!.Guilds.Count != 0)
                {
                    // Register command modules with the InteractionService.
                    // Scans the whole assembly for classes that define slash commands.
                    await _interactions!.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);
                    foreach (var guild in _client.Guilds)
                    {
                        await _interactions.RegisterCommandsToGuildAsync(guild.Id, true);
                    }
                }
                else
                {
                    Console.WriteLine($"\nNo guilds found\n");
                }
                Console.WriteLine($"\nLogged in as {_client.CurrentUser.Username}\n" +
                    $"Registered {_interactions!.SlashCommands.Count} slash commands\n" +
                    $"Bot is a member of {_client.Guilds.Count} guilds\n");
                await _client.SetGameAsync("/help", null, ActivityType.Listening);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e}");
                throw;
            }
        }

        /// <summary>Handles logging of messages and exceptions from the Discord client and interaction services.</summary>
        /// <param name="message">The message to log.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        private Task Log(LogMessage message)
        {
            Console.WriteLine($"{DateTime.Now} [{message.Severity}] {message.Source}: {message.Message}");
            if (message.Exception is not null) // Check if there is an exception
            {
                // Log the full exception, including the stack trace
                Console.WriteLine($"Exception: {message.Exception}");
            }
            return Task.CompletedTask;
        }
    }
}