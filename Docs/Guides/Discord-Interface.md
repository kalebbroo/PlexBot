# Discord Interface Guide

## Overview

PlexBot uses Discord's Components V2 system for its player UI — a container-based layout with images, text displays, and interactive buttons. This guide covers how the interface works and how to set it up for your server.

## Player Display

### Modern Visual Player (Default)

An image-based player rendered with ImageSharp:
- Album artwork fills the background
- Track title, artist, album, and duration overlaid on the image
- Volume level and repeat mode indicators rendered on the image
- Sent as a PNG attachment inside a Components V2 container

### Classic Embed Player

A text-based player using Discord's native components:
- Track info displayed as text with a thumbnail accessory for album art
- Lower CPU usage (no image rendering)
- Works well in multi-purpose channels

Switch between them in `config.fds`:
```yaml
visualPlayer:
    useModernPlayer: true   # false for classic embed
```

## Interactive Buttons

The player message includes a row of buttons for playback control:

| Button | Action | Details |
|--------|--------|---------|
| Pause / Resume | Toggle playback | Icon changes to reflect current state |
| Skip | Next track | Skips to the next track in queue |
| Repeat | Cycle repeat mode | Off → Queue → Track → Off |
| Shuffle | Shuffle queue | Randomizes remaining queue order |
| Volume Up | +10% volume | Capped at 100% |
| Volume Down | -10% volume | Minimum 0% |
| Stop | Disconnect | Stops playback, clears queue, leaves voice |
| Queue | Queue panel | Opens queue view with additional options |

### Queue Panel

The queue button opens a panel with:
- **View Queue** — Paginated list of upcoming tracks
- **Shuffle** — Randomize the queue
- **Clear** — Remove all tracks from the queue

### Interaction Cooldown

All button interactions have a **2-second cooldown** per user to prevent spam. Rapid clicks are silently ignored.

## Progress Bar

A live-updating progress bar shows track position (refreshes every second):

```
` 1:23 `▓▓▓▓▓▓▓░░░░░░░░░` 3:45 `
```

With custom emoji configured, the bar has smooth partial-fill levels for a polished look. See the [Player UI Guide](./Player-UI-Guide.md) for setup instructions.

The bar width is configurable (`small` / `medium` / `large`) in `config.fds` to accommodate different screen sizes.

## Static Player Channel

When enabled, the player locks to a single channel and updates in place as tracks change:

- The bot creates a placeholder message on startup
- All player updates go to this channel regardless of where commands are used
- Commands can still be used from any channel

Configure in `config.fds`:
```yaml
visualPlayer:
    staticChannel:
        enabled: true
        channelId: 123456789012345678
```

### Recommended Channel Setup

1. Create a dedicated `#music-player` channel
2. Set the bot's permissions to **Send Messages** and **Attach Files**
3. Optionally restrict other users from posting in the channel to keep it clean

## Search Results

The `/search` command returns interactive select menus:
- Results are grouped by type (Artists, Albums, Tracks)
- Select an item from the dropdown to play it or browse deeper
- YouTube results appear as a separate menu when `source:youtube` is used

## Required Bot Permissions

The bot needs these Discord permissions to function:

- Read Messages / View Channels
- Send Messages
- Embed Links
- Attach Files
- Read Message History
- Use Slash Commands
- Connect (voice)
- Speak (voice)

Permission integer: `277062627904`

## Mobile Compatibility

- All buttons work on Discord mobile
- The `small` progress bar size is recommended for mobile users to avoid line wrapping
- Modern visual player images scale to mobile screen width automatically

## Related Guides

- [Commands Guide](./Commands.md)
- [Player UI Guide](./Player-UI-Guide.md)
- [Troubleshooting Guide](./Troubleshooting.md)
