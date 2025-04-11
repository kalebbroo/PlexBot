#!/bin/bash
set -e

echo "==================================="
echo "PlexBot Installation Script"
echo "==================================="
echo ""

# Get script directory path
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
DOCKER_DIR="$SCRIPT_DIR/Docker"

# Check for Docker
if ! command -v docker &> /dev/null; then
    echo "Docker is not installed. Please install Docker first."
    echo "Visit: https://docs.docker.com/engine/install/"
    exit 1
fi

# Check for Docker Compose
if ! command -v docker-compose &> /dev/null; then
    echo "Docker Compose is not installed. Please install it first."
    echo "Visit: https://docs.docker.com/compose/install/"
    exit 1
fi

# Create plugins directory
mkdir -p "$DOCKER_DIR/plugins"

# Create Lavalink application.yml if it doesn't exist
if [ ! -f "$DOCKER_DIR/lavalink.application.yml" ]; then
    echo "Creating Lavalink configuration..."
    cat > "$DOCKER_DIR/lavalink.application.yml" << 'EOF'
server:
# Port and address come from environment variables
lavalink:
  server:
    # Password comes from environment variables
    sources:
      youtube: false  # Disable built-in YouTube source as we're using the plugin
      bandcamp: true
      soundcloud: true
      twitch: true
      vimeo: true
      http: true
      local: false
      nico: true
    bufferDurationMs: 400
    frameBufferDurationMs: 5000
    youtubePlaylistLoadLimit: 10
    playerUpdateInterval: 3
    trackStuckThresholdMs: 10000
    youtubeSearchEnabled: true
    soundcloudSearchEnabled: true
    gc-warnings: true
  plugins:
    - dependency: "dev.lavalink.youtube:youtube-plugin:LATEST"
      snapshot: false
plugins:
  youtube:
    enabled: true
    allowSearch: true
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - MUSIC
      - ANDROID_VR
      - WEB
      - WEBEMBEDDED
logging:
  file:
    max-history: 30
    max-size: 1GB
  level:
    # Root and Lavalink log levels come from environment variables
EOF
fi

# Check if .env file exists, if not create template
if [ ! -f "$ROOT_DIR/.env" ]; then
    echo "Creating template .env file..."
    cat > "$ROOT_DIR/.env" << 'EOF'
######################################################
## PlexBot Configuration  and Lavalink Server setup ##
######################################################

# Edit these to setup your bot. Never leak or share this file. #
# Keep it secret. Keep it safe. #

########################
## Discord Bot Config ##
########################

# You can get your bot token from https://discord.com/developers/applications
DISCORD_TOKEN=YOUR_BOT_TOKEN_HERE
BOT_PREFIX=! # The prefix for text commands
LISTENING_TO_MUSIC_MESSAGE=true # Setting to false will fallback to the default message "Listening to Music"
STATUS=online

######################
## Optional Plugins ##
######################

# Install plugins by joining the PlexBot Discord server. #
ENABLE_YOUTUBE=false
ENABLE_SOUNDCLOUD=false
ENABLE_TWITCH=false
ENABLE_VIMEO=false
ENABLE_BANDCAMP=false

#########################
## Plex server details ##
#########################

# The URL is your public IP address and port of your Plex server. Learn how to get the token 
# here: https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/
PLEX_URL=http://PUBLIC_URL:YOUR_PORT
PLEX_TOKEN=YOUR_PLEX_TOKEN_HERE
PLEX_CLIENT_IDENTIFIER=
PLEX_APP_NAME=Your Cool Plex App
ALLOW_CUSTOM_PLAYLISTS=true
NUMBER_OF_CUSTOM_PLAYLISTS=5
PLAYLIST_IDENTIFIER=userId

## Lavalink server details ##
# These are the primary settings users will need to change
LAVALINK_HOST=lavalink
SERVER_ADDRESS=0.0.0.0
SERVER_PORT=2333
LAVALINK_SERVER_PASSWORD=youshallnotpass
_JAVA_OPTIONS=-Xmx8G

# The following settings are now managed in the application.yml file
# and don't need to be changed by most users
#
# LAVALINK_SERVER_SOURCES_YOUTUBE=false
# LAVALINK_SERVER_SOURCES_BANDCAMP=true
# LAVALINK_SERVER_SOURCES_SOUNDCLOUD=true
# LAVALINK_SERVER_SOURCES_TWITCH=true
# LAVALINK_SERVER_SOURCES_VIMEO=true
# LAVALINK_SERVER_SOURCES_HTTP=true
# LAVALINK_SERVER_SOURCES_LOCAL=false
# LAVALINK_SERVER_SOURCES_NICO=true
#
# LAVALINK_SERVER_YOUTUBE_SEARCH_ENABLED=true
# LAVALINK_SERVER_SOUNDCLOUD_SEARCH_ENABLED=true
# YOUTUBE_PLAYLIST_LOAD_LIMIT=10
#
# LAVALINK_SERVER_PLAYER_UPDATE_INTERVAL=3
# LAVALINK_SERVER_TRACK_STUCK_THRESHOLD_MS=10000
# OPUS_ENCODING_QUALITY=10
# RESAMPLING_QUALITY=HIGH
# BUFFER_DURATION_MS=400
# FRAME_BUFFER_DURATION_MS=5000
# USE_SEEK_GHOSTING=true
# GC_WARNINGS=true

## Logging settings ##
LOGGING_LEVEL_ROOT=INFO  # Set global logging level (DEBUG, INFO, WARN, ERROR)
LOGGING_LEVEL_LAVALINK=INFO
EOF

    echo ""
    echo "Please update the .env file with your Discord token and Plex server details."
    echo "Then run this script again."
    if command -v nano &> /dev/null; then
        nano "$ROOT_DIR/.env"
    elif command -v vim &> /dev/null; then
        vim "$ROOT_DIR/.env"
    else
        echo "Please edit the .env file manually with a text editor."
    fi
    exit 1
fi

# Create Dockerfile if it doesn't exist
if [ ! -f "$DOCKER_DIR/dockerfile" ]; then
    echo "Creating Docker configuration files..."
    cat > "$DOCKER_DIR/dockerfile" << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /source

# Copy the project files
COPY . .

# Restore dependencies and build the project
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

# Install additional dependencies
RUN apt-get update && apt-get install -y \
    curl \
    unzip \
    git \
    && rm -rf /var/lib/apt/lists/*

# Copy the build output from the build stage
COPY --from=build /app/publish .

# Set up the startup script
COPY Install/Docker/startup.sh /app/startup.sh
RUN chmod +x /app/startup.sh

# Mount point for the .env file (will be copied at runtime)
VOLUME /app/config

ENTRYPOINT ["/app/startup.sh"]
EOF
fi

# Create startup script if it doesn't exist
if [ ! -f "$DOCKER_DIR/startup.sh" ]; then
    echo "Creating startup script..."
    cat > "$DOCKER_DIR/startup.sh" << 'EOF'
#!/bin/bash
set -e

# Path to source code (mounted volume)
SOURCE_DIR="/source"
# Make sure .env is accessible
if [ -f "/source/.env" ]; then
    cp /source/.env /app/.env
    echo "Copied .env from source directory"
elif [ -f "/app/config/.env" ]; then
    cp /app/config/.env /app/.env
    echo "Copied .env from config volume"
else
    echo "Warning: No .env file found"
fi

# Function to check if rebuild is needed
need_rebuild() {
  if [ ! -d "/app/bin" ]; then
    echo "Binary directory doesn't exist, rebuild needed"
    return 0
  fi

  if [ -z "$(ls -A /app/bin 2>/dev/null)" ]; then
    echo "Binary directory is empty, rebuild needed"
    return 0
  fi

  # Check if source code is newer than binaries
  NEWEST_SOURCE=$(find $SOURCE_DIR -type f -name "*.cs" -o -name "*.csproj" | xargs stat --format '%Y' 2>/dev/null | sort -nr | head -n 1)
  NEWEST_BINARY=$(find /app/bin -type f -name "*.dll" | xargs stat --format '%Y' 2>/dev/null | sort -nr | head -n 1)

  if [[ -z "$NEWEST_BINARY" || "$NEWEST_SOURCE" -gt "$NEWEST_BINARY" ]]; then
    echo "Source code is newer than binaries, rebuild needed"
    return 0
  fi

  return 1
}

# Pull latest code from GitHub if repo exists
pull_latest_code() {
  # TODO: Uncomment this section once the repository is created
  # if [ -d "$SOURCE_DIR/.git" ]; then
  #   echo "Pulling latest code from GitHub..."
  #   cd $SOURCE_DIR
  #   git pull
  #   cd -
  # fi
  echo "TODO: GitHub pull functionality will be implemented once repository is created"
}

# Main execution
echo "Starting PlexBot..."

# Check if we need to rebuild the project
if need_rebuild; then
  echo "Rebuilding project from source..."
  cd $SOURCE_DIR
  dotnet restore
  dotnet build
  cd /app
  echo "Rebuild complete"
fi

# Start the application
echo "Starting application..."
dotnet PlexBot.dll
EOF
    chmod +x "$DOCKER_DIR/startup.sh"
fi

# Create docker-compose.yml if it doesn't exist
if [ ! -f "$DOCKER_DIR/docker-compose.yml" ]; then
    echo "Creating Docker Compose configuration..."
    cat > "$DOCKER_DIR/docker-compose.yml" << 'EOF'
version: '3.9'

services:
  # PlexBot Service
  plexbot:
    container_name: PlexBot
    build:
      context: ../..  # This points to the project root from Docker folder
      dockerfile: Install/Docker/dockerfile
    restart: unless-stopped
    depends_on:
      - lavalink
    volumes:
      - ../..:/source  # Mount project root as /source
      - ../../data:/app/data
      - ../../logs:/app/logs
      - ../../.env:/app/config/.env
    environment:
      - DOTNET_ENVIRONMENT=Production
    env_file:
      - ../../.env
    networks:
      - plexbot-network

  # Lavalink Service
  lavalink:
    container_name: LavaLink
    image: ghcr.io/lavalink-devs/lavalink:4
    restart: unless-stopped
    env_file:
      - ../../.env
    environment:
      # These can override what's in the .env file if needed
      - SERVER_PORT=${SERVER_PORT:-2333}
      - SERVER_ADDRESS=${SERVER_ADDRESS:-0.0.0.0}
      - LAVALINK_SERVER_PASSWORD=${LAVALINK_SERVER_PASSWORD:-youshallnotpass}
    volumes:
      - ./lavalink.application.yml:/opt/Lavalink/application.yml
      - ./plugins:/opt/Lavalink/plugins
    ports:
      - "${SERVER_PORT:-2333}:${SERVER_PORT:-2333}"
    networks:
      - plexbot-network

networks:
  plexbot-network:
    driver: bridge
EOF
fi

# Make sure data and logs directories exist
mkdir -p "$ROOT_DIR/data" "$ROOT_DIR/logs"

echo ""
echo "Building and starting Docker containers..."
echo ""

# Navigate to Docker directory and run docker-compose
cd "$DOCKER_DIR"
docker-compose up -d --build

echo ""
echo "PlexBot installation completed successfully!"
echo "The bot should now be running in the background."
echo ""
echo "You can check the logs with: docker-compose logs -f"
echo ""