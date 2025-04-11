# PlexBot Commands Guide

## Overview

This guide covers all available commands in PlexBot, organized by category. PlexBot uses Discord's slash command system for easy interaction.

## Getting Started

To use PlexBot commands, type `/` in your Discord server and select from the available commands. All commands are prefixed with `/` and include helpful descriptions.

## Basic Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/help` | Display help information | `/help` |
| `/ping` | Check if the bot is responsive | `/ping` |
| `/info` | Display information about the bot | `/info` |

## Music Control Commands

### Playback Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/play` | Play a song from YouTube/Spotify/URL | `/play query:bohemian rhapsody` |
| `/pause` | Pause the current playback | `/pause` |
| `/resume` | Resume paused playback | `/resume` |
| `/stop` | Stop playback and clear queue | `/stop` |
| `/skip` | Skip the current track | `/skip` |
| `/back` | Go back to the previous track | `/back` |
| `/seek` | Seek to a specific position | `/seek position:1:30` |
| `/nowplaying` or `/np` | Show current playing track | `/nowplaying` |

### Queue Management

| Command | Description | Example |
|---------|-------------|---------|
| `/queue` | Display the current queue | `/queue` |
| `/clear` | Clear the current queue | `/clear` |
| `/remove` | Remove a track from the queue | `/remove position:3` |
| `/shuffle` | Shuffle the current queue | `/shuffle` |
| `/loop` | Toggle track or queue looping | `/loop mode:track` |

### Volume Control

| Command | Description | Example |
|---------|-------------|---------|
| `/volume` | Set or check playback volume (0-100) | `/volume level:75` |

## Voice Channel Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/join` | Join your voice channel | `/join` |
| `/leave` | Leave the voice channel | `/leave` |
| `/move` | Move to a different voice channel | `/move channel:Music Room` |

## Plex Integration Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/plex search` | Search your Plex library | `/plex search query:jazz` |
| `/plex play` | Play from your Plex library | `/plex play artist:Queen` |
| `/plex playlist` | Play a Plex playlist | `/plex playlist name:Favorites` |
| `/plex shuffle` | Shuffle play from Plex | `/plex shuffle artist:Metallica` |

*For detailed Plex command usage, see the [Plex Integration Guide](./Plex-Integration.md)*

## Music Provider Commands

### YouTube Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/youtube` or `/yt` | Play from YouTube | `/youtube query:music video` |
| `/ytsearch` | Search YouTube | `/ytsearch query:vevo top hits` |

### Spotify Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/spotify` | Play from Spotify | `/spotify query:discover weekly` |
| `/spotify playlist` | Play a Spotify playlist | `/spotify playlist url:https://open.spotify.com/playlist/...` |
| `/spotify album` | Play a Spotify album | `/spotify album url:https://open.spotify.com/album/...` |

## Player Settings Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/settings player` | View/change player settings | `/settings player` |
| `/settings visual` | Toggle visual player on/off | `/settings visual enabled:true` |

## Administrative Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/admin` | Access admin commands | `/admin` |
| `/admin channel setup` | Set up static player channel | `/admin channel setup channel:music-bot` |
| `/admin restart` | Restart the bot | `/admin restart` |

## Command Parameters

Most commands accept parameters to customize their behavior:

### Example Parameter Types:

- **Text:** Free text input like search queries
- **Number:** Numeric values like volume level
- **Boolean:** True/false toggles
- **Channel:** Discord channel selection
- **User:** Discord user selection

### Parameter Examples:

```
/play query:something worth fighting for   # Text parameter
/volume level:75                          # Number parameter
/settings visual enabled:true             # Boolean parameter
/move channel:General                     # Channel parameter
```

## Advanced Usage

### Command Combinations

Chain commands together for efficient control:

```
# Start a music session:
/join
/play query:welcome to the jungle
/volume level:80

# Manage playback:
/pause
/queue
/skip
/resume
```

### URL Playback

Play directly from supported URLs:

```
/play query:https://www.youtube.com/watch?v=dQw4w9WgXcQ
/play query:https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT
```

## Troubleshooting Command Issues

If commands aren't working:

1. **Check Bot Permissions:**
   - Ensure the bot has necessary permissions in your server and channels

2. **Command Cooldowns:**
   - Some commands may have cooldowns to prevent abuse

3. **Parameter Requirements:**
   - Make sure you're providing all required parameters

4. **Slash Command Syncing:**
   - If new commands aren't appearing, the bot may need to re-sync with Discord
   - This happens automatically but may take time

## Command Examples with Responses

### Playing Music

Command:
```
/play query:despacito
```

Response:
```
Added to queue: Despacito - Luis Fonsi ft. Daddy Yankee
Position: #1 • Duration: 4:41
```

### Checking Queue

Command:
```
/queue
```

Response:
```
Current Queue (3 tracks):
→ Now Playing: Despacito - Luis Fonsi ft. Daddy Yankee (4:41)
#1 Shape of You - Ed Sheeran (3:53)
#2 Uptown Funk - Mark Ronson ft. Bruno Mars (4:30)

Page 1/1
```

### Volume Control

Command:
```
/volume level:75
```

Response:
```
Volume set to 75%
```

## Keyboard Shortcuts

When interacting with music player messages, you can use these reactions for quick control:

- ⏯️ - Toggle Play/Pause
- ⏭️ - Skip
- ⏮️ - Previous
- 🔁 - Toggle Loop
- 🔀 - Shuffle
- ❌ - Stop

## Command Permissions

Some commands require specific permissions:

- **Basic Commands:** Available to all users
- **Playback Commands:** Available to users in the same voice channel
- **Administrative Commands:** Restricted to server administrators

## Additional Resources

- [Player UI Guide](./Player-UI-Guide.md)
- [Plex Integration Guide](./Plex-Integration.md)
- [Troubleshooting Guide](./Troubleshooting.md)
