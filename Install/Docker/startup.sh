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

# Create config.fds from template if it doesn't exist (after rebuild so publish doesn't delete it)
if [ ! -f "$APP_DIR/config.fds" ]; then
    TEMPLATE="$SOURCE_DIR/RenameMe.config.fds"
    if [ -f "$TEMPLATE" ]; then
        echo "No config.fds found — creating from template with defaults..."
        cp "$TEMPLATE" "$APP_DIR/config.fds"
    else
        echo "Warning: No config.fds found. Bot will use built-in defaults."
    fi
fi

# Start the application (exec replaces shell for proper signal handling)
echo "Starting PlexBot..."
cd "$APP_DIR"
exec dotnet PlexBot.dll
