#!/bin/bash
set -e

# Directories
SOURCE_DIR="/source"
APP_DIR="/app"

# TODO: Check for git and update the source code if needed
# If the source directory is a git repository, update the source code
#if [ -d "$SOURCE_DIR/.git" ]; then
#    echo "Checking for updates in source code..."
#    cd "$SOURCE_DIR"
#    git pull
#    cd -
#else 
#    echo "Source directory is not a git repository, skipping update."
#fi

# Function to check if rebuild is needed
need_rebuild() {
  # If the binaries directory doesn't exist or is empty, rebuild is needed
  if [ ! -d "$APP_DIR/bin" ]; then
    echo "Binary directory doesn't exist, rebuild needed."
    return 0
  fi

  if [ -z "$(ls -A "$APP_DIR/bin" 2>/dev/null)" ]; then
    echo "Binary directory is empty, rebuild needed."
    return 0
  fi

  # Find the newest modification time for source and binary files
  NEWEST_SOURCE=$(find "$SOURCE_DIR" -type f \( -name "*.cs" -o -name "*.csproj" \) -print0 | xargs -0 stat --format '%Y' | sort -nr | head -n 1)
  NEWEST_BINARY=$(find "$APP_DIR/bin" -type f -name "*.dll" -print0 | xargs -0 stat --format '%Y' | sort -nr | head -n 1)

  if [[ -z "$NEWEST_BINARY" || "$NEWEST_SOURCE" -gt "$NEWEST_BINARY" ]]; then
    echo "Source code is newer than binaries, rebuild needed."
    return 0
  fi

  return 1
}

# Rebuild the project if needed
if need_rebuild; then
    echo "Rebuilding project from source..."
    cd "$SOURCE_DIR"
    dotnet restore
    dotnet build -c Release
    # Publish the build output to the APP_DIR
    dotnet publish -c Release -o "$APP_DIR/publish"
    cd "$APP_DIR"
    echo "Rebuild complete."
fi

echo "Starting PlexBot application..."
cd "$APP_DIR"
dotnet PlexBot.dll
