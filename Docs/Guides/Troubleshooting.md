# PlexBot Troubleshooting Guide

This guide provides solutions to common issues you might encounter when running PlexBot.

## Table of Contents
- [Connection Issues](#connection-issues)
- [Audio Problems](#audio-problems)
- [Visual Player Issues](#visual-player-issues)
- [Command Problems](#command-problems)
- [Docker-Specific Issues](#docker-specific-issues)
- [Lavalink Issues](#lavalink-issues)

## Connection Issues

### Bot Won't Connect to Discord

**Symptoms:**
- Bot shows offline in Discord
- Logs show connection errors

**Solutions:**

1. **Check Discord Token:**
   ```bash
   # Verify your token is correct in .env
   DISCORD_TOKEN=your_discord_token_here
   ```

2. **Verify Network Connectivity:**
   ```bash
   # Test connection to Discord
   ping discord.com
   ```

3. **Check Discord API Status:**
   Visit [Discord Status](https://discordstatus.com/) to see if there are any ongoing API issues.

4. **Review Bot Permissions:**
   Ensure your bot has the correct OAuth2 scopes and permissions.

## Audio Problems

### No Sound in Voice Channel

**Symptoms:**
- Bot joins voice channel but doesn't play audio
- Commands seem to work but no music plays

**Solutions:**

1. **Check Lavalink Connection:**
   ```bash
   # In Docker:
   docker-compose logs lavalink
   
   # Look for successful connection messages
   ```

2. **Verify Voice Channel Permissions:**
   - Ensure bot has "Connect" and "Speak" permissions in the voice channel

3. **Test with Different Audio Sources:**
   - Try YouTube links
   - Try Spotify links
   - Try direct file playback

4. **Check Volume Settings:**
   ```
   /volume 100
   ```

### Choppy or Stuttering Audio

**Symptoms:**
- Audio plays but frequently stutters or cuts out

**Solutions:**

1. **Check Server Resources:**
   ```bash
   # Check CPU and memory usage
   top
   ```

2. **Optimize Lavalink:**
   Adjust Lavalink buffer settings in `application.yml`

3. **Network Bandwidth:**
   Ensure your server has sufficient upload bandwidth

## Visual Player Issues

### No Text on Player Images

**Symptoms:**
- Player image shows but has no text overlay
- Background image appears without track information

**Solutions:**

1. **Verify Font Packages in Docker:**
   ```bash
   # Check if fonts are installed
   docker exec -it plexbot_plexbot_1 fc-list
   
   # If empty or error, rebuild with fonts
   docker-compose up -d --build
   ```

2. **Check Logs for Font Errors:**
   ```bash
   docker-compose logs | grep -i font
   ```

3. **Manual Font Installation:**
   ```bash
   # If using Docker, modify Dockerfile to include:
   RUN apt-get update && apt-get install -y \
       fontconfig \
       fonts-dejavu \
       fonts-liberation \
       fonts-noto
   ```

### Player Image Not Showing

**Symptoms:**
- No album art or player image appears
- Only text information is displayed

**Solutions:**

1. **Check Network Access:**
   Ensure the bot can access external URLs for album art

2. **Enable Visual Player Mode:**
   ```bash
   # In .env file
   PLAYER_STYLE_VISUAL=true
   ```

3. **Check ImageSharp Errors:**
   Look for image processing errors in logs

## Command Problems

### Commands Not Responding

**Symptoms:**
- Bot is online but doesn't respond to commands
- No error messages appear

**Solutions:**

1. **Verify Command Registration:**
   Check if slash commands are registered with Discord

2. **Check Bot Permissions:**
   Ensure "Use Application Commands" permission is granted

3. **Server-Specific Issues:**
   Try the bot in a different server or channel

4. **Restart the Bot:**
   ```bash
   docker-compose restart plexbot
   ```

### Command Syntax Errors

**Symptoms:**
- Commands return syntax errors
- "Unknown command" messages

**Solutions:**

1. **Review Command Documentation:**
   Use `/help` to see correct command syntax

2. **Check for Updates:**
   Your bot version might be outdated

## Docker-Specific Issues

### Container Crashes on Startup

**Symptoms:**
- Docker container exits shortly after starting
- Logs show initialization errors

**Solutions:**

1. **Check Environment Variables:**
   Ensure all required variables are set in `.env`

2. **Increase Container Resources:**
   Allocate more CPU/memory to the container

3. **File Permissions:**
   Check permissions on mounted volumes

4. **Clean Rebuild:**
   ```bash
   docker-compose down
   docker system prune -a
   docker-compose up -d --build
   ```

### Volume Mounting Issues

**Symptoms:**
- Data doesn't persist between restarts
- Configuration changes don't take effect

**Solutions:**

1. **Check Volume Paths:**
   Ensure paths in `docker-compose.yml` are correct

2. **Verify File Ownership:**
   Files should be owned by the user inside the container

## Lavalink Issues

### Lavalink Connection Failures

**Symptoms:**
- Errors about "Unable to connect to Lavalink node"
- Audio commands fail with connection errors

**Solutions:**

1. **Check Lavalink Availability:**
   ```bash
   # Test if Lavalink is accessible
   curl -I http://lavalink:2333
   ```

2. **Verify Lavalink Configuration:**
   ```bash
   # In .env
   LAVALINK_HOST=lavalink
   LAVALINK_PORT=2333
   LAVALINK_PASSWORD=youshallnotpass
   ```

3. **Lavalink Log Analysis:**
   ```bash
   docker-compose logs lavalink
   ```

4. **Restart Lavalink:**
   ```bash
   docker-compose restart lavalink
   ```

## Still Having Issues?

If you've tried the solutions above and are still experiencing problems:

1. **Check Full Logs:**
   ```bash
   docker-compose logs > plexbot-logs.txt
   ```

2. **Open an Issue:**
   Submit the logs and a detailed description of your issue on our [GitHub repository](https://github.com/kalebbroo/plex_music_bot/issues)

3. **Discord Support:**
   Join our support server for real-time assistance
