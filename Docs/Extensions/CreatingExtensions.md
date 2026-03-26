# Creating Extensions for PlexBot

This guide provides comprehensive instructions for creating custom extensions for PlexBot. Extensions allow you to add new features, commands, music providers, and functionality to the bot without modifying the core codebase.

## Table of Contents

- [Overview](#overview)
- [Extension Lifecycle](#extension-lifecycle)
- [Extension Structure](#extension-structure)
- [Creating a Basic Extension](#creating-a-basic-extension)
- [Required Properties and Methods](#required-properties-and-methods)
- [Creating Slash Commands](#creating-slash-commands)
- [Registering Services](#registering-services)
- [Adding a Music Provider](#adding-a-music-provider)
- [Using the Event Bus](#using-the-event-bus)
- [Extension Configuration](#extension-configuration)
- [Example Extension: Custom Music Source](#example-extension-custom-music-source)
- [Packaging and Distribution](#packaging-and-distribution)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Overview

Extensions in PlexBot are modular components that inherit from the `Extension` base class. They are **built from source at startup** — PlexBot automatically runs `dotnet build` for each extension project it finds. Extensions use shared `.props` files to reference the host without a `ProjectReference`, keeping them fully decoupled. The extension system provides:

- **Build-at-startup**: Extensions are compiled automatically from source when the bot starts
- **Isolation**: Extensions operate independently of the core bot code
- **Dependency Management**: Extensions can depend on other extensions (loaded in topological order)
- **Service Registration**: Extensions register services into the real DI container
- **Command Discovery**: Slash commands in extension assemblies are automatically registered with Discord
- **Music Providers**: Extensions can add new music sources (SoundCloud, Spotify, etc.) via `IMusicProvider`
- **Event Bus**: Extensions can subscribe to bot lifecycle events (track started, bot ready, etc.)
- **Configuration**: Per-extension config via `extensions.{id}.*` keys in `config.fds`

## Extension Lifecycle

```
ServiceRegistration.AddExtensionServices()
  ├── ExtensionManager created (source dir + bin dir)
  ├── DiscoverAndInstantiateAsync()
  │    ├── For each extension folder with a .csproj:
  │    │    ├── dotnet build (passes HostOutputDir for DLL reference)
  │    │    ├── Load compiled DLL via ExtensionLoadContext
  │    │    └── Discover Extension subtypes, instantiate them
  │    └── Returns list of discovered extensions
  ├── extension.RegisterServices(IServiceCollection) ← registers into REAL DI container
  └── (container is built)

BotHostedService.StartAsync()
  ├── extensionManager.InitializeAllAsync(IServiceProvider) ← extensions get the built container
  ├── Music providers registered with MusicProviderRegistry
  └── (Discord Ready)
       ├── interactions.AddModulesAsync(ext.SourceAssembly) ← auto-discovers slash commands
       └── eventBus.PublishAsync(BotReady)
```

## Extension Structure

A typical extension consists of:

1. **`.csproj` file**: Imports `PlexBot.extension.props` for build configuration
2. **Main Extension Class**: Inherits from `PlexBot.Core.Extensions.Extension`
3. **Commands Module(s)**: Classes that implement slash commands using Discord.NET's interaction framework
4. **Services**: Additional services that provide business logic
5. **Music Providers**: Optional `IMusicProvider` implementations for new music sources

Extensions live in subdirectories under the `Extensions/` folder at the project root. Each extension must have its own directory containing a `.csproj` file.

```
PlexBot/
├── Extensions/
│   ├── README.md
│   ├── MyExtension/
│   │   ├── MyExtension.csproj
│   │   ├── MyExtension.cs
│   │   └── MyCommands.cs
│   └── AnotherExtension/
│       ├── AnotherExtension.csproj
│       └── ...
├── PlexBot.extension.props    ← shared build properties
├── PlexBot.deps.props         ← shared NuGet dependencies
└── PlexBot.csproj             ← host project (no extension references)
```

## Creating a Basic Extension

### Step 1: Create Extension Directory

Create a directory for your extension inside the `Extensions` folder:

```
Extensions/MyFirstExtension/
```

### Step 2: Create the `.csproj` File

Create a minimal `.csproj` that imports the shared props file. This gives your extension access to PlexBot's types and all shared NuGet dependencies without managing references manually:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <AssemblyName>MyFirstExtension</AssemblyName>
    </PropertyGroup>

    <Import Project="../../PlexBot.extension.props" />

</Project>
```

That's it for project setup. `PlexBot.extension.props` handles:
- Target framework (net9.0)
- Reference to the pre-built `PlexBot.dll` (via `HostOutputDir` passed at build time)
- Shared NuGet packages (Discord.Net, Lavalink4NET, etc.) via `PlexBot.deps.props`
- Build settings (nullable, implicit usings, etc.)

If your extension needs additional NuGet packages not in the shared deps, add them directly:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <AssemblyName>MyFirstExtension</AssemblyName>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    </ItemGroup>

    <Import Project="../../PlexBot.extension.props" />

</Project>
```

### Step 3: Create Extension Class

```csharp
using PlexBot.Core.Extensions;
using PlexBot.Utils;

namespace MyFirstExtension;

public class MyExtension : Extension
{
    public override string Id => "my-first-extension";
    public override string Name => "My First Extension";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "A simple extension for PlexBot";

    // Optional
    public override string MinimumBotVersion => "1.0.0";
    public override IEnumerable<string> Dependencies => [];

    protected override async Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        Logs.Info($"{Name} is initializing...");

        // Your initialization logic here
        // The full DI container is available via 'services'

        return true; // Return true for successful initialization
    }

    public override void RegisterServices(IServiceCollection services)
    {
        // Register services BEFORE the container is built
        // These become available to your commands and other extensions
        services.AddSingleton<MyService>();
    }

    public override async Task ShutdownAsync()
    {
        Logs.Info($"{Name} is shutting down...");
        await base.ShutdownAsync();
    }
}
```

## Required Properties and Methods

### Essential Properties

| Property | Description | Example |
|----------|-------------|---------|
| `Id` | Unique identifier (no spaces, lowercase) | `"my-extension"` |
| `Name` | User-friendly display name | `"My Extension"` |
| `Version` | Semantic version (MAJOR.MINOR.PATCH) | `"1.0.0"` |
| `Author` | Creator's name | `"Your Name"` |
| `Description` | Brief functionality explanation | `"Adds weather commands"` |

### Optional Properties

| Property | Description | Default |
|----------|-------------|---------|
| `MinimumBotVersion` | Minimum compatible bot version | `"1.0.0"` |
| `Dependencies` | IDs of required extensions | `[]` |

### Required Methods

| Method | Description | Return Value |
|--------|-------------|--------------|
| `OnInitializeAsync` | Initialization logic (receives built IServiceProvider) | `Task<bool>` (true = success) |

### Optional Methods

| Method | Description |
|--------|-------------|
| `RegisterServices` | Register services into DI container (called before container is built) |
| `ShutdownAsync` | Clean up resources on shutdown |

### Built-in Config Helpers

Extensions can read per-extension configuration from `config.fds` using built-in helpers:

```csharp
// Reads from config key "extensions.my-extension.apiKey"
string apiKey = GetConfig("apiKey", "default-value");
bool enabled = GetConfigBool("enabled", true);
int maxResults = GetConfigInt("maxResults", 25);
double threshold = GetConfigDouble("threshold", 0.5);
```

## Creating Slash Commands

Slash commands in extension assemblies are **automatically discovered** during Discord's Ready event. Just create a public class inheriting from `InteractionModuleBase<SocketInteractionContext>`:

```csharp
using Discord;
using Discord.Interactions;

namespace MyFirstExtension;

[Group("myext", "My extension commands")]
public class MyCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly MyService _myService;

    public MyCommands(MyService myService)
    {
        _myService = myService;
    }

    [SlashCommand("ping", "Returns a pong response")]
    public async Task PingCommand()
    {
        await RespondAsync("Pong! Extension is working!");
    }

    [SlashCommand("greet", "Greets a user")]
    public async Task GreetCommand(
        [Summary("user", "The user to greet")] IUser user,
        [Summary("greeting", "The greeting to use")] string greeting = "Hello")
    {
        await RespondAsync($"{greeting}, {user.Mention}!");
    }
}
```

No manual registration needed — PlexBot scans your extension's assembly automatically.

## Registering Services

Register services in `RegisterServices()` — this runs before the DI container is built, so your services are available everywhere:

```csharp
public override void RegisterServices(IServiceCollection services)
{
    services.AddSingleton<MyService>();
    services.AddScoped<MyOtherService>();
    services.AddTransient<MyDisposableService>();
}
```

## Adding a Music Provider

Extensions can add new music sources by implementing `IMusicProvider` and registering it. The provider will automatically appear in `/search` autocomplete and handle search/browse operations.

### Step 1: Implement IMusicProvider

```csharp
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.Music;

namespace MyMusicExtension;

public class SoundCloudProvider(IHttpClientFactory httpClientFactory) : IMusicProvider
{
    public string Id => "soundcloud";
    public string DisplayName => "SoundCloud";
    public bool IsAvailable => true;
    public int Priority => 20; // Lower = appears first in autocomplete
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search | MusicProviderCapabilities.UrlPlayback;

    public async Task<SearchResults> SearchAsync(string query, CancellationToken ct = default)
    {
        // Implement search logic
        SearchResults results = new() { Query = query, SourceSystem = "soundcloud" };
        // ... add tracks to results.Tracks
        return results;
    }

    public async Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken ct = default)
    {
        // Return track details or null if not supported
        return null;
    }

    // Implement other methods (return empty lists/null for unsupported features)
    public Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Track>());
    public Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Album>());
    public Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken ct = default) =>
        Task.FromResult(new List<Track>());
    public Task<List<Playlist>> GetPlaylistsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<Playlist>());
    public Task<Playlist?> GetPlaylistDetailsAsync(string playlistKey, CancellationToken ct = default) =>
        Task.FromResult<Playlist?>(null);
}
```

### Step 2: Register as IMusicProvider

```csharp
public override void RegisterServices(IServiceCollection services)
{
    // Register as IMusicProvider — PlexBot auto-registers it with MusicProviderRegistry
    services.AddSingleton<IMusicProvider, SoundCloudProvider>();
}
```

### Capabilities Flags

| Flag | Description |
|------|-------------|
| `Search` | Provider supports text search |
| `TrackDetails` | Can retrieve individual track details |
| `Albums` | Can browse albums |
| `Playlists` | Can browse playlists |
| `ArtistBrowse` | Can browse artist discographies |
| `UrlPlayback` | Can play from direct URLs |

## Using the Event Bus

Extensions can subscribe to bot lifecycle events via `BotEventBus`:

```csharp
using PlexBot.Core.Events;

protected override async Task<bool> OnInitializeAsync(IServiceProvider services)
{
    BotEventBus eventBus = services.GetRequiredService<BotEventBus>();

    // Subscribe to track events
    eventBus.Subscribe(BotEvents.TrackStarted, async (evt) =>
    {
        string title = evt.Data["title"] as string ?? "Unknown";
        string artist = evt.Data["artist"] as string ?? "Unknown";
        ulong guildId = (ulong)evt.Data["guildId"];
        Logs.Info($"Now playing: {title} by {artist} in guild {guildId}");
    });

    eventBus.Subscribe(BotEvents.BotReady, async (evt) =>
    {
        int guildCount = (int)evt.Data["guildCount"];
        Logs.Info($"Bot is ready! Connected to {guildCount} guilds");
    });

    return true;
}
```

### Available Events

| Event | Data Keys | Description |
|-------|-----------|-------------|
| `track.started` | `title`, `artist`, `guildId` | A track started playing |
| `track.ended` | `title`, `guildId`, `endReason` | A track finished |
| `queue.changed` | `guildId` | Queue was modified |
| `player.created` | `guildId` | Player was created |
| `player.destroyed` | `guildId` | Player was destroyed |
| `extension.loaded` | `extensionId`, `extensionName` | An extension was loaded |
| `bot.ready` | `guildCount` | Bot connected to Discord |

## Extension Configuration

Add per-extension configuration in `config.fds`:

```fds
extensions:
    my-extension:
        apiKey: your-api-key-here
        maxResults: 25
        enabled: true
```

Access it using the built-in helpers in your extension class:

```csharp
string apiKey = GetConfig("apiKey");
int maxResults = GetConfigInt("maxResults", 25);
```

## Example Extension: Custom Music Source

Here's a complete extension that adds a hypothetical music source:

### Directory Structure

```
Extensions/
└── MyMusicSource/
    ├── MyMusicSource.csproj
    ├── MyMusicExtension.cs
    ├── MyMusicProvider.cs
    └── MyMusicCommands.cs
```

### MyMusicSource.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <AssemblyName>MyMusicSource</AssemblyName>
    </PropertyGroup>

    <Import Project="../../PlexBot.extension.props" />

</Project>
```

### MyMusicExtension.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using PlexBot.Core.Events;
using PlexBot.Core.Extensions;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;

namespace MyMusicSource;

public class MyMusicExtension : Extension
{
    public override string Id => "my-music-source";
    public override string Name => "My Music Source";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "Adds a custom music source to PlexBot";

    public override void RegisterServices(IServiceCollection services)
    {
        // Register the music provider — automatically picked up by MusicProviderRegistry
        services.AddSingleton<IMusicProvider, MyMusicProvider>();
    }

    protected override async Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        string apiKey = GetConfig("apiKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            Logs.Error($"{Name}: No API key configured. Set extensions.{Id}.apiKey in config.fds");
            return false;
        }

        // Subscribe to events
        BotEventBus eventBus = services.GetRequiredService<BotEventBus>();
        eventBus.Subscribe(BotEvents.TrackStarted, OnTrackStarted);

        Logs.Info($"{Name} v{Version} initialized");
        return true;
    }

    private Task OnTrackStarted(BotEvent evt)
    {
        // React to tracks from our provider
        Logs.Debug($"Track started: {evt.Data["title"]}");
        return Task.CompletedTask;
    }
}
```

## Packaging and Distribution

Extensions are distributed as **source code**, not pre-compiled DLLs. PlexBot builds them automatically at startup.

### For development

1. Create your extension folder under `Extensions/`
2. Add a `.csproj` importing `PlexBot.extension.props`
3. Start PlexBot — it runs `dotnet build` for your extension automatically

### For distribution

1. Share your extension folder (the directory with `.csproj` and source files)
2. Users place it under their `Extensions/` directory
3. PlexBot builds and loads it on next startup

### Disabling an extension

Rename the folder with a `.disabled` suffix:
```
Extensions/MyExtension.disabled/
```

PlexBot skips directories ending in `.disabled` during discovery.

### Build behavior

- **Debug mode**: Extensions are rebuilt every startup to pick up source changes
- **Release mode**: Extensions are only built if no cached DLL exists. Delete the compiled output under the bin directory to force a rebuild.

## Shared Props Files

PlexBot provides two shared MSBuild files that extensions import:

### `PlexBot.extension.props`

Sets up the build environment and references the host DLL. The `HostOutputDir` MSBuild property is passed automatically by the `ExtensionManager` at build time so extensions always reference the correct host output.

### `PlexBot.deps.props`

Provides shared NuGet package references matching the host's versions (Discord.Net, Lavalink4NET, ImageSharp, etc.). These use `PrivateAssets="all"` so they aren't copied to extension output — the host already has them loaded at runtime.

If you need a package not listed in `PlexBot.deps.props`, add it directly to your `.csproj`. If it's a private dependency (not shared with the host), it will be copied to your extension's output and loaded via the extension's `AssemblyLoadContext`.

## Best Practices

### Do's
- Follow semantic versioning
- Add comprehensive command descriptions and parameter summaries
- Implement proper error handling and logging using the `Logs` class
- Clean up resources in `ShutdownAsync`
- Use `RegisterServices` for DI — don't create services manually
- Use `GetConfig()` helpers for extension settings
- Return empty collections (not null) from unsupported `IMusicProvider` methods
- Import `PlexBot.extension.props` instead of manually adding references

### Don'ts
- Don't add a `ProjectReference` to `PlexBot.csproj` — use the shared props files
- Don't create a separate `ServiceCollection` — use the one passed to `RegisterServices`
- Don't block the main thread with long operations
- Don't create global static state that might conflict with other extensions
- Don't override Discord events directly — use the `BotEventBus` instead
- Don't hardcode values for IDs, channels, or messages

## Troubleshooting

### Extension Not Building

1. Check console logs for `dotnet build` error output
2. Verify your `.csproj` imports `../../PlexBot.extension.props`
3. Ensure the host has been built first (extensions reference the host DLL)
4. Check that required NuGet packages are available

### Extension Not Loading

1. Check console logs for error messages
2. Verify the directory is under `Extensions/` and contains a `.csproj`
3. Ensure the class inherits from `Extension` (singular, not `Extensions`)
4. Check `MinimumBotVersion` compatibility
5. Verify all dependency extensions are present
6. Make sure the folder name doesn't end in `.disabled`

### Commands Not Appearing

1. Ensure command modules are `public` and inherit from `InteractionModuleBase<SocketInteractionContext>`
2. Verify the extension loaded successfully (check logs)
3. In Development mode, commands register per-guild (instant). In Production, global commands can take up to an hour to propagate.

### Services Not Resolving

1. Make sure services are registered in `RegisterServices()`, not in `OnInitializeAsync()`
2. `RegisterServices` runs before the container is built — that's the only place to add services
3. Check service lifetimes (Singleton vs Scoped vs Transient)

### Music Provider Not Showing

1. Verify you registered it as `services.AddSingleton<IMusicProvider, YourProvider>()`
2. Check that `IsAvailable` returns `true`
3. Look for registration logs: "Music provider registered: YourProvider (your-id)"
