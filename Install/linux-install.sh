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

# Detect compose command (plugin "docker compose" preferred, fallback to standalone)
if docker compose version &> /dev/null; then
    COMPOSE="docker compose"
elif command -v docker-compose &> /dev/null; then
    COMPOSE="docker-compose"
else
    echo "Docker Compose is not installed."
    echo "Install via: https://docs.docker.com/compose/install/"
    exit 1
fi

echo "Using compose command: $COMPOSE"

# Create plugins directory for Lavalink
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
    - dependency: "dev.lavalink.youtube:youtube-plugin:1.18.0"
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
    oauth:
      enabled: true
      refreshToken: ""
logging:
  file:
    max-history: 30
    max-size: 1GB
  level:
    root: INFO
    lavalink: INFO
EOF
fi

# Check if .env file exists
if [ ! -f "$ROOT_DIR/.env" ]; then
    echo ""
    echo "No .env file found at: $ROOT_DIR/.env"
    echo "Please create a .env file with your Discord token and Plex server details."
    echo ""
    exit 1
fi

# Make sure data and logs directories exist
mkdir -p "$ROOT_DIR/data" "$ROOT_DIR/logs"

echo ""
echo "Building and starting Docker containers..."
echo ""

cd "$DOCKER_DIR"

# Stop existing containers gracefully (preserve volumes/data)
$COMPOSE -p plexbot down --remove-orphans 2>/dev/null || true

# Build and start containers
$COMPOSE -p plexbot up -d --build

echo ""
echo "PlexBot installation completed successfully!"
echo "The bot should now be running in the background."
echo ""
echo "Useful commands:"
echo "  View logs:       cd \"$DOCKER_DIR\" && $COMPOSE -p plexbot logs -f"
echo "  Stop bot:        cd \"$DOCKER_DIR\" && $COMPOSE -p plexbot down"
echo "  Restart bot:     cd \"$DOCKER_DIR\" && $COMPOSE -p plexbot restart"
echo "  Rebuild & start: cd \"$DOCKER_DIR\" && $COMPOSE -p plexbot up -d --build"
echo ""
