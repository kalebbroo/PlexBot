@echo off
SETLOCAL

:: Change directory to the script's location
cd %~dp0

:: Navigate to the root directory where .env is located
cd ..

:: Check for the .env file in the root directory
SET ENV_FILE=.env
echo Looking for .env file at: %cd%\%ENV_FILE%

IF NOT EXIST "%ENV_FILE%" (
    echo Error: .env file not found at %cd%\%ENV_FILE%
    echo Please ensure that the .env file exists and try again.
    pause
    exit /b 1
)

echo Found .env file

:: Now change to the Docker directory where docker-compose.yml is located
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
docker-compose build

:: Stop and remove any old containers
echo Stopping old containers...
docker-compose down

:: Start the new containers
echo Starting new containers...
docker-compose up -d

echo The application and Lavalink are now running.
pause
