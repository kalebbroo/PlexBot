@echo off
setlocal enabledelayedexpansion

echo ===================================
echo PlexBot Installation Script
echo ===================================
echo.

REM Set paths relative to the script location
set "SCRIPT_DIR=%~dp0"
set "ROOT_DIR=%SCRIPT_DIR%.."
set "DOCKER_DIR=%SCRIPT_DIR%Docker"

REM Check for Docker
docker --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Docker is not installed. Please install Docker Desktop for Windows first.
    echo Visit: https://www.docker.com/products/docker-desktop
    pause
    exit /b 1
)

REM Check for Docker Compose
docker-compose --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Docker Compose is not installed. It should come with Docker Desktop.
    echo Please ensure Docker is properly installed.
    pause
    exit /b 1
)

REM Create extensions directory if it doesn't exist
if not exist "%DOCKER_DIR%\Extensions" mkdir "%DOCKER_DIR%\Extensions"

REM Check if we have a lavalink config file, create if not
if not exist "%DOCKER_DIR%\lavalink.application.yml" (
    echo Creating Lavalink configuration...
    (
        echo server:
        echo # Port and address come from environment variables
        echo lavalink:
        echo   server:
        echo     # Password comes from environment variables
        echo     sources:
        echo       youtube: false  # Disable built-in YouTube source as we're using the plugin
        echo       bandcamp: true
        echo       soundcloud: true
        echo       twitch: true
        echo       vimeo: true
        echo       http: true
        echo       local: false
        echo       nico: true
        echo     bufferDurationMs: 400
        echo     frameBufferDurationMs: 5000
        echo     youtubePlaylistLoadLimit: 10
        echo     playerUpdateInterval: 3
        echo     trackStuckThresholdMs: 10000
        echo     youtubeSearchEnabled: true
        echo     soundcloudSearchEnabled: true
        echo     gc-warnings: true
        echo   plugins:
        echo     - dependency: "dev.lavalink.youtube:youtube-plugin:1.13.1"
        echo       snapshot: false
        echo plugins:
        echo   youtube:
        echo     enabled: true
        echo     allowSearch: true
        echo     allowDirectVideoIds: true
        echo     allowDirectPlaylistIds: true
        echo     clients:
        echo        - TVHTML5EMBEDDED
        echo        - TV 
        echo     oauth:
        echo       enabled: true
        echo       refreshToken: ""
        echo logging:
        echo   file:
        echo     max-history: 30
        echo     max-size: 1GB
        echo   level:
        echo     root: INFO
        echo     lavalink: INFO
    ) > "%DOCKER_DIR%\lavalink.application.yml"
)
    
REM Check if .env file exists and prompt if it does not
if not exist "%ROOT_DIR%\.env" (
    echo.
    echo Please update the .env file with your Discord token and Plex server details.
    echo You MUST rename to .env and update the file with your own credentials before continuing.
    echo.
    pause
    exit /b 1
)

REM Make sure data and logs directories exist
if not exist "%ROOT_DIR%\data" mkdir "%ROOT_DIR%\data"
if not exist "%ROOT_DIR%\logs" mkdir "%ROOT_DIR%\logs"

echo.
echo Building and starting Docker containers...
echo.

REM Navigate to the Docker directory and run docker-compose
cd "%DOCKER_DIR%"

REM Stop and remove existing containers, networks, and volumes
docker-compose down --volumes --remove-orphans

REM Remove any existing images
docker rmi -f plexbot:latest
docker rmi -f ghcr.io/lavalink-devs/lavalink:4

REM Clear build cache
docker builder prune -f

REM Build and start the containers
docker-compose -p plexbot up -d --build

echo.
echo PlexBot installation completed successfully!
echo The bot should now be running in the background.
echo.
echo You can check the logs with: docker-compose logs -f
echo.
pause