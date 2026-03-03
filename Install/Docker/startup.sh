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

    # Compare newest source file to newest binary
    NEWEST_SOURCE=$(find "$SOURCE_DIR" -type f \( -name "*.cs" -o -name "*.csproj" \) -printf '%T@\n' 2>/dev/null | sort -nr | head -n 1)
    NEWEST_BINARY=$(find "$APP_DIR" -maxdepth 1 -type f -name "*.dll" -printf '%T@\n' 2>/dev/null | sort -nr | head -n 1)

    if [ -z "$NEWEST_BINARY" ] || [ "${NEWEST_SOURCE%.*}" -gt "${NEWEST_BINARY%.*}" ] 2>/dev/null; then
        echo "Source is newer than binaries, rebuild needed."
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
    echo "Rebuild complete."
fi

# Start the application (exec replaces shell for proper signal handling)
echo "Starting PlexBot..."
cd "$APP_DIR"
exec dotnet PlexBot.dll
