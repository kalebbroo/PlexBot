# PlexBot — agent entry point

A Discord music bot (.NET 10) that plays Plex library tracks through Lavalink4NET. Code lives in `Core/` (Discord
layer, services, models, events), `Main/` (entry point and DI), `Utils/` (config, logging, images), `Extensions/`
(optional plugins, built at startup), `Tests/PlexBot.Tests`, `Tools/EmojiGenerator` (draws the emoji art) and
`Docs/`. Human-facing setup and conventions are in [CONTRIBUTING.md](CONTRIBUTING.md) and [README.md](README.md).

Read once per task: [CONTRIBUTING.md](CONTRIBUTING.md), then the one `Docs/` page for the area you are changing.
Never read the whole tree for one task. For wrong playback, skipped tracks or Plex errors, read
[Docs/Plex-Playlist-Refactor-Plan.md](Docs/Plex-Playlist-Refactor-Plan.md) and [Docs/Guides/Troubleshooting.md](Docs/Guides/Troubleshooting.md)
before debugging.

Three rules that apply before anything else:

- **kalebbroo is the sole author.** No co-author, session or tool-attribution trailer in any commit message, PR
  title or PR body, whatever a tool's own default says. Check with `git log --format=%B` before pushing.
- **Work ships as a draft PR** against `main`: tested, bot-reviewed, every review comment resolved, then merged.
  Do not merge a PR nobody has tested. Stacked PRs merge bottom-up, and the lower one merges first.
- **Reuse before writing.** Check `Utils/` and the existing services first (`AssetPaths`, `BotConfig`, `Logs`,
  `ComponentV2Builder`, `Notices`, `DesignTokens`). New code stays modular and parameterized rather than copied per
  caller.

## Commands

- Build the bot: `dotnet build PlexBot.csproj`
- Run the tests: `dotnet test PlexBot.sln` (the test project is excluded from the bot build and from Docker)
- Rebuild and start the stack: `Install/start.sh --build` (logs: `Install/start.sh --logs`)
- Regenerate the emoji art: `dotnet run --project Tools/EmojiGenerator -- Images/Emoji Images/PlexBotBanner.png <sheet.png>`

## Conventions that matter here

- **Components V2 everywhere.** Status messages come from `ComponentV2Builder` (`Success`, `Error`, `Info`,
  `Warning`, or a `Notice` from `Core/Discord/Messages/Notices.cs` when the wording is shared). When modifying a
  message, set `Components`, `Embed = null` and `Flags = MessageFlags.ComponentsV2`.
- **Colours come from `DesignTokens`** (`Core/Discord/Design/`). No inline colour values in builders.
- **Shared wording lives in `Notices`.** Changing a notice changes what every user sees; `NoticeTests` pins it.
- **Secrets in `.env`, app settings in `config.fds`.** Never commit `.env`, `config.fds`, anything under `data/`
  or `logs/`. Logs can contain Plex tokens. Never log a token or a full URL that contains one.
- **Use `Logs`** (`Utils/Logs.cs`), not `Console.WriteLine` or `ILogger`.
- **C# 12 with primary constructors** for DI. Nullable reference types are on.
- **Comments are for the non-obvious only.** Name things well instead.

## Runtime state

- `data/` holds runtime files, including `data/emoji-hashes.json` written by the emoji sync. It is not source.
- Emoji are **application emoji** owned by the bot (not server emoji). `EmojiRegistry` uploads the PNGs in
  `Images/Emoji` on startup, skips names already present, and replaces an emoji whose PNG hash changed. Any change
  to an emoji's art is a PNG change plus a restart.

## Where the work is

| Area | Start here |
|---|---|
| Visual player, buttons, Components V2 | `Core/Discord/Embeds/` (`VisualPlayer`, `ComponentV2Builder`, `DiscordButtonBuilder`) |
| Slash commands and button handlers | `Core/Discord/Commands/MusicCommands.cs`, `Core/Discord/Interactions/MusicInteractionsHandler.cs` |
| Playback, queue, Lavalink | `Core/Services/LavaLink/` (`PlayerService`, `CustomLavaLinkPlayer`, `QueueResolveService`) |
| Plex API and caching | `Core/Services/PlexApi/`, `Core/Services/Music/` |
| Player image | `Utils/ImageBuilder.cs` |
| Events and extensions | `Core/Events/BotEventBus.cs`, `Core/Extensions/` |
| Startup and DI | `Main/ServiceRegistration.cs`, `Main/BotHostedService.cs` |

## Shipping a change

1. Branch from `main` with a descriptive name. Stacked branches are fine when a change depends on another; say so
   in the PR body.
2. Build and run `dotnet test PlexBot.sln`. Add tests for pure logic.
3. Rebuild the stack (`Install/start.sh --build`) and test live in KalebKorp `#testing` with the voice channel
   and microphone muted. Keep a note of what you checked.
4. Open the PR as a draft against `main`. The body says what changed, what was tested, and anything that changes
   outside the repo.
5. Resolve every review comment, then mark ready and merge. The author merges, not the agent.

## Replies

Keep answers terse. Do not restate what the diff shows. End a long answer or research session with a short
plain-English TL;DR.
