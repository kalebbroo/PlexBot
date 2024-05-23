@echo off
SETLOCAL

:: Change directory to the script's location
cd %~dp0

:: Navigate to the root directory
cd ..

:: Pull the latest changes from GitHub
echo Pulling latest changes from GitHub...
git pull

:: Change to the Docker directory
cd Docker

:: Check for the docker-compose.yml file
IF NOT EXIST "docker-compose.yml" (
    echo Error: docker-compose.yml file not found at %cd%\docker-compose.yml
    echo Please ensure that the docker-compose.yml file exists and try again.
    pause
    exit /b 1
)

echo Found docker-compose.yml file

:: Pull the latest images
echo Pulling latest images...
docker-compose pull

:: Build the project
echo Building the Docker containers...
docker-compose build --no-cache

:: Stop and remove any old containers
echo Stopping old containers...
docker-compose down

:: Start the new containers
echo Starting new containers...
docker-compose -p plexbot up -d

echo PlexBot and Lavalink are now running.
pause
