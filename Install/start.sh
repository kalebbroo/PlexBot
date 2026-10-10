#!/bin/bash
# Starts the PlexBot stack (bot + Lavalink) with Docker Compose.
# Does NOT start Plex Requests — that runs separately (configured via the bridge baseUrl in config.fds).
#
# Usage:
#   Install/start.sh            start (no rebuild) and wait for Lavalink
#   Install/start.sh --build    rebuild the bot image, then start
#   Install/start.sh --stop     stop the PlexBot stack
#   Install/start.sh --logs     follow the bot logs

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
DOCKER_DIR="$SCRIPT_DIR/Docker"
PROJECT="plexbot"

# Detect compose command (same logic as linux-install.sh)
if docker compose version &> /dev/null; then
    COMPOSE="docker compose"
elif command -v docker-compose &> /dev/null; then
    COMPOSE="docker-compose"
else
    echo "Docker Compose is not installed. Install it first." >&2
    exit 1
fi

compose() {
    (cd "$DOCKER_DIR" && $COMPOSE --env-file "$ROOT_DIR/.env" -p "$PROJECT" "$@")
}

case "${1:-}" in
    --stop)
        compose down
        echo "PlexBot stack stopped."
        exit 0
        ;;
    --logs)
        compose logs -f plexbot
        exit 0
        ;;
esac

# Pre-flight checks
if ! docker info &> /dev/null; then
    echo "Docker daemon is not running or not accessible." >&2
    exit 1
fi

if [ ! -f "$ROOT_DIR/.env" ]; then
    echo "No .env at $ROOT_DIR/.env. Copy RenameMe.env.txt and fill it in." >&2
    exit 1
fi

if [ ! -f "$ROOT_DIR/config.fds" ] && [ -f "$ROOT_DIR/RenameMe.config.fds" ]; then
    echo "Creating config.fds from template..."
    cp "$ROOT_DIR/RenameMe.config.fds" "$ROOT_DIR/config.fds"
fi

mkdir -p "$ROOT_DIR/data" "$ROOT_DIR/logs"

# Clone the extensions listed in Install/extensions.txt that are missing, so their config fragments are merged below
"$SCRIPT_DIR/sync-extensions.sh" clone

# Regenerate the Lavalink config from base + extension fragments (same as linux-install.sh)
echo "Generating Lavalink configuration..."
docker run --rm --entrypoint sh \
    -v "$ROOT_DIR/Extensions:/extensions:ro" \
    -v "$DOCKER_DIR/lavalink.base.yml:/config/base.yml:ro" \
    -v "$DOCKER_DIR/generate-lavalink-config.sh:/config/generate.sh:ro" \
    -v "$DOCKER_DIR:/output" \
    mikefarah/yq:latest /config/generate.sh /extensions /output /config/base.yml

# Download any Lavalink plugin the config declares that is missing, before Lavalink starts
"$SCRIPT_DIR/sync-extensions.sh" plugins

if [ "${1:-}" = "--build" ]; then
    echo "Starting PlexBot stack (rebuilding bot image)..."
    compose up -d --build --force-recreate
else
    echo "Starting PlexBot stack..."
    compose up -d
fi

# Wait for Lavalink to answer before reporting success.
# The bot retries its connection, but a ready Lavalink avoids a noisy first start.
LAVALINK_PORT=$(grep -E '^LAVALINK_SERVER_PORT=' "$ROOT_DIR/.env" | tail -1 | cut -d= -f2- | tr -d '\r')
LAVALINK_PASS=$(grep -E '^LAVALINK_SERVER_PASSWORD=' "$ROOT_DIR/.env" | tail -1 | cut -d= -f2- | tr -d '\r')
LAVALINK_PORT=${LAVALINK_PORT:-2333}
LAVALINK_PASS=${LAVALINK_PASS:-youshallnotpass}

echo -n "Waiting for Lavalink on port $LAVALINK_PORT"
ready=false
for _ in $(seq 1 60); do
    if curl -fsS -H "Authorization: $LAVALINK_PASS" "http://localhost:$LAVALINK_PORT/version" &> /dev/null; then
        ready=true
        break
    fi
    echo -n "."
    sleep 1
done
echo ""

if $ready; then
    echo "Lavalink is up."
else
    echo "Lavalink did not answer within 60s. Check: $SCRIPT_DIR/Docker && $COMPOSE -p $PROJECT logs lavalink" >&2
    exit 1
fi

compose ps
echo ""
echo "Bot logs:   $0 --logs"
echo "Stop:       $0 --stop"
