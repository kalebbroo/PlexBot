# PlexBot Commands Guide

## Overview

PlexBot uses Discord's slash command system. Type `/` in any channel to see available commands. All playback controls (pause, skip, volume, etc.) are handled through interactive buttons on the player — not separate slash commands.

## Slash Commands

### `/search`

Search your Plex library or YouTube for music.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `query` | Yes | What to search for |
| `source` | No | Where to search — `plex` (default) or `youtube` |

**Example:** `/search query:The Beatles source:plex`

Returns interactive select menus for browsing artists, albums, and tracks. Select an item to play it or add it to the queue.

### `/playlist`

Load and play a Plex playlist.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `playlist` | Yes | Playlist name (autocompletes from your Plex playlists) |
| `shuffle` | No | Shuffle the playlist before playing (default: `true`) |

**Example:** `/playlist playlist:Summer Hits shuffle:true`

### `/play`

Quick play — searches your Plex library and plays the first match.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `query` | Yes | Track, album, or artist name to search for |

**Example:** `/play query:Bohemian Rhapsody`

Searches in order: tracks, then albums, then artists. Plays the first match found.

### `/help`

Displays an interactive help embed with all available commands and player controls.

## Player Controls (Buttons)

All playback controls are buttons on the player message itself — no slash commands needed.

| Button | Action |
|--------|--------|
| Pause / Resume | Toggle playback |
| Skip | Skip to next track in queue |
| Repeat | Cycle repeat mode: Off → Queue → Track → Off |
| Shuffle | Shuffle the current queue |
| Volume Up | Increase volume by 10% |
| Volume Down | Decrease volume by 10% |
| Stop | Stop playback and disconnect from voice |
| Queue | View and manage the queue |

### Queue Options

The queue button opens a panel with additional controls:

- **View Queue** — Shows the current queue with track listing
- **Shuffle** — Randomize queue order
- **Clear** — Remove all tracks from the queue

## Interaction Cooldown

All button interactions have a 2-second cooldown to prevent spam. If you click too fast, the bot will briefly ignore repeated presses.

## Command Not Appearing?

- **Guild-scoped commands** update instantly during development (set `bot.environment: Development` in `config.fds`)
- **Global commands** can take up to 1 hour to propagate across Discord
- Make sure the bot has the **Use Slash Commands** permission in your server

## Additional Resources

- [Player UI Guide](./Player-UI-Guide.md) — Player styles and progress bar configuration
- [Plex Integration Guide](./Plex-Integration.md) — Plex setup and library search tips
- [Troubleshooting Guide](./Troubleshooting.md) — Common issues and fixes
