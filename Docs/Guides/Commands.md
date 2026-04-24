# PlexBot Commands Guide

## Overview

PlexBot uses Discord's slash command system. Type `/` in any channel to see available commands. All playback controls (pause, skip, volume, etc.) are handled through interactive buttons on the player — not separate slash commands.

## Slash Commands

### `/search`

Unified search across all sources and Plex sonic features.

| Parameter | Required | Description |
|-----------|----------|-------------|
| `mode` | Yes | Where and how to search (autocomplete dropdown) |
| `query` | Yes | What to search for — for Mood, Genre, and Radio modes the autocomplete populates with choices from your library |

**Modes:**

| Mode | What it does | Query autocomplete |
|------|-------------|-------------------|
| **Plex Library** | Standard library search — returns artists, albums, and tracks | Free text (hint shown) |
| **Find by Mood** | Browse tracks matching a mood tag (e.g. "Happy", "Aggressive") | Randomized sample of 25 moods; type to filter |
| **Find by Genre** | Browse tracks matching a genre (e.g. "Rock", "Jazz") | Lists all available genres |
| **Radio Station** | Pick a station or search for a track to seed radio from | Lists available stations |
| *Extension providers* | Any loaded extensions (YouTube, SoundCloud, etc.) appear automatically in the dropdown | Free text (hint shown) |

> **Note:** Similar Tracks and Sonic Adventure are available as buttons on the Visual Player (see [Player Controls](#player-controls-buttons) below), not as search modes. They require a currently playing Plex track as context.

**Examples:**
- `/search mode:Plex Library query:The Beatles` — Search Plex for The Beatles
- `/search mode:Find by Mood query:Happy` — Pick a mood from autocomplete or type one
- `/search mode:Find by Genre query:Rock` — Pick a genre from autocomplete or type one
- `/search mode:Radio Station query:Library Radio` — Pick a station from autocomplete
- `/search mode:Radio Station query:Bohemian Rhapsody` — Seed radio from a track search

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

### `/ping`

A simple test command to verify the bot is responding to interactions.

**Example:** `/ping`

Returns a response confirming the bot is online and interactions are working.

## Player Controls (Buttons)

All playback controls are buttons on the player message itself — no slash commands needed.

| Button | Action |
|--------|--------|
| Pause / Resume | Toggle playback |
| Skip | Skip to next track in queue |
| Repeat | Cycle repeat mode: Off → Queue → Track → Off |
| Queue Options | View queue, shuffle, or clear |
| Volume Up / Down | Adjust volume by 10% |
| Radio 📻 | Start a radio station from the current Plex track (replace queue, append, or browse similar tracks) |
| Similar 🔍 | Show 25 sonically similar tracks to the currently playing Plex track |
| Adventure 🧭 | Opens a popup to type a destination track — builds a sonic path from what's playing to the destination |
| Kill | Stop playback, clear queue, and disconnect from voice |

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
