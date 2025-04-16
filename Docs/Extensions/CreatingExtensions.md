# Creating Extensions for PlexBot

This guide provides comprehensive instructions for creating custom extensions for PlexBot. Extensions allow you to add new features, commands, and functionality to the bot without modifying the core codebase.

## Table of Contents

- [Overview](#overview)
- [Extension Structure](#extension-structure)
- [Creating a Basic Extension](#creating-a-basic-extension)
- [Required Properties and Methods](#required-properties-and-methods)
- [Creating Slash Commands](#creating-slash-commands)
- [Registering Services](#registering-services)
- [Example Extension: Ping Command](#example-extension-ping-command)
- [Packaging and Distribution](#packaging-and-distribution)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Overview

Extensions in PlexBot are modular components that inherit from the `Extensions` base class. They have their own lifecycle, allowing them to initialize, register services and commands, and shut down gracefully. The extension system provides:

- **Isolation**: Extensions operate independently of the core bot code
- **Dependency Management**: Extensions can depend on other extensions
- **Service Registration**: Extensions can register their own services in the dependency injection container
- **Command Registration**: Extensions can add new slash commands and interactions
- **Configuration**: Extensions can have their own configuration settings

## Extension Structure

A typical extension consists of:

1. **Main Extension Class**: Inherits from `PlexBot.Core.Extensions.Extensions`
2. **Commands Module(s)**: Classes that implement slash commands using Discord.NET's interaction framework
3. **Services**: Additional services that provide business logic
4. **Handlers**: Event handlers for Discord events or custom events

Extensions are loaded from subdirectories in the `extensions` folder of the PlexBot installation. Each extension should be in its own directory with its own assembly.

## Creating a Basic Extension

### Step 1: Create Extension Directory

Create a directory for your extension inside the `extensions` folder:

```
/extensions/MyFirstExtension/
```

### Step 2: Create Project

Create a new C# Class Library project targeting .NET 7.0 or later:

```bash
cd extensions/MyFirstExtension
dotnet new classlib -f net7.0
```

### Step 3: Add References

Add references to the PlexBot core libraries and Discord.NET:

```bash
dotnet add reference ../../PlexBot.dll
dotnet add package Discord.Net
```

### Step 4: Create Extension Class

Create a class that inherits from the `Extensions` base class:

```csharp
using PlexBot.Core.Extensions;
using PlexBot.Utils;

namespace MyFirstExtension;

public class MyExtension : Extensions
{
    // Required properties
    public override string Id => "my-first-extension";
    public override string Name => "My First Extension";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "A simple extension for PlexBot";
    
    // Optional properties
    public override string MinimumBotVersion => "1.0.0";
    public override IEnumerable<string> Dependencies => Array.Empty<string>();
    
    // Required initialization method
    protected override async Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        Logs.Info($"{Name} is initializing...");
        
        // Your initialization logic here
        
        return true; // Return true for successful initialization
    }
    
    // Optional service registration
    public override void RegisterServices(IServiceCollection services)
    {
        // Register any services your extension needs
    }
    
    // Optional shutdown method
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
| `Dependencies` | IDs of required extensions | `Array.Empty<string>()` |

### Required Methods

| Method | Description | Return Value |
|--------|-------------|--------------|
| `OnInitializeAsync` | Initialization logic | `Task<bool>` (true for success) |

### Optional Methods

| Method | Description |
|--------|-------------|
| `RegisterServices` | Register services with DI container |
| `ShutdownAsync` | Clean up resources on shutdown |

## Creating Slash Commands

Slash commands are registered using Discord.NET's interaction framework. Here's how to create a command module:

### Step 1: Create Command Module Class

```csharp
using Discord;
using Discord.Interactions;
using PlexBot.Utils;

namespace MyFirstExtension;

// Group commands under a specific name
[Group("myext", "My extension commands")]
public class MyCommands : InteractionModuleBase<SocketInteractionContext>
{
    // Inject dependencies if needed
    private readonly MyService _myService;
    
    public MyCommands(MyService myService)
    {
        _myService = myService;
    }
    
    // Create a slash command
    [SlashCommand("ping", "Returns a simple pong response")]
    public async Task PingCommand()
    {
        await RespondAsync("Pong! Extension is working!");
    }
    
    // Command with parameters
    [SlashCommand("greet", "Greets a user")]
    public async Task GreetCommand(
        [Summary("user", "The user to greet")] IUser user,
        [Summary("greeting", "The greeting to use")] string greeting = "Hello")
    {
        await RespondAsync($"{greeting}, {user.Mention}!");
    }
    
    // Command with choices
    [SlashCommand("format", "Format text in different styles")]
    public async Task FormatCommand(
        [Summary("text", "The text to format")] string text,
        [Summary("style", "The formatting style")]
        [Choice("Bold", "bold"), 
         Choice("Italic", "italic"),
         Choice("Underline", "underline")] string style)
    {
        string formattedText = style switch
        {
            "bold" => $"**{text}**",
            "italic" => $"*{text}*",
            "underline" => $"__{text}__",
            _ => text
        };
        
        await RespondAsync(formattedText);
    }
}
```

### Step 2: Register Command Module

Command modules are automatically discovered and registered by PlexBot's extension system. Ensure your command module is public and follows the Discord.NET Interactions patterns.

## Registering Services

Extensions can register their own services for dependency injection:

```csharp
public override void RegisterServices(IServiceCollection services)
{
    // Register as singleton (one instance for entire bot)
    services.AddSingleton<MyService>();
    
    // Register as scoped (one instance per interaction)
    services.AddScoped<MyOtherService>();
    
    // Register as transient (new instance each time requested)
    services.AddTransient<MyDisposableService>();
}
```

## Example Extension: Ping Command

Let's create a complete, working extension with a simple ping command:

### Directory Structure

```
extensions/
└── PingExtension/
    ├── PingExtension.csproj
    ├── PingExtension.cs
    └── PingCommands.cs
```

### PingExtension.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  
  <ItemGroup>
    <Reference Include="PlexBot">
      <HintPath>..\..\PlexBot.dll</HintPath>
    </Reference>
  </ItemGroup>
  
  <ItemGroup>
    <PackageReference Include="Discord.Net" Version="3.12.0" />
  </ItemGroup>
</Project>
```

### PingExtension.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using PlexBot.Core.Extensions;
using PlexBot.Utils;

namespace PingExtension;

public class PingExtension : Extensions
{
    public override string Id => "ping-extension";
    public override string Name => "Ping Extension";
    public override string Version => "1.0.0";
    public override string Author => "PlexBot Team";
    public override string Description => "Adds a simple ping command to check bot latency";
    
    // Optional dependency on core music extension if needed
    public override IEnumerable<string> Dependencies => new[] { "core-music" };
    
    protected override Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        Logs.Info($"{Name} v{Version} initializing...");
        
        // This extension doesn't need special initialization
        
        Logs.Info($"{Name} initialized successfully!");
        return Task.FromResult(true);
    }
    
    public override void RegisterServices(IServiceCollection services)
    {
        // Register a ping statistics service
        services.AddSingleton<PingStatisticsService>();
    }
}

// Simple service to track ping statistics
public class PingStatisticsService
{
    private int _pingCount = 0;
    private readonly Stopwatch _stopwatch = new();
    
    public int GetAndIncrementPingCount()
    {
        return Interlocked.Increment(ref _pingCount);
    }
    
    public long MeasurePingTime(Action action)
    {
        _stopwatch.Restart();
        action();
        _stopwatch.Stop();
        return _stopwatch.ElapsedMilliseconds;
    }
}
```

### PingCommands.cs

```csharp
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using PlexBot.Utils;

namespace PingExtension;

public class PingCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PingStatisticsService _statistics;
    private readonly DiscordSocketClient _client;
    
    public PingCommands(PingStatisticsService statistics, DiscordSocketClient client)
    {
        _statistics = statistics;
        _client = client;
    }
    
    [SlashCommand("ping", "Check the bot's latency and uptime")]
    public async Task PingCommand()
    {
        // Defer response to measure time
        await DeferAsync();
        
        // Measure response time
        long responseTime = _statistics.MeasurePingTime(() => { /* Empty action for timing */ });
        
        // Get WebSocket latency
        int websocketLatency = _client.Latency;
        
        // Get ping count
        int pingCount = _statistics.GetAndIncrementPingCount();
        
        // Build and send response
        var embed = new EmbedBuilder()
            .WithTitle("🏓 Pong!")
            .WithColor(Color.Green)
            .WithDescription("Bot is up and running!")
            .AddField("Response Time", $"{responseTime}ms", true)
            .AddField("WebSocket Latency", $"{websocketLatency}ms", true)
            .AddField("Ping Count", pingCount, true)
            .WithFooter($"Extension v1.0.0 • {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
            .Build();
        
        await FollowupAsync(embed: embed);
    }
    
    [SlashCommand("uptime", "Check how long the bot has been running")]
    public async Task UptimeCommand()
    {
        // Get process uptime
        TimeSpan uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        
        // Format uptime string
        string formattedUptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        
        await RespondAsync($"Bot has been running for: **{formattedUptime}**");
    }
}
```

## Packaging and Distribution

To package your extension for distribution:

1. **Build your extension**:
   ```bash
   dotnet build -c Release
   ```

2. **Create a directory structure**:
   ```
   MyExtension/
   ├── bin/
   │   └── <compiled DLLs>
   └── extension.json  # Optional metadata
   ```

3. **Share your extension**:
   - Upload to GitHub
   - Share ZIP file
   - Submit to PlexBot extension repository

## Best Practices

### Do's
- ✅ Follow semantic versioning for your extension
- ✅ Add comprehensive command descriptions and parameter summaries
- ✅ Implement proper error handling and logging
- ✅ Clean up resources in the `ShutdownAsync` method
- ✅ Use the PlexBot logging system (`Logs` class) for consistency

### Don'ts
- ❌ Access Discord client directly when possible, use the provided services
- ❌ Block the main thread with long operations
- ❌ Create global static state that might conflict with other extensions
- ❌ Override Discord events without understanding the implications
- ❌ Use hardcoded values for IDs, channels, or messages

## Troubleshooting

### Extension Not Loading

1. Check console logs for error messages
2. Verify file structure and assembly names
3. Ensure all dependencies are available
4. Check minimum bot version compatibility

### Commands Not Appearing

1. Ensure commands are public and properly attributed
2. Check that interaction modules inherit from `InteractionModuleBase`
3. Verify extension is properly initialized
4. Check Discord API permissions

### Runtime Errors

1. Look for exceptions in the logs
2. Check service lifetime and dependency registration
3. Verify permissions for actions your extension performs
4. Test commands individually to isolate issues

---

This guide covers the basics of creating extensions for PlexBot. For more advanced techniques, see the [Advanced Extensions Guide](./AdvancedExtensions.md) or browse the [sample extensions repository](https://github.com/kalebbroo/plexbot-extensions-examples).