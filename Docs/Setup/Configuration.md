# PlexBot Configuration Guide

This guide provides comprehensive information about all available configuration options for PlexBot.

## Environment Variables

PlexBot uses environment variables for configuration. These can be set in the `.env` file in the root directory.

### Core Configuration

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `DISCORD_TOKEN` | Your Discord bot token | Yes | N/A |
| `DISCORD_APPLICATION_ID` | Your Discord application ID | Yes | N/A |
| `PREFIX` | Command prefix for text commands | No | `!` |
| `LOG_LEVEL` | Determines the verbosity of logs | No | `INFO` |

### Plex Integration

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `PLEX_SERVER` | URL to your Plex server including port | For Plex features | N/A |
| `PLEX_TOKEN` | Authentication token for your Plex server | For Plex features | N/A |
| `PLEX_LIBRARY_SECTION` | Name of the music library section | No | `Music` |

### Player UI Configuration

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `PLAYER_STYLE_VISUAL` | Use visual player with album art background | No | `true` |
| `USE_STATIC_PLAYER_CHANNEL` | Maintain player in a fixed channel | No | `false` |
| `STATIC_PLAYER_CHANNEL_ID` | Channel ID for static player | If static player enabled | N/A |

### Lavalink Configuration

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `LAVALINK_HOST` | Hostname for Lavalink server | No | `lavalink` |
| `LAVALINK_PORT` | Port for Lavalink server | No | `2333` |
| `LAVALINK_PASSWORD` | Password for Lavalink server | No | `youshallnotpass` |

## Example .env File

Below is a complete example of an `.env` file with all available configuration options:

```bash
# Core Discord Configuration
DISCORD_TOKEN=your_discord_token_here
DISCORD_APPLICATION_ID=your_application_id_here
PREFIX=!
LOG_LEVEL=INFO

# Plex Configuration
PLEX_SERVER=http://your-plex-server:32400
PLEX_TOKEN=your_plex_token_here
PLEX_LIBRARY_SECTION=Music

# Player UI Configuration
PLAYER_STYLE_VISUAL=true
USE_STATIC_PLAYER_CHANNEL=false
STATIC_PLAYER_CHANNEL_ID=

# Lavalink Configuration
LAVALINK_HOST=lavalink
LAVALINK_PORT=2333
LAVALINK_PASSWORD=youshallnotpass
```

## Advanced Configuration

### Docker Compose Configuration

The default `docker-compose.yml` file includes configuration for both the PlexBot and Lavalink services. You can modify this file to change container names, port mappings, or volume mounts.

Example modifications:

```yaml
version: '3'
services:
  plexbot:
    build:
      context: .
      dockerfile: ./Install/Docker/dockerfile
    restart: unless-stopped
    volumes:
      - ./data:/app/data
      - ./custom_fonts:/app/custom_fonts  # Add a volume for custom fonts
    depends_on:
      - lavalink
    env_file:
      - .env

  lavalink:
    image: fredboat/lavalink:latest
    restart: unless-stopped
    volumes:
      - ./Install/Lavalink/application.yml:/opt/Lavalink/application.yml
```

### Font Configuration

PlexBot needs system fonts to display text on player images. In Docker environments, these fonts are automatically installed in the container.

If you experience issues with missing fonts, ensure your Docker image has the following packages:
- fontconfig
- fonts-dejavu
- fonts-liberation
- fonts-noto

The default Dockerfile already includes these packages.

## Configuration File Locations

| File | Location | Purpose |
|------|----------|---------|
| `.env` | Root directory | Main configuration |
| `docker-compose.yml` | Root directory | Docker services configuration |
| `application.yml` | `Install/Lavalink/` | Lavalink server configuration |

## Permissions

The Discord bot requires the following permissions to function properly:

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

These permissions translate to the permission integer: `277062627904`

## After Changing Configuration

After modifying any configuration, you need to restart the bot:

**With Docker:**
```bash
docker-compose down
docker-compose up -d
```

**Without Docker:**
```bash
# Stop the running process with Ctrl+C and then
dotnet run
```

## Validating Configuration

To validate that your configuration is working:

1. Check the bot's logs:
   ```bash
   docker-compose logs -f plexbot
   ```

2. Use the `/ping` command in Discord to verify the bot is responsive

3. Use the `/play` command with a song title to test audio playback
