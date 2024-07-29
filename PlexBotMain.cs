Directory.SetCurrentDirectory(AppContext.BaseDirectory);

Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
    .WriteTo.Console()
    .CreateLogger();

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables(prefix: "PlexBot_")
    .AddDotNetEnv(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "../../../.env"))
    .AddUserSecrets<Program>()
    .Build();

var builder = Host.CreateApplicationBuilder(args);

string? discordToken = builder.Configuration.GetRequiredValue<string>("DISCORD_TOKEN");
builder.Services.AddSerilog();
builder.Services.AddSingleton<IConfiguration>(configuration);

// Configure Discord client
builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.All,
    LogLevel = LogSeverity.Debug
}));

// Configure InteractionService for handling interactions from commands, buttons, modals, and selects
builder.Services.AddSingleton(p => new InteractionService(p.GetRequiredService<DiscordSocketClient>()));

// Add other bot components so they can be passed between each other
builder.Services.AddSingleton<Buttons>();
builder.Services.AddSingleton<SlashCommands>();
builder.Services.AddSingleton<AutoComplete>();
builder.Services.AddSingleton<UserEvents>();
builder.Services.AddSingleton<PlexCore>();
builder.Services.AddSingleton<SelectMenus>();
builder.Services.AddSingleton<Players>();
builder.Services.AddSingleton<LavaLinkCommands>();
builder.Services.AddSingleton<PlexMusic>();

// Add Lavalink and configure it
builder.Services.AddLavalink();
builder.Services.ConfigureLavalink(options =>
{
    string password = builder.Configuration.GetValue<string>("LAVALINK_SERVER_PASSWORD") ?? "youshallnotpass";
    string host = builder.Configuration.GetValue<string>("LAVALINK_HOST") ?? "lavalink";
    string port = builder.Configuration.GetValue<string>("SERVER_PORT") ?? "2333";
    options.Label = "PlexBot";
    options.Passphrase = password;
    options.HttpClientName = host;
    options.BufferSize = 1024 * 1024 * 4;
    options.BaseAddress = new Uri($"http://{host}:{port}");
    options.ResumptionOptions = new LavalinkSessionResumptionOptions(TimeSpan.FromSeconds(60));
});

var host = builder.Build();

// Start all IHostedService instances
foreach (IHostedService hostedService in host.Services.GetServices<IHostedService>())
{
    await hostedService.StartAsync(CancellationToken.None);
}

var discordClient = host.Services.GetRequiredService<DiscordSocketClient>();
var interactionService = host.Services.GetRequiredService<InteractionService>();
discordClient.InteractionCreated += async interaction =>
{
    SocketInteractionContext ctx = new(discordClient, interaction);
    await interactionService.ExecuteCommandAsync(ctx, host.Services);
};

// Attach the LogMessage method to both discordClient.Log and interactionService.Log
discordClient.Log += DiscordHelpers.LogMessageAsync;
interactionService.Log += DiscordHelpers.LogMessageAsync;
discordClient.Ready += DiscordHelpers.ClientReady(host.Services);

// Initialize and register event handlers
UserEvents eventHandlers = new(discordClient);
eventHandlers.RegisterHandlers();

await discordClient.LoginAsync(TokenType.Bot, discordToken);
await discordClient.StartAsync();

//TODO: Is this really needed?
// Block this task until the program is closed.
await Task.Delay(-1);