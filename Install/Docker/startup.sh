#!/bin/bash
set -e

APP_DIR="/app"
SOURCE_DIR="/source"

# Verify .env file exists
if [ ! -f "$APP_DIR/.env" ]; then
    echo "ERROR: No .env file found at $APP_DIR/.env"
    echo "Please create a .env file with your Discord token and Plex server details."
    exit 1
fi

# Pull latest source if git repo is mounted
if [ -d "$SOURCE_DIR/.git" ]; then
    echo "Checking for source updates..."
    cd "$SOURCE_DIR"
    git pull || echo "Warning: git pull failed, continuing with existing source"
    cd "$APP_DIR"
fi

# Regenerate Lavalink config from base template + extension fragments.
# The generated file is mounted directly into the Lavalink container from the host.
# This covers live-source-mount scenarios where extensions changed after initial startup.
DOCKER_DIR="/source/Install/Docker"
EXTENSIONS_DIR="${EXTENSIONS_SOURCE_DIR:-/source/Extensions}"
if [ -f "$DOCKER_DIR/generate-lavalink-config.sh" ] && command -v yq >/dev/null 2>&1; then
    echo "Regenerating Lavalink configuration..."
    sh "$DOCKER_DIR/generate-lavalink-config.sh" "$EXTENSIONS_DIR" "$DOCKER_DIR" "$DOCKER_DIR/lavalink.base.yml"
    echo "Note: Restart Lavalink container if plugin config changed."
fi

BUILD_MARKER="$APP_DIR/.last-build"

# Check if rebuild is needed
need_rebuild() {
    # No DLL = need build
    if [ ! -f "$APP_DIR/PlexBot.dll" ]; then
        echo "No binaries found, rebuild needed."
        return 0
    fi

    # No source mounted = skip rebuild
    if [ ! -d "$SOURCE_DIR" ] || [ ! -f "$SOURCE_DIR/PlexBot.csproj" ]; then
        return 1
    fi

    # No build marker = need build (first run or marker was cleared)
    if [ ! -f "$BUILD_MARKER" ]; then
        echo "No build marker found, rebuild needed."
        return 0
    fi

    # Compare newest source file to build marker (not DLL timestamps, which are
    # unreliable across Windows host / Linux container volume mounts)
    # Exclude bin/obj dirs which contain auto-generated files from builds
    NEWEST_SOURCE=$(find "$SOURCE_DIR" -type d \( -name bin -o -name obj \) -prune -o -type f \( -name "*.cs" -o -name "*.csproj" \) -newer "$BUILD_MARKER" -print -quit 2>/dev/null)

    if [ -n "$NEWEST_SOURCE" ]; then
        echo "Source is newer than last build, rebuild needed."
        return 0
    fi

    return 1
}

# Rebuild if needed
if need_rebuild; then
    echo "Rebuilding project from source..."
    cd "$SOURCE_DIR"
    dotnet restore
    dotnet publish -c Release -o "$APP_DIR"
    touch "$BUILD_MARKER"
    echo "Rebuild complete."
fi

# Copy config.fds into the app directory (after rebuild so publish doesn't delete it)
if [ ! -f "$APP_DIR/config.fds" ]; then
    if [ -f "$SOURCE_DIR/config.fds" ]; then
        # User has a custom config in the project root — use it
        echo "Found user config.fds — copying to app directory..."
        cp "$SOURCE_DIR/config.fds" "$APP_DIR/config.fds"
    elif [ -f "$SOURCE_DIR/RenameMe.config.fds" ]; then
        # No user config — create from template with defaults
        echo "No config.fds found — creating from template with defaults..."
        cp "$SOURCE_DIR/RenameMe.config.fds" "$APP_DIR/config.fds"
    else
        echo "Warning: No config.fds found. Bot will use built-in defaults."
    fi
fi

# Start the application (exec replaces shell for proper signal handling)
echo "Starting PlexBot..."
cd "$APP_DIR"
exec dotnet PlexBot.dll
