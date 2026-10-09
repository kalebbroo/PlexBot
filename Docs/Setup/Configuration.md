# PlexBot Configuration Guide

PlexBot uses two configuration files:

- **`.env`** — Secrets and infrastructure (tokens, server URLs, Lavalink connection). Never commit this file.
- **`config.fds`** — Application settings (player UI, logging, progress bar). Uses [Frenetic Data Syntax](https://github.com/FreneticLLC/FreneticUtilities) (YAML-like format).

Template files are provided: `RenameMe.env.txt` must be manually copied to `.env`. `config.fds` is **auto-created** from `RenameMe.config.fds` with sensible defaults if it doesn't exist when the bot starts.

---

## .env — Secrets & Infrastructure

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `DISCORD_TOKEN` | Your Discord bot token | Yes | N/A |
| `PLEX_URL` | Plex server URL with port | Yes | N/A |
| `PLEX_TOKEN` | Plex authentication token | Yes | N/A |
| `PLEX_CLIENT_IDENTIFIER` | Unique app identifier | No | Auto-generated |
| `PLEX_APP_NAME` | Display name for Plex | No | `PlexBot` |
| `LAVALINK_HOST` | Lavalink hostname — `Lavalink` for Docker, IP/hostname for remote | No | `Lavalink` |
| `LAVALINK_SERVER_PORT` | Lavalink server port | No | `2333` |
| `LAVALINK_SERVER_PASSWORD` | Lavalink password | No | `youshallnotpass` |
| `LAVALINK_SECURE` | Use HTTPS/WSS (set `true` for remote Lavalink behind SSL) | No | `false` |

---

## config.fds — Application Settings

### Visual Player Settings

```yaml
visualPlayer:
    useModernPlayer: true        # true = album art image player, false = classic text embed
    inactivityTimeout: 2.0       # Minutes before auto-disconnect from voice
    buttonCooldownSeconds: 2.0   # Seconds between presses of the same button by one person (0 = off)
    staticChannel:
        enabled: false           # Lock the player to one specific channel
        channelId: 0             # Discord channel ID (right-click channel > Copy Channel ID)
    progressBar:
        enabled: true            # Show live progress bar (updates every second)
        size: medium             # small (mobile), medium (default), large (wide displays)
        emoji:                   # See "Custom Progress Bar Emoji" section below
            bar_left_empty:
            # ... (30 emoji IDs total)
```

### Plex Settings

```yaml
plex:
    maxConcurrentResolves: 2     # Older name for stream.maxConcurrentLoads, used only when that is not set
    resolveCacheSize: 500        # Resolved tracks kept, keyed by Plex part
    resolveCacheMinutes: 60      # How long a resolved track is reused
    stream:
        maxConcurrentLoads: 2        # Plex file loads at once, shared by every server the bot is in
        retryDelaysSeconds: 2, 5, 15 # Wait before each retry of a failed load; all Plex loads pause meanwhile
        resolveAhead: 3              # Queued tracks loaded ahead of the one playing
    radio:
        infinite: false              # Keep radio going: queue more tracks from the seed when the queue runs low
        refillThreshold: 5           # Refill when fewer than this many tracks are queued
        batchSize: 30                # Tracks fetched per radio request (first batch and each refill)
```

Infinite radio runs until the player is stopped, killed, or the bot leaves voice. Tracks already queued in a session are not queued again.

**Static player channel.** Set `visualPlayer.staticChannel.enabled: true` and `channelId` to keep one player in one channel. When the bot starts it deletes its own old messages in that channel and posts the idle card, so use a channel for the player only.

Large playlists are queued straight away. Only the next few tracks (`resolveAhead`) are loaded through Lavalink, as they come up, so Plex sees requests only for tracks that are about to play. Plex drops file requests when too many run at once, which Lavalink reports as "Could not read the file for detecting file type" or "Premature end of Content-Length delimited message body". The bot retries those with the delays above. A track that still fails is removed from the queue with a short notice in the channel.

### Logging Settings

```yaml
logging:
    level: INFO                  # Console output: VERBOSE, DEBUG, INFO, WARN, ERROR
    saveToFile: true             # Log files always save ALL levels regardless
    path: logs/plex-bot-[year]-[month]-[day].log
```

Supported path placeholders: `[year]`, `[month]`, `[day]`, `[hour]`, `[minute]`, `[second]`, `[pid]`

### Bot Settings

```yaml
bot:
    environment:                 # Set to "Development" for guild-scoped commands (faster updates)
```

---

## Custom Progress Bar Emoji

PlexBot includes a smooth-fill progress bar made of 30 custom Discord emoji. Without these, the player falls back to unicode block characters (`▓░`) which work everywhere but look less polished.

### Setup Steps

1. **Open the Discord Developer Portal**
   Go to [discord.com/developers/applications](https://discord.com/developers/applications) and select your bot application.

2. **Navigate to Emojis**
   In the left sidebar, click **Emojis** (under the application settings, not a server).

3. **Upload all 30 images**
   Upload each `.png` file from the `Images/Icons/progress/` folder in the PlexBot project. The filenames are the emoji names — Discord will use them automatically.

   The 30 images are organized into three groups:

   | Group | Files | Count |
   |-------|-------|-------|
   | Left cap | `bar_left_empty`, `bar_left_filled_1` through `bar_left_filled_6`, `bar_left_filled` | 8 |
   | Middle | `bar_mid_empty`, `bar_filled_1` through `bar_filled_12`, `bar_mid_filled` | 14 |
   | Right cap | `bar_right_empty`, `bar_right_filled_1` through `bar_right_filled_6`, `bar_right_filled` | 8 |

4. **Copy each emoji's ID**
   After uploading, hover over each emoji in the Developer Portal and copy its numeric ID (or use the Discord API).

5. **Paste IDs into `config.fds`**
   Find the `visualPlayer.progressBar.emoji` section and paste each ID next to its matching name:

   ```yaml
   emoji:
       bar_left_empty: 1478623138618150993
       bar_left_filled_1: 1478623139972780063
       bar_left_filled_2: 1478623140966957197
       # ... continue for all 30 emoji
   ```

6. **Restart the bot**
   The bot will log whether custom emoji loaded successfully or fell back to unicode.

### Troubleshooting

- **"Progress bar emoji missing: bar_xxx"** — That emoji ID is empty in config.fds. All 30 must be provided.
- **"Invalid ID: 'abc'"** — The value isn't a numeric Discord emoji ID. Copy just the number, not the full `<:name:id>` format.
- **Emoji show as broken squares in Discord** — The emoji were deleted from the application, or the bot doesn't own them. Re-upload and update the IDs.
- **Unicode fallback is fine** — If you don't want to set up custom emoji, just leave all the IDs empty. The unicode `▓░` bar works in all Discord clients.

---

## Docker Configuration

In Docker, `.env` is mounted directly into the container. `config.fds` is handled automatically by the startup script — if you have a `config.fds` at the project root, it's copied into the container. If not, one is created from the template with defaults.

After changing either file, restart the bot:

```bash
docker-compose down
docker-compose up -d
```

---

## Permissions

The Discord bot requires the following permissions:

```
- Read Messages/View Channels
- Send Messages
- Send Messages in Threads
- Embed Links
- Attach Files
- Read Message History
- Add Reactions
- Use Slash Commands
- Connect (to voice channels)
- Speak (in voice channels)
```

Permission integer: `277062627904`
