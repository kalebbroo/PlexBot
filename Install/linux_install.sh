#!/bin/bash

set -e

# Change to the script's directory
cd "$(dirname "$0")"

# Navigate to the root directory where .env is located
cd ..

# Check for the .env file in the root directory
ENV_FILE=".env"
echo "Looking for .env file at: $(pwd)/$ENV_FILE"

if [ ! -f "$ENV_FILE" ]; then
  echo "Error: .env file not found at $(pwd)/$ENV_FILE"
  echo "Please ensure that the .env file exists and try again."
  exit 1
fi

echo "Found .env file"

# Now change to the Docker directory where docker-compose.yml is located
cd Docker

# Check for the docker-compose.yml file
if [ ! -f "docker-compose.yml" ]; then
  echo "Error: docker-compose.yml file not found at $(pwd)/docker-compose.yml"
  echo "Please ensure that the docker-compose.yml file exists and try again."
  exit 1
fi

echo "Found docker-compose.yml file"

# Pull the latest images
echo "Pulling latest images..."
docker-compose pull

# Build the project
echo "Building the Docker containers..."
docker-compose build --no-cache

# Stop and remove any old containers
echo "Stopping old containers..."
docker-compose down
# Remove dangling images to free up space
echo "Removing dangling images..."
docker image prune -f

# Start the new containers
echo "Starting new containers..."
docker-compose -p plexbot up -d

echo "PlexBot and Lavalink are now running."
