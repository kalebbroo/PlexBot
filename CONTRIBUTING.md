# Contributing to PlexBot

Thanks for your interest in contributing to PlexBot! This guide covers how to get started, the development workflow, and project conventions.

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker & Docker Compose](https://www.docker.com/products/docker-desktop/) (for running Lavalink)
- A Discord bot token ([Developer Portal](https://discord.com/developers/applications))
- A Plex server with a music library and an authentication token

### Local Development Setup

1. **Clone the repo**
   ```bash
   git clone https://github.com/kalebbroo/PlexBot.git
   cd PlexBot
   ```

2. **Configure secrets** — Copy `RenameMe.env.txt` to `.env` and fill in your credentials:
   ```bash
   cp RenameMe.env.txt .env
   ```

3. **Configure app settings** — Copy `RenameMe.config.fds` to `config.fds`:
   ```bash
   cp RenameMe.config.fds config.fds
   ```
   Set `bot.environment: Development` in `config.fds` for instant guild-scoped slash command updates.

4. **Start Lavalink** — The bot needs a running Lavalink instance. The easiest way is Docker:
   ```bash
   cd Install/Docker
   docker-compose up -d lavalink
   ```
   Then set `LAVALINK_HOST=localhost` in your `.env` (instead of the default `Lavalink` Docker service name).

5. **Build and run**
   ```bash
   dotnet build
   dotnet run
   ```

## Project Structure

```
PlexBot/
├── Core/                    # Core bot logic
│   ├── Discord/             # Discord interaction layer
│   │   ├── Commands/        # Slash command modules
│   │   ├── Autocomplete/    # Autocomplete handlers
│   │   └── Embeds/          # CV2 builders, buttons, visual player
│   ├── Events/              # Event bus system
│   ├── Extensions/          # Extension base class and manager
│   ├── Models/              # Data models (Track, Album, Playlist, etc.)
│   └── Services/            # Music services, providers, Lavalink
├── Extensions/              # Extension source directories (built at startup)
├── Docs/                    # All documentation
├── Images/                  # Player assets, icons, progress bar emoji
├── Install/                 # Docker and install scripts
├── Main/                    # Entry point, DI registration, global usings
└── Utils/                   # Config, logging, HTTP utilities
```

### Key Files

| File | Purpose |
|------|---------|
| `Main/ServiceRegistration.cs` | DI container setup |
| `Core/Discord/Embeds/ComponentV2Builder.cs` | Static factory for Components V2 layouts |
| `Core/Discord/Embeds/DiscordButtonBuilder.cs` | Flag-based button registration system |
| `Core/Discord/Embeds/VisualPlayer.cs` | Player UI rendering (modern image + classic embed) |
| `Core/Services/Music/PlexMusicService.cs` | Plex API integration with caching |
| `Core/Services/Music/MusicProviderRegistry.cs` | Routes searches to registered providers |
| `Core/Services/LavaLink/PlayerService.cs` | Audio playback via Lavalink4NET |
| `Core/Extensions/ExtensionManager.cs` | Discovers, builds, and loads extensions |
| `Utils/BotConfig.cs` | Reads `config.fds` settings |
| `Utils/EnvConfig.cs` | Reads `.env` secrets |

## Development Workflow

### Branching

- `main` — stable release branch
- Feature branches — branch off `main`, use descriptive names (e.g. `add-soundcloud-provider`, `fix-queue-shuffle`)

### Making Changes

1. Create a branch from `main`
2. Make your changes
3. Test locally with a real Discord bot and Plex server
4. Open a pull request against `main`

### Code Style

- **C# 12** with primary constructors — use them for DI injection
- **Nullable reference types** enabled — avoid `null` where possible
- Use the `Logs` utility class for logging (not `Console.WriteLine` or `ILogger`)
- Follow existing patterns: if similar code exists, match its style
- No unnecessary comments — code should be self-explanatory

### Components V2 Patterns

All Discord messages use the Components V2 system. Follow these patterns:

- Use `ComponentV2Builder` static methods for status messages (`Error`, `Info`, `Success`, `Warning`)
- Always set `MessageFlags.ComponentsV2` when using `ModifyAsync`
- When modifying a message: set `msg.Components`, `msg.Embed = null`, and `msg.Flags = MessageFlags.ComponentsV2`

### Configuration

- **Secrets** (tokens, passwords, URLs) go in `.env` — never in code or `config.fds`
- **App settings** (UI, behavior, logging) go in `config.fds`
- Never commit `.env` files

## Extensions

PlexBot has a plugin system for adding features without modifying the core. If your contribution is a self-contained feature (new music source, new command group, etc.), consider building it as an extension instead of modifying the core.

See [Creating Extensions](./Docs/Extensions/CreatingExtensions.md) for the full guide.

## Pull Request Guidelines

- Keep PRs focused — one feature or fix per PR
- Write a clear description of what changed and why
- Include steps to test the changes
- If your PR adds a new command or config option, update the relevant docs in `Docs/`

## Reporting Bugs

Use the [Bug Report](https://github.com/kalebbroo/PlexBot/issues/new?template=bug_report.yml) issue template. Include:

- Steps to reproduce
- Expected vs actual behavior
- Bot logs (`docker-compose logs plexbot` or check `logs/`)
- Your environment (OS, Docker version, .NET version)

## Requesting Features

Use the [Feature Request](https://github.com/kalebbroo/PlexBot/issues/new?template=feature_request.yml) issue template. Describe the use case and why it would be valuable.

## Community

- [Discord Server](https://discord.com/invite/5m4Wyu52Ek) — for questions and discussion
- [GitHub Issues](https://github.com/kalebbroo/PlexBot/issues) — for bugs and feature requests

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](./LICENSE).
