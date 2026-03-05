# Player UI Guide

## Overview

PlexBot offers two player UI styles and a configurable progress bar. All player settings live in `config.fds` (not `.env`).

## Player Styles

### 1. Modern Visual Player (Default)

A rich image-based player that uses album artwork as the background with track info overlaid using ImageSharp rendering.

- Album artwork fills the player background
- Track title, artist, and album overlaid on the image
- Volume level and repeat mode shown on the image itself
- Uses Discord's Components V2 container system
- Requires font packages installed in Docker (handled automatically by the Dockerfile)

### 2. Classic Embed Player

A lightweight text-based player using Discord's native embed system.

- Standard Discord embed with text fields
- Album artwork shown as a small thumbnail
- Lower CPU usage (no image rendering)
- Works well in multi-purpose channels

## Configuration

All player settings are in `config.fds`:

```yaml
visualPlayer:
    useModernPlayer: true       # true = modern image player, false = classic embed
    inactivityTimeout: 2.0      # Minutes of silence before auto-disconnect
    staticChannel:
        enabled: false          # Lock player to one channel
        channelId: 0            # Discord channel ID
    progressBar:
        enabled: true           # Show live progress bar (updates every second)
        size: medium            # small / medium / large
```

| Key | Default | Description |
|-----|---------|-------------|
| `visualPlayer.useModernPlayer` | `true` | `true` = modern image player, `false` = classic embed |
| `visualPlayer.inactivityTimeout` | `2.0` | Minutes of silence before the bot auto-disconnects |
| `visualPlayer.staticChannel.enabled` | `false` | Lock the player to a single channel |
| `visualPlayer.staticChannel.channelId` | `0` | The Discord channel ID for the static player |

## Static Player Channel

When enabled, the player always appears in the designated channel regardless of where commands are used. The bot creates a placeholder message on startup and updates it as tracks change.

To set up:
1. Create a dedicated text channel in your Discord server
2. Right-click the channel and select **Copy Channel ID** (requires Developer Mode in Discord settings)
3. Set the values in `config.fds`:
   ```yaml
   visualPlayer:
       staticChannel:
           enabled: true
           channelId: 123456789012345678
   ```

## Progress Bar

The progress bar shows a live-updating track position that refreshes every second.

### Size Options

| Size | Segments | Best For |
|------|----------|----------|
| `small` | 10 | Mobile / narrow displays |
| `medium` | 16 | Default — works well on most screens |
| `large` | 22 | Desktop / wide displays |

### Custom Emoji vs Unicode Fallback

- **Custom emoji** (30 Discord application emoji) provide a smooth-fill progress bar with partial fill levels
- **Unicode fallback** (`▓░`) works everywhere but only supports filled/empty (no partial fill)

To use custom emoji, upload the images from `Images/Icons/progress/` to your bot application in the [Discord Developer Portal](https://discord.com/developers/applications) and paste the IDs into `config.fds`. See the [Configuration Guide](../Setup/Configuration.md) for a detailed walkthrough.

To disable the progress bar entirely (reduces Discord API calls):
```yaml
visualPlayer:
    progressBar:
        enabled: false
```

## Player Controls

The player includes interactive buttons:

| Button | Action |
|--------|--------|
| Pause/Resume | Toggle playback |
| Skip | Skip to next track |
| Repeat | Cycle: Off → Queue → Track |
| Shuffle | Shuffle the current queue |
| Volume Up/Down | Adjust volume by 10% |
| Stop | Stop playback and disconnect |
| Queue | View and manage the queue |

## Troubleshooting

### Visual Player Shows No Text or Broken Image
- Rebuild the Docker container to ensure font packages are installed: `docker-compose up -d --build`
- The Dockerfile installs DejaVu, Liberation, Noto (including CJK and emoji) fonts automatically

### Static Player Not Appearing
- Verify the channel ID is correct (must be the numeric ID, not the channel name)
- Ensure the bot has **Send Messages** and **Attach Files** permissions in the channel
- Check that `staticChannel.enabled` is `true` in `config.fds`

### Progress Bar Not Showing
- Check that `progressBar.enabled` is `true` in `config.fds`
- If using custom emoji, ensure all 30 IDs are present and valid — missing IDs cause a fallback to unicode
