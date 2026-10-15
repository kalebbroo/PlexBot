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
set "MANIFEST=%SCRIPT_DIR%extensions.txt"
set "EXTENSIONS_DIR=%ROOT_DIR%\Extensions"
set "CONFIG_FILE=%ROOT_DIR%\config.fds"
set "PLUGINS_DIR=%DOCKER_DIR%\plugins"
set "LAVALINK_CONFIG=%DOCKER_DIR%\lavalink.application.yml"
set "RELEASES_REPO=https://maven.lavalink.dev/releases"

REM Check for Docker
docker --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo Docker is not installed. Please install Docker Desktop for Windows first.
    echo Visit: https://www.docker.com/products/docker-desktop
    pause
    exit /b 1
)

REM Detect compose command (plugin "docker compose" preferred, fallback to standalone)
set "COMPOSE=docker compose"
docker compose version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    docker-compose --version >nul 2>&1
    if %ERRORLEVEL% NEQ 0 (
        echo Docker Compose is not installed. It should come with Docker Desktop.
        echo Please ensure Docker is properly installed.
        pause
        exit /b 1
    )
    set "COMPOSE=docker-compose"
)

echo Using compose command: %COMPOSE%

REM Create plugins directory if it doesn't exist
if not exist "%DOCKER_DIR%\plugins" mkdir "%DOCKER_DIR%\plugins"

REM Create config.fds from template if it doesn't exist. The clone step reads its YouTube switch, so this must come first
if not exist "%ROOT_DIR%\config.fds" (
    if exist "%ROOT_DIR%\RenameMe.config.fds" (
        echo No config.fds found — creating from template with defaults...
        copy "%ROOT_DIR%\RenameMe.config.fds" "%ROOT_DIR%\config.fds" >nul
        echo Edit config.fds to customize player settings.
    ) else (
        echo Warning: No config.fds or RenameMe.config.fds found. Bot will use built-in defaults.
    )
)

REM Clone or rename the extensions listed in Install\extensions.txt, so their config fragments are merged below
call :clone_extensions

REM Generate Lavalink config from base template + extension plugin fragments.
REM Uses mikefarah/yq Docker image. Output is mounted directly into the Lavalink container.
echo Generating Lavalink configuration from base template + extension fragments...
docker run --rm -v "%ROOT_DIR%\Extensions:/extensions:ro" -v "%DOCKER_DIR%\lavalink.base.yml:/config/base.yml:ro" -v "%DOCKER_DIR%\generate-lavalink-config.sh:/config/generate.sh:ro" -v "%DOCKER_DIR%:/output" mikefarah/yq:latest sh /config/generate.sh /extensions /output /config/base.yml

REM Download any Lavalink plugin the config declares that is missing, before Lavalink starts
call :fetch_plugins
if errorlevel 1 (
    echo Lavalink cannot start without these plugins. Fix the network or the version, then run this script again.
    pause
    exit /b 1
)

REM Check if .env file exists
if not exist "%ROOT_DIR%\.env" (
    echo.
    echo No .env file found at: %ROOT_DIR%\.env
    echo Please create one from the template:
    echo   copy RenameMe.env.txt .env
    echo Then fill in your Discord token and Plex server details.
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

cd "%DOCKER_DIR%"

REM Stop existing containers gracefully (preserve volumes/data)
%COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot down --remove-orphans 2>nul

REM Build and start containers
%COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot up -d --build

echo.
echo PlexBot installation completed successfully!
echo The bot should now be running in the background.
echo.
echo Useful commands:
echo   View logs:       %COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot logs -f
echo   Stop bot:        %COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot down
echo   Restart bot:     %COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot restart
echo   Rebuild ^& start: %COMPOSE% --env-file "%ROOT_DIR%\.env" -p plexbot up -d --build
echo.
pause
exit /b 0

:clone_extensions
if not exist "%MANIFEST%" (
    echo [extensions] no manifest at %MANIFEST%, nothing to clone
    exit /b 0
)
if not exist "%EXTENSIONS_DIR%" mkdir "%EXTENSIONS_DIR%"
for /f "usebackq eol=# tokens=1,2,3" %%U in ("%MANIFEST%") do (
    set "cloneUrl=%%U"
    set "cloneName=%%V"
    set "cloneKey=%%W"
    call :clone_one
)
exit /b 0

:clone_one
if "%cloneName%"=="" (
    echo [extensions] skipped %cloneUrl%: the manifest line needs a folder name
    exit /b 0
)
set "target=%EXTENSIONS_DIR%\%cloneName%"
if not "%cloneKey%"=="" (
    call :apply_switch
    if not errorlevel 1 exit /b 0
)
if exist "%target%" (
    echo [extensions] %cloneName%: already present
    exit /b 0
)
if exist "%target%.disabled" (
    echo [extensions] %cloneName%: disabled, left alone
    exit /b 0
)
git clone --quiet "%cloneUrl%" "%target%" <nul
if errorlevel 1 (
    echo [extensions] WARNING: could not clone %cloneName% from %cloneUrl%. The bot starts without it.
    exit /b 0
)
echo [extensions] %cloneName%: cloned from %cloneUrl%
exit /b 0

:apply_switch
set "switchValue="
if exist "%CONFIG_FILE%" for /f "usebackq delims=" %%S in (`docker run --rm -v "%CONFIG_FILE%:/config.fds:ro" mikefarah/yq:latest ".%cloneKey%" /config.fds 2^>nul`) do set "switchValue=%%S"
if "%switchValue%"=="true" goto switch_on
if "%switchValue%"=="false" goto switch_off
echo [extensions] %cloneName%: %cloneKey% is not set or unreadable in config.fds, left as it is
exit /b 0

:switch_on
if exist "%target%" (
    echo [extensions] %cloneName%: %cloneKey% is true, already present
    exit /b 0
)
if exist "%target%.disabled" (
    move "%target%.disabled" "%target%" >nul
    if errorlevel 1 (
        echo [extensions] WARNING: could not restore %cloneName% from %cloneName%.disabled
    ) else (
        echo [extensions] %cloneName%: %cloneKey% is true, restored from %cloneName%.disabled
    )
    exit /b 0
)
echo [extensions] %cloneName%: %cloneKey% is true, cloning
exit /b 1

:switch_off
if not exist "%target%" goto switch_off_missing
if exist "%target%.disabled" (
    echo [extensions] WARNING: %cloneName% and %cloneName%.disabled both exist. Left both as they are
    exit /b 0
)
move "%target%" "%target%.disabled" >nul
if errorlevel 1 (
    echo [extensions] WARNING: could not rename %cloneName% to %cloneName%.disabled
    exit /b 0
)
echo [extensions] %cloneName%: %cloneKey% is false, renamed to %cloneName%.disabled
exit /b 0

:switch_off_missing
if exist "%target%.disabled" (
    echo [extensions] %cloneName%: %cloneKey% is false, already disabled
    exit /b 0
)
echo [extensions] %cloneName%: %cloneKey% is false, not present
exit /b 0

:fetch_plugins
if not exist "%LAVALINK_CONFIG%" (
    echo [plugins] no generated Lavalink config at %LAVALINK_CONFIG%, nothing to fetch
    exit /b 0
)
if not exist "%PLUGINS_DIR%" mkdir "%PLUGINS_DIR%"
set "present=0"
set "fetched=0"
set "failed=0"
for /f "usebackq tokens=*" %%L in (`findstr /c:"- dependency:" "%LAVALINK_CONFIG%" 2^>nul`) do (
    set "entry=%%L"
    call :fetch_one
)
echo [plugins] %present% present, %fetched% downloaded, %failed% failed
if %failed% GTR 0 (
    exit /b 1
)
exit /b 0

:fetch_one
set "dependency=%entry:*dependency:=%"
set "dependency=%dependency: =%"
set "dependency=%dependency:"=%"
set "dependency=%dependency:'=%"
set "group="
set "artifact="
set "version="
for /f "tokens=1,2,3 delims=:" %%G in ("%dependency%") do (
    set "group=%%G"
    set "artifact=%%H"
    set "version=%%I"
)
if not defined version (
    echo [plugins] WARNING: cannot read the plugin %dependency%
    set /a failed+=1
    exit /b 0
)
set "jar=%artifact%-%version%.jar"
if exist "%PLUGINS_DIR%\%jar%" (
    echo [plugins] %jar%: present
    set /a present+=1
    exit /b 0
)
set "groupPath=%group:.=/%"
set "url=%RELEASES_REPO%/%groupPath%/%artifact%/%version%/%jar%"
set "partial=%PLUGINS_DIR%\.%jar%.part"
echo [plugins] downloading %jar%
curl.exe -fSL --retry 2 -m 300 -o "%partial%" "%url%"
if errorlevel 1 goto plugin_failed
move /y "%partial%" "%PLUGINS_DIR%\%jar%" >nul
if errorlevel 1 goto plugin_failed
set /a fetched+=1
exit /b 0

:plugin_failed
if exist "%partial%" del "%partial%"
echo [plugins] FAILED to download %jar% from %url%
set /a failed+=1
exit /b 0
