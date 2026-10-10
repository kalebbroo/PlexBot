# Build your first extension

This tutorial builds a working PlexBot extension from nothing. It is one folder with three files, and it adds a `/hello` slash command that replies with the bot's name and the extension's version. Copy the files in order and each step will make sense as you go.

For the full reference (music providers, services, the event bus, configuration and packaging), read [Creating Extensions](CreatingExtensions.md) after this tutorial.

> **Checked:** the three files below compile with the bot's own build flags (step 6). They also load through PlexBot's `ExtensionManager`, and the command module is discovered by the interaction framework, in a test program that does not connect to Discord. The `/hello` reply and the Docker run (step 7) follow the source code and have not been run end to end against Discord.

## Contents

1. [What you need](#1-what-you-need)
2. [Folder layout](#2-folder-layout)
3. [The project file](#3-the-project-file)
4. [The extension class](#4-the-extension-class)
5. [The command module](#5-the-command-module)
6. [Check that it builds](#6-check-that-it-builds)
7. [Run it](#7-run-it)
8. [Turn it off](#8-turn-it-off)
9. [Share it](#9-share-it)
10. [Troubleshooting](#10-troubleshooting)
11. [Next steps](#11-next-steps)

---

## 1. What you need

- **The .NET 10 SDK.** `dotnet --version` should print `10.0.x`.
- **A PlexBot checkout:**

  ```bash
  git clone https://github.com/kalebbroo/PlexBot.git
  cd PlexBot
  ```

- **Docker and Docker Compose**, only to run the bot (step 7). Steps 2 to 6 need just the SDK.

---

## 2. Folder layout

Create a folder named `HelloTutorial` inside the checkout's `Extensions/` folder:

```
PlexBot/
└── Extensions/
    └── HelloTutorial/
        ├── HelloTutorial.csproj
        ├── HelloExtension.cs
        └── HelloCommands.cs
```

The bot relies on three rules here:

- The `.csproj` must sit directly inside the extension folder. The bot looks only at that level.
- The extension folder must sit directly inside `Extensions/`. That is what makes `../../PlexBot.extension.props` point at the checkout root.
- The `.csproj` file name is the name of the DLL the bot loads, so `AssemblyName` must match it.

---

## 3. The project file

Create `Extensions/HelloTutorial/HelloTutorial.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <AssemblyName>HelloTutorial</AssemblyName>
    </PropertyGroup>

    <Import Project="../../PlexBot.extension.props" />

</Project>
```

`PlexBot.extension.props` sets the target to net10.0, references the host's `PlexBot.dll`, and adds the shared NuGet packages from `PlexBot.deps.props` (Discord.Net, Lavalink4NET and others). You do not list those yourself. The shared props do not bring in the host's global usings (`Main/Using.cs`), so each file below imports the namespaces it uses.

---

## 4. The extension class

Create `Extensions/HelloTutorial/HelloExtension.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PlexBot.Core.Extensions;
using PlexBot.Utils;

namespace HelloTutorial;

public class HelloExtension : Extension
{
    public override string Id => "hello-tutorial";
    public override string Name => "Hello Tutorial";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "Adds a /hello command that shows the bot's name and this extension's version";

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(this);
    }

    protected override Task<bool> OnInitializeAsync(IServiceProvider services)
    {
        Logs.Info($"{Name} v{Version} is ready");
        return Task.FromResult(true);
    }
}
```

What the parts do:

- `Id`, `Name`, `Version`, `Author` and `Description` are abstract in the base class ([`Core/Extensions/Extension.cs`](../../Core/Extensions/Extension.cs)), so every extension must set them. `Id` must be unique. It is also the prefix of the extension's settings (`extensions.<Id>.*` in `config.fds`).
- `MinimumBotVersion` (default `"1.0.0"`) and `Dependencies` (default: none) are optional, so this sample leaves them out. The bot skips an extension whose `MinimumBotVersion` is higher than its own assembly version. PlexBot does not set one, so the assembly version is 1.0.0.0 and the default passes.
- `RegisterServices` runs before the service container is built. `services.AddSingleton(this)` registers this instance, so the command module can ask for it in its constructor (step 5).
- `OnInitializeAsync` runs after the container is built. Return `true` when the extension is ready. Returning `false`, or throwing, leaves it unloaded.

---

## 5. The command module

Create `Extensions/HelloTutorial/HelloCommands.cs`:

```csharp
using Discord.Interactions;
using PlexBot.Core.Discord.Embeds;

namespace HelloTutorial;

public class HelloCommands(HelloExtension helloExtension) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("hello", "Say hello and see the bot and extension versions")]
    public async Task HelloCommand(
        [Summary("name", "Who to greet")] string name)
    {
        string botName = Context.Client.CurrentUser.Username;
        await RespondAsync(components: ComponentV2Builder.Info(
            "Hello",
            $"Hello {name}, I am {botName}. This is {helloExtension.Name} v{helloExtension.Version}."),
            ephemeral: true);
    }
}
```

What the parts do:

- The class derives from `InteractionModuleBase<SocketInteractionContext>`, which makes it a command module. The bot finds it by scanning the extension's assembly, so there is no registration call. The pattern matches [`Core/Discord/Commands/MusicCommands.cs`](../../Core/Discord/Commands/MusicCommands.cs).
- `[SlashCommand("hello", ...)]` turns the method into `/hello`. The name is lowercase, and the description is what Discord shows in the command picker.
- `[Summary("name", ...)]` names the option that the user fills in. A parameter with no default value is required.
- `helloExtension` is the instance registered in step 4. The constructor receives it from the service container.
- `ComponentV2Builder.Info` builds the Components V2 card that the rest of the bot uses for status messages (see the [Components V2 patterns in CONTRIBUTING.md](../../CONTRIBUTING.md#components-v2-patterns)). `ephemeral: true` means only the person who ran the command sees the reply.

---

## 6. Check that it builds

**6.1. Build PlexBot once** so that `bin/Debug/net10.0/PlexBot.dll` exists. Extensions compile against that file:

```bash
cd /path/to/PlexBot
dotnet build PlexBot.csproj
```

**6.2. Build the extension with the command the bot runs at startup** ([`Core/Extensions/ExtensionManager.cs`](../../Core/Extensions/ExtensionManager.cs)). It uses the bot's flags, with the output folder the bot uses on a local run:

```bash
cd /path/to/PlexBot/Extensions/HelloTutorial
dotnet build HelloTutorial.csproj -c Debug \
  -o /path/to/PlexBot/bin/Debug/net10.0/Extensions/HelloTutorial \
  -p:HostOutputDir=/path/to/PlexBot/bin/Debug/net10.0 \
  -p:EnableDynamicLoading=true
```

The summary must show `Build succeeded.` and `0 Error(s)`. Warnings about the shared NuGet packages (`NU1902` or `NU1903` advisories) can appear and do not come from your code. Warnings with a `CS` code do come from your code, and you should fix them.

`HostOutputDir` tells the build where the host's `PlexBot.dll` is. In Docker you do not run this step by hand: the bot runs the same build inside the container, with `HostOutputDir` set to `/app`.

---

## 7. Run it

### 7.1 Make commands appear at once (recommended while developing)

Open `config.fds` (created from `RenameMe.config.fds` by `Install/start.sh` if it is missing) and set:

```yaml
bot:
    environment: Development
```

In `Development`, slash commands register for each server the bot is in, so a new command shows up at once. Otherwise commands register globally, and Discord can take up to an hour to show a new global command. The bot copies `config.fds` into the container when it starts, so restart the bot after you change it. See [Configuration](../Setup/Configuration.md).

### 7.2 Start the stack

```bash
Install/start.sh --build
```

This rebuilds the bot image and recreates the container, so the bot builds your extension again inside the container. It needs your `.env` file ([Installation](../Setup/Installation.md)).

### 7.3 Check the logs

```bash
Install/start.sh --logs
```

The lines below come from the extension loader and the startup code, in this order. Other log lines are mixed in with them:

```
Building extension: HelloTutorial...
Built extension: HelloTutorial
Loaded extension assembly: HelloTutorial
Discovered extension: Hello Tutorial v1.0.0 by Your Name
Initializing extension: Hello Tutorial (v1.0.0)
Hello Tutorial v1.0.0 is ready
Extension initialized successfully: Hello Tutorial
Successfully loaded extension: Hello Tutorial v1.0.0
Successfully initialized 1 of 1 extensions
Initialized 1 extensions
```

`Initialized N extensions` counts every extension that loaded, not only yours. Once Discord connects, the bot logs:

```
Registered commands from extension: Hello Tutorial
```

The Docker bot is a Release build. It builds an extension once and then reuses that DLL, so until the container is recreated, later starts show `Loading cached extension: HelloTutorial` in place of `Building extension`.

In Discord, run `/hello` with a name, for example `/hello name:world`. The reply is a Components V2 card titled **Hello** that names the bot and says `Hello Tutorial v1.0.0`. Only you can see it.

**Docker keeps the built DLL.** After you change the code, run `Install/start.sh --build` again. A plain `docker restart PlexBot` starts the same container and reuses the old DLL.

### 7.4 Run without Docker (optional)

From the checkout root, run `dotnet run --project PlexBot.csproj`. That is a Debug build, so extensions are rebuilt at every start. You still need a `.env` file and a running Lavalink ([CONTRIBUTING.md](../../CONTRIBUTING.md#local-development-setup)).

---

## 8. Turn it off

Rename the folder with a `.disabled` suffix, then rebuild and start:

```bash
mv Extensions/HelloTutorial Extensions/HelloTutorial.disabled
Install/start.sh --build
```

The bot logs `Skipping disabled extension: HelloTutorial.disabled` and does not build or load it. The `/hello` command goes away when the bot next registers its commands. Global commands can take up to an hour to update.

---

## 9. Share it

PlexBot's `.gitignore` excludes `/Extensions`, so an extension needs a git repository of its own.

1. Put the contents of the `HelloTutorial` folder in a repository of their own, with the `.csproj` at the top level. Add a `.gitignore` that excludes `bin/` and `obj/`.
2. Others install it by cloning the repository into `Extensions/`:

   ```bash
   cd PlexBot/Extensions
   git clone https://github.com/<you>/hello-tutorial.git HelloTutorial
   cd ..
   Install/start.sh --build
   ```

The folder must sit directly in `Extensions/`, and its name must not end in `.disabled`.

---

## 10. Troubleshooting

Search the bot log for `HelloTutorial` (`Install/start.sh --logs`, or the files in `logs/`).

| What you see | Cause | Fix |
|---|---|---|
| `Failed to build extension HelloTutorial (exit code 1):` followed by `error CS...` lines | The code does not compile | Run step 6 and fix the first `error` line. A missing `using` is a common cause, because the host's global usings do not apply to extensions. |
| `The type or namespace name '...' could not be found` | A `using` is missing | Add the namespace shown in steps 4 and 5. |
| `No .csproj found in HelloTutorial — skipping` | The project file is not at the top of the folder | Move `HelloTutorial.csproj` directly into `Extensions/HelloTutorial/`. |
| `Extension HelloTutorial built but DLL not found at ...` | `AssemblyName` does not match the project file name | Make `AssemblyName` and the `.csproj` file name the same. |
| `No Extension subtypes found in HelloTutorial` | The class is not a non-abstract subclass of `Extension`, or it was not compiled | Check that `HelloExtension` is `public`, not `abstract`, and derives from `Extension`. |
| `Extension failed to initialize: Hello Tutorial - Extension initialization returned false` or `Exception during extension initialization: ...` | `OnInitializeAsync` returned `false` or threw | Read the message, fix the code, then run `Install/start.sh --build`. |
| `Extension Hello Tutorial requires bot version ..., current is .... Skipping.` | `MinimumBotVersion` is higher than the bot's version | Lower `MinimumBotVersion` to the bot's version. |
| `Skipping disabled extension: HelloTutorial.disabled` | The folder name ends in `.disabled` | Remove the suffix, unless you turned the extension off on purpose (step 8). |
| No `Registered commands from extension: Hello Tutorial` line | The extension did not load, or the command module was not found | Look for error lines above it. If the extension loaded, check that the module is `public`, derives from `InteractionModuleBase<SocketInteractionContext>`, and has a `[SlashCommand]` method. |
| `/hello` is missing in Discord | Global commands are slow to update, or `bot.environment` is not `Development` | Set `bot.environment: Development` (step 7.1), run `Install/start.sh --build`, then reload Discord (Ctrl+R). |
| Your code change has no effect in Docker | The Release bot reuses the DLL it built earlier (`Loading cached extension`) | Run `Install/start.sh --build`, which recreates the container and rebuilds the DLL. |

---

## 11. Next steps

- **A music provider.** Implement `IMusicProvider` ([`Core/Services/Music/IMusicProvider.cs`](../../Core/Services/Music/IMusicProvider.cs)) and register it in `RegisterServices` with `services.AddSingleton<IMusicProvider, YourProvider>()`. The bot then offers it as a `/search` source. See [Adding a Music Provider](CreatingExtensions.md#adding-a-music-provider).
- **Lavalink plugins.** Add a `lavalink.plugin.yml` next to the `.csproj`. It can contain `plugin:` (appended to Lavalink's plugin list), `pluginConfig:` (merged under `plugins:`) and `sources:` (merged into Lavalink's sources). [`Install/Docker/generate-lavalink-config.sh`](../../Install/Docker/generate-lavalink-config.sh) merges every enabled extension's file when `Install/start.sh` runs. Run `Install/start.sh --build` after each change, because only `--build` recreates the Lavalink container. The YouTube Music Provider's [`lavalink.plugin.yml`](https://github.com/kalebbroo/PlexBot-YouTube-MusicProvider/blob/main/lavalink.plugin.yml) is a real example. *Not run for this tutorial: this bullet follows the generator script's comments and that extension's own notes.*
- **Settings.** `GetConfig`, `GetConfigBool`, `GetConfigInt` and `GetConfigDouble` read `extensions.<Id>.<key>` from `config.fds`. See [Extension Configuration](CreatingExtensions.md#extension-configuration).
- **Events.** Subscribe to the `BotEventBus` events, such as `bot.ready` or `track.started`. See [Using the Event Bus](CreatingExtensions.md#using-the-event-bus).
