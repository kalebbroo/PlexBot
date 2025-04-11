# PlexBot Installation Guide

This guide provides detailed instructions for installing and configuring PlexBot, a Discord music bot that integrates with Plex Media Server.

## Prerequisites

- Docker and Docker Compose, We also recommend Docker Desktop for an easier UI and just a better experience all around.
- A Discord Bot Token (see [Discord Developer Portal](https://discord.com/developers/applications))
- Plex Media Server and a token (see [How to get a Plex token](http://))
- A server or computer to host the bot
- (Optional) YouTube Oauth refresh token (see [Here]()) 

## Installation

#### Step 1: Clone the Repository

```bash
git clone https://github.com/kalebbroo/PlexBot.git
cd PlexBot
```

#### Step 2: Configure Environment Variables

1. Copy the example environment file:

```bash
cp RenameMe.env.txt .env
```

2. Edit the `.env` file with your credentials:

```bash
# Required Discord Configuration
DISCORD_TOKEN=your_discord_token_here
DISCORD_APPLICATION_ID=your_application_id_here

# Required Plex Configuration
PLEX_SERVER=http://your-plex-server:32400
PLEX_TOKEN=your_plex_token_here

# Visual Player Configuration (How the player looks in Discord)
PLAYER_STYLE_VISUAL=true
USE_STATIC_PLAYER_CHANNEL=false
STATIC_PLAYER_CHANNEL_ID=
```

#### Step 3: Install PlexBot

For Windows users:
```bash
# Navigate to the Install directory
cd Install

# Simply run the Windows installation script
win-inatall.bat
```

For Linux users:
```bash
# Make the installation script executable if needed
chmod +x ./Install/linux-install.sh

# Run the Linux installation script
./Install/linux-install.sh
```

This script will:
1. Build the PlexBot Docker image with all required dependencies including lavalink
2. Run a setup script that creates the config yml and starts the bot and Lavalink service
3. Downloads and installs the YouTube plugin (Oauth refresh token needed)
4. Checks for issues or missing cridentials

#### Step 4: Verify Installation

After the installation script completes, check the logs to ensure the bot started correctly:

In Docker Desktop just click the container named PlexBot and it will display both logs for lavalink and the bot. 

or

```bash
docker-compose logs -f
```

You should see output indicating the bot has connected to Discord and is ready to use.

#### Step 5: Run the Bot

The bot should now be running but if you stop it and need to restart just cliock the start button next to the container in Docker Desktop.

or

```bash
dotnet run
```

## Updating PlexBot

### Docker Update

Updating is done by running the install script again This will pull the update from GitHub and rebuild the project and Docker container. 

## Troubleshooting

### Common Issues

#### Bot Doesn't Start

**Check the logs:**
```bash
docker-compose logs -f
```

**Common solutions:**
- Ensure your DISCORD_TOKEN is correct
- Verify network connectivity
- Check Discord bot permissions

#### No Audio Output

**Common solutions:**
- Ensure Lavalink is running and check the logs
- Check that the bot has the necessary permissions in your Discord server
- Verify the bot is in a voice channel

#### Font Issues in Docker

If the player image shows but no text appears:

```bash
# Rebuild the container to ensure font packages are installed
docker-compose up -d --build
```

## Additional Configuration

For more advanced configuration options and features, refer to the [Configuration Guide](./Configuration.md).

## Need Help?

If you encounter issues not covered in this guide:
- Check the [Troubleshooting Guide](../Guides/Troubleshooting.md)
- Open an issue on our [GitHub repository](https://github.com/kalebbroo/plex_music_bot)
- Join our [Discord support server](https://discord.gg/plexbot)
