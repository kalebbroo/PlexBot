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

# Generate Lavalink config from base template + extension plugin fragments.
# Uses the same mikefarah/yq Docker image that docker-compose uses for the init container.
# This always regenerates so adding/removing extensions is picked up on re-install.
echo "Generating Lavalink configuration from base template + extension fragments..."
docker run --rm \
    -v "$ROOT_DIR/Extensions:/extensions:ro" \
    -v "$DOCKER_DIR/lavalink.base.yml:/config/base.yml:ro" \
    -v "$DOCKER_DIR/generate-lavalink-config.sh:/config/generate.sh:ro" \
    -v "$DOCKER_DIR:/output" \
    mikefarah/yq:latest sh /config/generate.sh /extensions /output /config/base.yml

# Check if .env file exists
if [ ! -f "$ROOT_DIR/.env" ]; then
    echo ""
    echo "No .env file found at: $ROOT_DIR/.env"
    echo "Please create one from the template:"
    echo "  cp RenameMe.env.txt .env"
    echo "Then fill in your Discord token and Plex server details."
    echo ""
    exit 1
fi

# Create config.fds from template if it doesn't exist
if [ ! -f "$ROOT_DIR/config.fds" ]; then
    if [ -f "$ROOT_DIR/RenameMe.config.fds" ]; then
        echo "No config.fds found — creating from template with defaults..."
        cp "$ROOT_DIR/RenameMe.config.fds" "$ROOT_DIR/config.fds"
        echo "Edit config.fds to customize player settings."
    else
        echo "Warning: No config.fds or RenameMe.config.fds found. Bot will use built-in defaults."
    fi
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
