# Plex Media Server Integration Guide

## Overview

PlexBot streams music from your Plex Media Server into Discord voice channels via Lavalink. Plex serves the audio files, Lavalink decodes and streams them to Discord, and PlexBot handles the UI and commands.

## Setup

### Prerequisites

- A running Plex Media Server with a music library
- A Plex authentication token
- PlexBot and Lavalink running (see [Installation Guide](../Setup/Installation.md))

### Configuration

Add these to your `.env` file:

```env
PLEX_URL=http://192.168.1.100:32400
PLEX_TOKEN=your_plex_token_here
```

| Variable | Description | Required |
|----------|-------------|----------|
| `PLEX_URL` | Full URL to your Plex server including port | Yes |
| `PLEX_TOKEN` | Your Plex authentication token | Yes |

### Finding Your Plex Token

See [Plex's official guide](https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/).

Quick method:
1. Sign in to the Plex web app
2. Open any media item, click the three-dot menu, then **Get Info**
3. Click **View XML** — your token is the `X-Plex-Token` parameter in the URL

## Searching Your Library

Use `/search` to find music in your Plex library:

```
/search query:The Beatles source:plex
```

This returns interactive select menus for:
- **Artists** — select to browse their albums and tracks
- **Albums** — select to queue the full album
- **Tracks** — select to play or queue individual tracks

The `source` parameter defaults to `plex`, so you can also just use:
```
/search query:The Beatles
```

## Playing Music

### Quick Play

`/play` searches your Plex library and plays the first match:

```
/play query:Bohemian Rhapsody
```

Searches tracks first, then albums, then artists.

### Playlists

`/playlist` loads a full Plex playlist:

```
/playlist playlist:Summer Hits shuffle:true
```

The `playlist` parameter autocompletes from your Plex playlists. Shuffle is enabled by default.

### Playback Controls

All playback is controlled through buttons on the player message — not slash commands. See the [Commands Guide](./Commands.md) for the full button reference.

## Playlist Loading & Concurrency

When loading large playlists, PlexBot resolves tracks in parallel through Lavalink. The concurrency is configurable in `config.fds`:

```yaml
plex:
    maxConcurrentResolves: 3
```

- **Lower values** (1-2): Safer for Plex servers with limited resources, but slower
- **Higher values** (4-5): Faster loading, but may overwhelm Plex — you'll see "Failed to load" messages if Plex drops connections
- **Default (3)**: Good balance for most setups

Failed tracks are automatically retried once after a delay. Any permanently failed tracks are listed in the status embed.

## Troubleshooting

### Cannot Connect to Plex Server

- Verify `PLEX_URL` in `.env` is correct and includes the port (default: `32400`)
- Make sure the Plex server is reachable from the machine running PlexBot
- Test: `curl -H "X-Plex-Token: YOUR_TOKEN" http://your-plex-ip:32400`

### Tracks Fail to Load from Playlists

- Lower `plex.maxConcurrentResolves` in `config.fds` — Plex drops connections under heavy concurrent load
- Check `logs/` for detailed error messages
- Ensure Plex is serving files directly (Direct Play) — transcoding adds load

### Authentication Errors

- Regenerate your Plex token and update `.env`
- Make sure the token belongs to an account with access to the music library

## Best Practices

- **Same network**: Run PlexBot on the same network as Plex for fastest file serving
- **Direct Play**: Ensure Plex serves raw audio files (no transcoding) to minimize server load
- **Library metadata**: Well-tagged music with proper artist/album/title metadata improves search results

## Related Guides

- [Commands Guide](./Commands.md)
- [Player UI Guide](./Player-UI-Guide.md)
- [Troubleshooting Guide](./Troubleshooting.md)
