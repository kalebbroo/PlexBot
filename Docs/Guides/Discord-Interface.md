# Discord Interface Guide

## Overview

PlexBot integrates seamlessly with Discord's interface to provide an intuitive music experience. This guide covers all aspects of the bot's Discord interface elements, including the interactive player, buttons, and UI customization.

![Discord Interface](../images/discord-interface.png)

## Interactive Player

PlexBot features an interactive player that displays in Discord with real-time information and controls.

### Player Display Types

#### Visual Player (Default)

The visual player displays album artwork as a background with text overlay:

```
[Image with album artwork and overlaid text showing:]
Artist: Daft Punk
Title: Get Lucky
Album: Random Access Memories
Studio: Columbia Records
Duration: 6:08
```

#### Classic Embed Player

The classic embed player uses Discord's native embed system:

```
Now Playing:
Artist: Daft Punk
Title: Get Lucky
Album: Random Access Memories
Studio: Columbia Records
Duration: 6:08
[Album artwork thumbnail]
```

### Interactive Buttons

The player includes interactive buttons for controlling playback:

| Button | Function | Description |
|--------|----------|-------------|
| ⏯️ | Play/Pause | Toggle between play and pause states |
| ⏭️ | Skip | Skip to the next track in queue |
| 🔁 | Loop | Cycle through loop modes (off, track, queue) |
| 🔀 | Shuffle | Shuffle the current queue |
| 🔊 | Volume | Open volume controls |
| ❌ | Disconnect | Stop playback and disconnect bot |

### Button Interactions

Clicking a button triggers an immediate response:

1. **Instant Feedback**: The button shows a loading state while processing
2. **Visual Confirmation**: The player updates to reflect the new state
3. **User Notification**: Brief confirmation message appears

## Queue Display

Viewing your music queue provides a paginated display of upcoming tracks:

```
Current Queue (15 tracks):
→ Now Playing: Get Lucky - Daft Punk (6:08)
#1 Uptown Funk - Mark Ronson ft. Bruno Mars (4:30)
#2 Blinding Lights - The Weeknd (3:20)
#3 Seven Nation Army - The White Stripes (3:51)
#4 Sweet Child O' Mine - Guns N' Roses (5:56)

Page 1/3 • Use the buttons below to navigate
```

### Queue Controls

The queue display includes navigation buttons:

| Button | Function |
|--------|----------|
| ⬅️ | Previous Page |
| ➡️ | Next Page |
| 🔄 | Refresh Queue |
| 🗑️ | Clear Queue |

## Static Player Channel

When enabled, the static player channel provides a persistent display that automatically updates with the current playback status.

### Features:

- **Persistent Presence**: The player remains in a dedicated channel
- **Auto-Updates**: Updates automatically when tracks change
- **Server Focal Point**: Provides a central location for music control

### Example:

```
#music-player channel:

[Visual Player Display]
[Interactive Buttons]

Use /play in any channel to add songs to the queue!
```

## Slash Commands Interface

PlexBot uses Discord's slash command system for intuitive interaction:

1. **Command Autocomplete**: Type `/` to see all available commands
2. **Parameter Helpers**: Discord shows required and optional parameters
3. **Inline Documentation**: Brief descriptions appear while typing

### Example Slash Command Flow:

```
/play query:_
```

As you type, Discord shows autocomplete suggestions based on your input.

## Direct Message Interface

PlexBot can also respond to direct messages for private interaction:

- **Help Information**: Get command help without channel clutter
- **Private Feedback**: Receive notifications about server-based actions
- **User Settings**: Configure personal preferences

## Search Results Interface

When searching for music, PlexBot presents results in a clean, interactive format:

```
Search Results for "rock classics":

1. We Will Rock You - Queen (2:02)
2. Sweet Child O' Mine - Guns N' Roses (5:56)
3. Smoke on the Water - Deep Purple (5:40)
4. Back in Black - AC/DC (4:15)
5. Bohemian Rhapsody - Queen (5:55)

Select a track by clicking the corresponding button or typing its number.
```

## UI Customization

### Permissions Setup

Proper permissions ensure the bot functions correctly in your server:

1. **Required Permissions**:
   - Send Messages
   - Embed Links
   - Attach Files
   - Use External Emojis
   - Add Reactions
   - Use Application Commands
   - Connect to Voice Channels
   - Speak in Voice Channels

2. **Recommended Permissions**:
   - Manage Messages (for clearing outdated player messages)
   - Create Public Threads (for track discussions)

### Channel Configuration

For optimal use, consider these channel configurations:

1. **Dedicated Bot Channels**:
   - Create a `#music-commands` channel for bot interaction
   - Set up a `#now-playing` channel as your static player channel

2. **Permission Overwrites**:
   - Allow bot to post in channels where others cannot
   - Restrict music commands to specific channels

## Troubleshooting Interface Issues

### Buttons Not Responding

If interactive buttons stop working:

1. Check bot permissions (especially "Use External Emojis")
2. Verify the bot is online and responsive
3. Try using the equivalent slash command
4. Restart the interaction with a new command

### Missing Player Elements

If player displays incorrectly:

1. Ensure bot has "Embed Links" and "Attach Files" permissions
2. Check if visual player is enabled (`PLAYER_STYLE_VISUAL=true`)
3. Verify the bot can access album artwork URLs
4. Confirm font packages are installed in Docker

## Mobile Interface

PlexBot is fully compatible with Discord mobile:

- All buttons and commands work on mobile devices
- Visual player scales to mobile screen size
- Touch interface works with all interactive elements

## Best Practices

1. **Dedicated Channels**: Create specific channels for bot commands and player display
2. **Clear Instructions**: Pin a message with basic commands in your music channel
3. **Proper Permissions**: Ensure the bot has all necessary permissions
4. **Regular Updates**: Keep both Discord and PlexBot updated

## Community Features

Create a more engaging music experience:

1. **Listening Parties**: Schedule events for group listening
2. **Music Discussions**: Create threads from the player message to discuss tracks
3. **Song Requests**: Designate channels for members to request songs
4. **DJ Role**: Create a special role for members who can control music

## Related Guides

- [Commands Guide](./Commands.md)
- [Player UI Guide](./Player-UI-Guide.md)
- [Troubleshooting Guide](./Troubleshooting.md)
