# PlexBot Installation Guide

## Prerequisites

- **Docker & Docker Compose** — [Docker Desktop](https://www.docker.com/products/docker-desktop/) is recommended for an easier UI
- **Discord bot token** — Create one at the [Discord Developer Portal](https://discord.com/developers/applications)
- **Plex server** with a music library and an authentication token — [How to get a Plex token](https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/)
- A machine to host the bot (can be the same machine as Plex)

## Installation

### Step 1: Clone the Repository

```bash
git clone https://github.com/kalebbroo/PlexBot.git
cd PlexBot
```

### Step 2: Configure Secrets

Copy the template and fill in your credentials:

```bash
cp RenameMe.env.txt .env
```

Edit `.env` with your values:

```env
DISCORD_TOKEN=your_discord_bot_token
PLEX_URL=http://your-plex-ip:32400
PLEX_TOKEN=your_plex_token
```

These three are required. All other `.env` values have working defaults. See the [Configuration Guide](./Configuration.md) for the full list.

### Step 3: Configure Application Settings

Copy the template:

```bash
cp RenameMe.config.fds config.fds
```

The defaults work out of the box. Customize player style, progress bar, logging, etc. in `config.fds` as needed. See the [Configuration Guide](./Configuration.md) for all options.

### Step 4: Run the Install Script

**Windows:**
```bash
cd Install
win-install.bat
```

**Linux:**
```bash
chmod +x ./Install/linux-install.sh
./Install/linux-install.sh
```

The script will:
1. Build the PlexBot Docker image (includes .NET 9 SDK, fonts, dependencies)
2. Pull the Lavalink 4 image
3. Start both containers on a shared Docker network

### Step 5: Verify Installation

Check the logs to make sure the bot started successfully:

```bash
docker-compose logs -f
```

Or open **Docker Desktop** and click the PlexBot container group to see logs from both services.

You should see:
- "Lavalink services initialized"
- "Bot is ready"
- The bot appearing online in your Discord server

## Updating

Run the install script again — it pulls the latest code from GitHub and rebuilds the Docker image:

```bash
# Windows
cd Install && win-install.bat

# Linux
./Install/linux-install.sh
```

## Troubleshooting

### Bot Doesn't Start
- Check the logs: `docker-compose logs plexbot`
- Verify `DISCORD_TOKEN` is correct in `.env`
- Make sure both `.env` and `config.fds` exist at the project root

### No Audio
- Check Lavalink is running: `docker-compose logs lavalink`
- Ensure the bot has **Connect** and **Speak** permissions in the voice channel
- Verify Plex is reachable from the Docker host

### No Text on Player Images
- Rebuild the container to ensure fonts are installed: `docker-compose up -d --build`

For more detailed troubleshooting, see the [Troubleshooting Guide](../Guides/Troubleshooting.md).

## Additional Resources

- [Configuration Guide](./Configuration.md) — All `.env` and `config.fds` options
- [Docker Guide](./Docker-Guide.md) — Container management and customization
- [Player UI Guide](../Guides/Player-UI-Guide.md) — Player styles and progress bar setup
