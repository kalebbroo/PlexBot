# Docker Deployment Guide

## Overview

PlexBot runs as two Docker containers — the bot itself (.NET 9) and Lavalink (Java audio server). The install scripts handle everything, but this guide covers the Docker setup in detail for customization and troubleshooting.

## Prerequisites

- Docker Engine 20.10+
- Docker Compose v2+
- Git

## Project Structure

```
PlexBot/
├── Install/Docker/
│   ├── dockerfile              # Multi-stage .NET 9 build
│   ├── docker-compose.yml      # Orchestrates PlexBot + Lavalink
│   ├── lavalink.application.yml  # Lavalink server config
│   ├── startup.sh              # Container entrypoint
│   └── plugins/                # Lavalink plugins (YouTube, etc.)
├── .env                        # Secrets (tokens, passwords)
├── config.fds                  # Application settings
└── logs/                       # Persisted log files
    └── lavalink/               # Lavalink logs
```

## Docker Compose Services

### PlexBot Service

- **Image**: Built from `Install/Docker/dockerfile`
- **Container name**: `PlexBot`
- **Depends on**: Lavalink (starts after Lavalink is ready)
- **Volumes**:
  - `../../` → `/source` (project root for live source access)
  - `../../data` → `/app/data` (persistent data)
  - `../../logs` → `/app/logs` (bot logs)
  - `../../.env` → `/app/.env` (secrets)
  - `../../config.fds` → `/app/config.fds` (settings)
  - `../../Images` → `/app/Images` (player assets)
- **Network**: `plexbot-network` (bridge)

### Lavalink Service

- **Image**: `ghcr.io/lavalink-devs/lavalink:4`
- **Container name**: `LavaLink`
- **Ports**: `2333:2333` (configurable via `LAVALINK_SERVER_PORT` in `.env`)
- **Volumes**:
  - `lavalink.application.yml` → Lavalink config
  - `plugins/` → Lavalink plugins
  - `../../logs/lavalink` → Lavalink log files (persisted)
- **Network**: `plexbot-network` (bridge)

## Dockerfile

The build uses a multi-stage .NET 9 SDK image:

**Build stage**: Restores and publishes the .NET project
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app
```

**Runtime stage**: Uses the SDK image (not just runtime) to support live source rebuilds. Installs font packages for ImageSharp text rendering:
- `fonts-dejavu`, `fonts-liberation` — Latin text
- `fonts-noto`, `fonts-noto-cjk`, `fonts-noto-color-emoji` — CJK characters and emoji
- `fonts-ipafont-gothic`, `fonts-ipafont-mincho` — Japanese text

## Container Management

### Viewing Logs

```bash
# Both services
docker-compose logs

# Follow PlexBot logs
docker-compose logs -f plexbot

# Follow Lavalink logs
docker-compose logs -f lavalink
```

Bot logs are also saved to `logs/` and Lavalink logs to `logs/lavalink/` on the host.

### Start / Stop / Restart

```bash
docker-compose up -d          # Start
docker-compose down            # Stop
docker-compose restart plexbot # Restart just the bot
docker-compose up -d --build   # Rebuild and restart
```

### Check Status

```bash
docker-compose ps    # List containers
docker stats         # Resource usage
```

## Updating

Pull the latest code and rebuild:

```bash
git pull
docker-compose down
docker-compose up -d --build
```

Or just run the install script again — it handles this automatically.

## Performance Tuning

Two optional settings in `docker-compose.yml` help with audio stuttering:

### JVM Garbage Collection

Uncomment `_JAVA_OPTIONS` in `.env` to switch Lavalink's JVM to ZGC:
```env
_JAVA_OPTIONS=-XX:+UseZGC -XX:+ZGenerational -Xms256m -Xmx512m
```

### CPU Pinning

Uncomment `cpuset` and `cpu_shares` in `docker-compose.yml` to reserve CPU cores for Lavalink:
```yaml
cpuset: "0,1"
cpu_shares: 2048
```

See the [README Performance Tuning](../../README.md#performance-tuning-audio-stuttering-fix) section for details.

## Remote Lavalink

To use a Lavalink server running on a different machine, update `.env`:

```env
LAVALINK_HOST=192.168.1.100
LAVALINK_SERVER_PORT=2333
LAVALINK_SERVER_PASSWORD=mypassword
LAVALINK_SECURE=false
```

Then remove or comment out the `lavalink` service and `depends_on` block in `docker-compose.yml`.

## System Requirements

| Resource | Minimum |
|----------|---------|
| CPU | 2 cores |
| RAM | 2 GB |
| Disk | 1 GB free |
| Network | Stable connection to Discord and Plex |

## Security

- Store all tokens and passwords in `.env` — never in `docker-compose.yml` or code
- Don't expose the Lavalink port publicly unless you need remote access
- `.env` is `.gitignore`d — never commit it

## Troubleshooting

See the [Troubleshooting Guide](../Guides/Troubleshooting.md) for common issues.

## Additional Resources

- [Installation Guide](./Installation.md)
- [Configuration Guide](./Configuration.md)
- [Docker Documentation](https://docs.docker.com/)
