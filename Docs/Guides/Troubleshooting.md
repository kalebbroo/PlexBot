# PlexBot Troubleshooting Guide

## Table of Contents
- [Connection Issues](#connection-issues)
- [Audio Problems](#audio-problems)
- [Visual Player Issues](#visual-player-issues)
- [Track Loading Failures](#track-loading-failures)
- [Command Problems](#command-problems)
- [Docker Issues](#docker-issues)
- [Lavalink Issues](#lavalink-issues)

## Connection Issues

### Bot Won't Connect to Discord

- Verify `DISCORD_TOKEN` in `.env` is correct
- Check [Discord Status](https://discordstatus.com/) for outages
- Ensure the bot has been invited to your server with the correct permissions
- Check the bot logs: `docker-compose logs plexbot`

### Bot Is Online but Not Responding

- Ensure the bot has **Use Application Commands** permission in your server
- Slash commands can take up to 1 hour to propagate globally — set `bot.environment: Development` in `config.fds` for instant guild-scoped updates
- Try restarting: `docker-compose restart plexbot`

## Audio Problems

### No Sound in Voice Channel

1. Check that Lavalink is running: `docker-compose logs lavalink`
2. Ensure the bot has **Connect** and **Speak** permissions in the voice channel
3. Verify Lavalink connection settings in `.env` match your setup:
   ```env
   LAVALINK_HOST=Lavalink
   LAVALINK_SERVER_PORT=2333
   LAVALINK_SERVER_PASSWORD=youshallnotpass
   ```
4. Make sure your Plex server is reachable from the machine running PlexBot

### Audio Stuttering / Skipping

Lavalink must send an audio frame to Discord every 20ms. Stuttering is caused by interruptions to this schedule.

**Quick fixes:**
1. Uncomment `_JAVA_OPTIONS` in `.env` to enable ZGC garbage collection:
   ```env
   _JAVA_OPTIONS=-XX:+UseZGC -XX:+ZGenerational -Xms256m -Xmx512m
   ```
2. Uncomment `cpuset` and `cpu_shares` in `docker-compose.yml` to pin CPU cores for Lavalink

See the [Performance Tuning](../../README.md#performance-tuning-audio-stuttering-fix) section in the README for full details.

## Visual Player Issues

### No Text on Player Images

The modern visual player renders text with ImageSharp, which requires fonts installed in the Docker container.

- Rebuild the container: `docker-compose up -d --build`
- The Dockerfile installs DejaVu, Liberation, Noto (including CJK and emoji) fonts automatically
- Check for font errors: `docker-compose logs plexbot | grep -i font`

### Player Image Not Showing

- Ensure `visualPlayer.useModernPlayer` is `true` in `config.fds`
- Make sure the bot has **Attach Files** permission in the channel
- Check logs for ImageSharp errors

### Progress Bar Missing or Broken

- Verify `visualPlayer.progressBar.enabled` is `true` in `config.fds`
- If using custom emoji, all 30 IDs must be present — missing IDs trigger a fallback to unicode
- Check the bot startup log for "Progress bar: Using custom Discord emoji" or "Using unicode fallback"

### Progress Bar Wraps on Mobile

Set `visualPlayer.progressBar.size` to `small` in `config.fds` for a narrower bar that fits mobile screens.

## Track Loading Failures

### "Added X of Y tracks" — Some Tracks Failed

When loading large playlists, Plex can drop connections under concurrent load.

**Fixes:**
1. Lower `plex.maxConcurrentResolves` in `config.fds` (default: `3`, try `2` or `1`)
2. Failed tracks are retried automatically — check logs for "Retry succeeded" vs "Failed to resolve after retry"
3. The player embed shows which specific tracks failed to load

### All Tracks Fail

- Verify Plex is reachable: `curl -H "X-Plex-Token: YOUR_TOKEN" http://your-plex-ip:32400`
- Check that your `PLEX_URL` and `PLEX_TOKEN` in `.env` are correct
- Look at Lavalink logs for connection errors: check `logs/lavalink/` or `docker-compose logs lavalink`

## Command Problems

### Slash Commands Not Appearing

- Commands may take up to 1 hour to register globally with Discord
- For instant updates during development, set `bot.environment: Development` in `config.fds` (guild-scoped commands update immediately)
- Ensure the bot has **Use Application Commands** permission

### Buttons Not Responding

- There is a 2-second cooldown on all button interactions — wait and try again
- Check that the bot is still running: `docker-compose logs plexbot`
- If the player message is old (from a previous bot session), start a new one with `/play` or `/search`

## Docker Issues

### Container Crashes on Startup

1. Check logs: `docker-compose logs plexbot`
2. Verify all required environment variables are set in `.env` (`DISCORD_TOKEN`, `PLEX_URL`, `PLEX_TOKEN`)
3. Make sure `config.fds` exists at the project root (copy from `RenameMe.config.fds`)
4. Try a clean rebuild:
   ```bash
   docker-compose down
   docker-compose up -d --build
   ```

### Config Changes Not Taking Effect

Both `.env` and `config.fds` are read at startup. After changes, restart the bot:
```bash
docker-compose restart plexbot
```

### Lavalink Logs Lost After Container Rebuild

Lavalink logs are persisted to `logs/lavalink/` on the host via volume mount. They survive container rebuilds.

## Lavalink Issues

### "Unable to connect to Lavalink node"

1. Check that the Lavalink container is running: `docker-compose ps`
2. Verify the settings in `.env` match:
   ```env
   LAVALINK_HOST=Lavalink          # Docker service name
   LAVALINK_SERVER_PORT=2333
   LAVALINK_SERVER_PASSWORD=youshallnotpass
   ```
3. Both containers must be on the same Docker network (`plexbot-network`)
4. Restart Lavalink: `docker-compose restart lavalink`

### Remote Lavalink Connection Issues

If using a remote Lavalink server:
- Set `LAVALINK_HOST` to the IP/hostname of the remote server
- Set `LAVALINK_SECURE=true` if behind an HTTPS reverse proxy
- Ensure the port is open and reachable from the PlexBot machine

## Still Having Issues?

1. Collect logs: `docker-compose logs > plexbot-debug.txt`
2. Check PlexBot logs in `logs/` and Lavalink logs in `logs/lavalink/`
3. Open an issue on [GitHub](https://github.com/kalebbroo/PlexBot/issues) with the logs and a description of the problem
4. Join the [Discord support server](https://discord.com/invite/5m4Wyu52Ek)
