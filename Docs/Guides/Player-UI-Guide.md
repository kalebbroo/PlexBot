# Player UI Guide

## Overview

PlexBot offers two distinct player UI styles designed to enhance your Discord music experience. This guide covers both styles and how to configure them for your server.

![Player Examples](../images/player-examples.png)

## Player Styles

PlexBot supports two different player UI styles:

### 1. Visual Player (Default)

The visual player provides a rich graphical experience with album artwork as the background and text overlay.

**Features:**
- Displays album artwork as background
- Overlays track information (artist, title, album, etc.) directly on the image
- Visually appealing design for music channels

**Example:**
```
Now Playing:
[Visual Player with Album Art and Track Details]
```

### 2. Classic Embed Player

The classic embed player uses Discord's native embed system with a thumbnail for album art.

**Features:**
- Standard Discord embed format
- Album artwork displayed as thumbnail
- Track information in text fields
- Lighter on resources

**Example:**
```
Now Playing:
Artist: Example Artist
Title: Example Song
Album: Example Album
```

## Configuration

You can configure your preferred player style using environment variables:

### Environment Variables

| Variable | Description | Default | Options |
|----------|-------------|---------|---------|
| `PLAYER_STYLE_VISUAL` | Enables visual player with album artwork background | `true` | `true`/`false` |

Add these variables to your `.env` file:

```bash
# Player UI Configuration
PLAYER_STYLE_VISUAL=true  # Set to false for classic embed style
```

## Static Player Channel

PlexBot can maintain a static player in a designated channel, regardless of where commands are issued.

### Features:
- Fixed player display in a designated channel
- Automatically updates when tracks change
- Creates placeholder message on bot startup

### Configuration:

| Variable | Description | Default | Options |
|----------|-------------|---------|---------|
| `USE_STATIC_PLAYER_CHANNEL` | Enables static player channel | `false` | `true`/`false` |
| `STATIC_PLAYER_CHANNEL_ID` | Discord channel ID for static player | N/A | Valid Discord channel ID |

Add these variables to your `.env` file:

```bash
# Static Player Configuration
USE_STATIC_PLAYER_CHANNEL=true
STATIC_PLAYER_CHANNEL_ID=123456789012345678  # Replace with your channel ID
```

## Example Setup

Complete `.env` configuration example:

```bash
# Basic Bot Configuration
DISCORD_TOKEN=your_discord_token_here
DISCORD_APPLICATION_ID=your_application_id_here

# Player UI Configuration
PLAYER_STYLE_VISUAL=true
USE_STATIC_PLAYER_CHANNEL=true
STATIC_PLAYER_CHANNEL_ID=123456789012345678
```

## Troubleshooting

### Visual Player Shows No Album Art
- Ensure the bot has access to the internet to download album artwork
- Check that the Docker container has font packages installed
- Verify that your `.env` file has `PLAYER_STYLE_VISUAL=true`

### Static Player Not Appearing
- Verify that the channel ID is correct
- Ensure the bot has permissions to send messages in the designated channel
- Check that both `USE_STATIC_PLAYER_CHANNEL=true` and `STATIC_PLAYER_CHANNEL_ID` are set
