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

# Create Extensions directory
mkdir -p "$DOCKER_DIR/Extensions"

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
    - dependency: "dev.lavalink.youtube:youtube-plugin:1.13.5"
      snapshot: false
plugins:
  youtube:
    enabled: true
    allowSearch: true
    allowDirectVideoIds: true
    allowDirectPlaylistIds: true
    clients:
      - TVHTML5EMBEDDED
      - TV 
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
    echo "Please update the .env file with your Discord token and Plex server details."
    echo "You MUST rename to .env and update the file with your own credentials before continuing."
    echo ""
    exit 1
fi

# Make sure data and logs directories exist
mkdir -p "$ROOT_DIR/data" "$ROOT_DIR/logs"

echo ""
echo "Building and starting Docker containers..."
echo ""

# Navigate to Docker directory and run docker-compose
cd "$DOCKER_DIR"

# Stop and remove existing containers, networks, and volumes
docker-compose down --volumes --remove-orphans

# Remove any existing images
docker rmi -f plexbot:latest
docker rmi -f ghcr.io/lavalink-devs/lavalink:4

# Clear build cache
docker builder prune -f

# Build and start the containers
docker-compose -p plexbot up -d --build

echo ""
echo "PlexBot installation completed successfully!"
echo "The bot should now be running in the background."
echo ""
echo "You can check the logs with: docker-compose logs -f"
echo ""