# Docker Deployment Guide for PlexBot

This guide covers everything you need to know about deploying and managing PlexBot using Docker containers.

## Introduction to Docker

Docker allows PlexBot to run in an isolated container with all dependencies pre-configured, making deployment consistent across different environments.

## Prerequisites

- Docker Engine (v20.10.0+)
- Docker Compose (v2.0.0+)
- Git (for cloning the repository)
- Internet connection for pulling images

## Project Structure

The PlexBot Docker setup uses these key files:

- `Install/Docker/dockerfile` - Defines the PlexBot container image
- `docker-compose.yml` - Orchestrates PlexBot and Lavalink services
- `Install/Docker/startup.sh` - Container entrypoint script

## Quick Start

```bash
# Clone the repository
git clone https://github.com/kalebbroo/plex_music_bot.git
cd plex_music_bot

# Configure environment variables
cp RenameMe.env.txt .env
nano .env  # Edit with your Discord token and other settings

# Run the installation script
## For Windows:
cd Install
win-inatall.bat

## For Linux:
chmod +x ./Install/linux-install.sh
./Install/linux-install.sh
```

## Docker Compose Configuration

The default `docker-compose.yml` sets up two services:

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

### Key Components:

- **PlexBot Service**:
  - Built from the local Dockerfile
  - Persistent data through volume mount
  - Environment variables from `.env` file
  - Auto-restarts unless manually stopped

- **Lavalink Service**:
  - Uses official Fredboat Lavalink image
  - Custom configuration through mounted `application.yml`
  - Auto-restarts unless manually stopped

## Dockerfile Analysis

The PlexBot Dockerfile builds a .NET application with required dependencies:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build

# Install necessary dependencies
RUN apt-get update && apt-get install -y \
    curl \
    unzip \
    git \
    fontconfig \
    fonts-dejavu \
    fonts-liberation \
    fonts-noto \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . ./

# Build the application
RUN dotnet restore && \
    dotnet publish -c Release -o out

# Setup the runtime container
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/out .
COPY Install/Docker/startup.sh /app/startup.sh
RUN chmod +x /app/startup.sh

# Set the entrypoint
ENTRYPOINT ["/app/startup.sh"]
```

### Key Features:

- **Multi-stage build**: Smaller final image by separating build and runtime environments
- **Font packages**: Installed for proper text rendering on player images
- **Startup script**: Handles initialization and ensures code updates are applied

## Volume Mounts

Docker volumes preserve data between container restarts:

| Container Path | Host Path | Purpose |
|----------------|-----------|---------|
| `/app/data` | `./data` | Persistent bot data storage |
| `/opt/Lavalink/application.yml` | `./Install/Lavalink/application.yml` | Lavalink configuration |

## Managing Docker Containers

### Viewing Logs

```bash
# View logs from both services
docker-compose logs

# View only PlexBot logs with follow option
docker-compose logs -f plexbot

# View only Lavalink logs with follow option
docker-compose logs -f lavalink
```

### Container Management

```bash
# Stop containers
docker-compose down

# Start containers
docker-compose up -d

# Restart a specific service
docker-compose restart plexbot

# Rebuild and restart (after code changes)
docker-compose up -d --build
```

### Checking Container Status

```bash
# List running containers
docker-compose ps

# View resource usage
docker stats
```

## Updating PlexBot

To update PlexBot to the latest version:

```bash
# Pull the latest code
git pull

# Rebuild and restart containers
docker-compose down
docker-compose up -d --build
```

## Customizing the Docker Setup

### Using Custom Ports

Modify `docker-compose.yml` to change port mappings:

```yaml
services:
  lavalink:
    # Other settings...
    ports:
      - "8888:2333"  # Maps container port 2333 to host port 8888
```

### Adding Custom Volumes

For additional persistent storage:

```yaml
services:
  plexbot:
    # Other settings...
    volumes:
      - ./data:/app/data
      - ./custom_files:/app/custom_files
```

### Setting Resource Limits

To prevent resource exhaustion on your host:

```yaml
services:
  plexbot:
    # Other settings...
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 1G
```

## System Requirements

Minimum recommended specifications for running PlexBot in Docker:

- **CPU**: 2 cores
- **RAM**: 2GB
- **Storage**: 1GB free space
- **Network**: 5 Mbps upload/download

## Handling Container Updates

The container automatically checks for code updates on startup via the `startup.sh` script:

```bash
#!/bin/bash
cd /app

echo "Starting PlexBot..."
echo "Checking for updates..."

# Code update logic here

dotnet PlexBot.dll
```

## Security Considerations

- **Environment Variables**: Store sensitive tokens in `.env` file, not in `docker-compose.yml`
- **Network Exposure**: Avoid exposing Lavalink ports publicly
- **Volume Permissions**: Ensure proper file permissions on mounted volumes

## Troubleshooting Docker Issues

### Container Fails to Start

Check logs for error messages:
```bash
docker-compose logs plexbot
```

Common issues:
- Missing required environment variables
- Incorrect file permissions
- Port conflicts

### Container Starts but Bot is Offline

Verify network connectivity and Discord token:
```bash
docker exec -it plexbot_plexbot_1 ping discord.com
```

### High Resource Usage

Monitor and adjust resource limits:
```bash
docker stats
```

Consider increasing limits in `docker-compose.yml` if resources are exhausted.

## Advanced Docker Configurations

### Docker Networks

For enhanced security, create isolated networks:

```yaml
services:
  plexbot:
    # Other settings...
    networks:
      - bot_network
  
  lavalink:
    # Other settings...
    networks:
      - bot_network

networks:
  bot_network:
    driver: bridge
```

### Health Checks

Add health monitoring to automatically restart unhealthy containers:

```yaml
services:
  plexbot:
    # Other settings...
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
```

## Additional Resources

- [Docker Documentation](https://docs.docker.com/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [PlexBot Installation Guide](./Installation.md)
- [PlexBot Configuration Guide](./Configuration.md)
