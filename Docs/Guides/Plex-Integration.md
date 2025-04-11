# Plex Media Server Integration Guide

## Overview

PlexBot offers seamless integration with your Plex Media Server, allowing you to stream your personal music library directly to Discord voice channels. This guide will walk you through setting up and using this integration.

![Plex Integration](../images/plex-integration.png)

## Setup

### Prerequisites

- A running Plex Media Server
- A Plex authentication token
- Music library configured in Plex

### Configuration

Add the following variables to your `.env` file:

```bash
# Plex Configuration
PLEX_SERVER=http://your-plex-server:32400
PLEX_TOKEN=your_plex_token_here
PLEX_LIBRARY_SECTION=Music
```

| Variable | Description | Example |
|----------|-------------|---------|
| `PLEX_SERVER` | URL to your Plex server including port | `http://192.168.1.100:32400` |
| `PLEX_TOKEN` | Your Plex authentication token | `tH1s1sAn3xAmpL3pL3xT0k3n` |
| `PLEX_LIBRARY_SECTION` | Name of your music library in Plex | `Music` |

### Finding Your Plex Token

1. Sign in to Plex web app
2. Select any media item and click the ⋮ (three dots) menu
3. Click "Get Info"
4. Click the "View XML" link
5. In the URL that opens, your token is the `X-Plex-Token` parameter

## Using Plex With PlexBot

### Searching Plex Library

Use the `/plex search` command to find music in your Plex library:

```
/plex search query:<search term>
```

Example:
```
/plex search query:The Beatles
```

### Playing from Plex

#### Play a Specific Track

```
/plex play track:<track name> artist:<artist name>
```

Example:
```
/plex play track:Hey Jude artist:The Beatles
```

#### Play an Album

```
/plex play album:<album name> artist:<artist name>
```

Example:
```
/plex play album:Abbey Road artist:The Beatles
```

#### Play an Artist's Music

```
/plex play artist:<artist name>
```

Example:
```
/plex play artist:The Beatles
```

### Managing Plex Playback

The standard playback commands work with Plex content:

- `/pause` - Pause current playback
- `/resume` - Resume playback
- `/skip` - Skip to next track
- `/stop` - Stop playback completely
- `/queue` - View current queue

## Advanced Features

### Plex Playlists

Play playlists directly from your Plex server:

```
/plex playlist name:<playlist name>
```

Example:
```
/plex playlist name:My Workout Mix
```

### Shuffle Mode

Enable shuffle mode for Plex playback:

```
/plex shuffle artist:<artist name>
```

Example:
```
/plex shuffle artist:The Beatles
```

### Limiting Results

Limit the number of tracks played:

```
/plex play artist:<artist name> limit:10
```

Example:
```
/plex play artist:The Beatles limit:5
```

## Troubleshooting

### Cannot Connect to Plex Server

**Check server URL and port:**
- Ensure the server address in your `.env` file is correct
- Verify the server is accessible from the machine running PlexBot

**Test connection:**
```bash
curl -H "X-Plex-Token: YOUR_TOKEN" http://your-plex-server:32400
```

### Authentication Issues

If you see "Unauthorized" or "Invalid token" errors:
- Verify your Plex token is correct
- Ensure your Plex account has access to the server
- Try generating a new token

### Track Not Found

If PlexBot cannot find requested tracks:
- Check if the track exists in your Plex library
- Verify the library section name in your configuration
- Try using more specific search terms

### Playback Issues

If tracks are found but won't play:
- Check if your Plex server is configured for remote access
- Ensure the network allows connections between PlexBot and your Plex server
- Verify that direct play is enabled for your music files

## Best Practices

1. **Local Network:** For best performance, run PlexBot on the same network as your Plex server

2. **Library Organization:** Keep your Plex music library well-organized with proper metadata

3. **Remote Access:** If accessing Plex remotely, ensure your server has adequate upload bandwidth

4. **Regular Updates:** Keep both Plex and PlexBot updated to ensure compatibility

## Example Usage Scenarios

### Discord Party with Your Music

```
# Join a voice channel first
/join

# Start playing from your favorite playlist
/plex playlist name:Party Mix

# Adjust volume as needed
/volume 80

# Skip tracks that don't fit the mood
/skip
```

### Music Discovery Session

```
# Join a voice channel
/join

# Play random tracks from an artist
/plex shuffle artist:Radiohead limit:10

# Share track information with friends
/np
```

## Related Guides

- [Basic Commands Guide](./Commands.md)
- [Player UI Guide](./Player-UI-Guide.md)
- [Troubleshooting Guide](./Troubleshooting.md)
