# Plex Music Bot v1.0

This Discord bot allows users to play audio from their Plex library in a voice channel. The v1.0 release introduces a complete rewrite using C#, offering improved functionality, stability, and new interactive features.

## Features
- Search for and play songs from a Plex library.
- Play audio from YouTube and other popular websites.
- Manage a song queue with play, skip, shuffle, and loop functionality.
- Automatically disconnect after 2 minutes of inactivity when the queue is empty.
- Interactive buttons and slash commands for enhanced user control.

## Commands

<details>
  <summary><b>/search <query> <source></b></summary>
  Search media from various sources (Plex, YouTube, etc.). Select results from interactive menus.
  - **Example**: `/search query:"Your Query" source:"plex"`
</details>

<details>
  <summary><b>/playlist <playlist> [shuffle]</b></summary>
  Play songs from a specified Plex playlist. Optionally shuffle the playlist.
  - **Example**: `/playlist playlist:"Your Playlist" shuffle:true`
</details>

<details>
  <summary><b>/play <query></b></summary>
  Play music from YouTube using the specified query.
  - **Example**: `/play query:"Your Song"`
</details>

<details>
  <summary><b>/test_response <type> [query] [playlistID]</b></summary>
  Test API responses and return JSON in the console.
  - **Example**: `/test_response type:playlists`
</details>

<details>
  <summary><b>/help</b></summary>
  Show a summary of available commands.
  - **Example**: `/help`
</details>

## Visual Player
The bot features a visual player embedded in Discord that displays the now playing song information and provides interactive buttons for user control.

### Player Buttons
- **Play/Pause**: Toggle playback.
- **Skip**: Skip the current song and play the next song in the queue.
- **Shuffle**: Randomly shuffle the current song queue.
- **Loop**: Toggle loop mode for the current song or the entire queue.
- **Volume Up/Down**: Increase or decrease the playback volume.
- **Clear Queue**: Remove all songs from the current queue.
- **Disconnect**: Stop playing music and disconnect the bot from the voice channel.

## Prerequisites
- .NET 8.0 or higher
- A Discord bot token
- A Plex server with valid credentials (using a Plex token and base URL)

## Installation
Clone this repository or download it as a ZIP file and extract it.

```bash
git clone https://github.com/kalebbroo/plex_music_bot.git
cd plex_music_bot
```

## Configuration
- Rename the .envrename.txt to .env and add your credentials to the .env file.
- This is also where you can configure the bot's prefix and lavalink settings.

```env
echo "DISCORD_TOKEN=your-discord-bot-token" > .env
echo "PLEX_TOKEN=your-plex-token" >> .env
echo "PLEX_BASE_URL=your-plex-base-url" >> .env
```
## Running with Docker
Ensure Docker is installed on your system.
Clone the repository and navigate to the project folder:

Open the Install folder and double click on the Install.bat file to install the bot. This will build the Docker image for the bot and lavalink.

## Contributing
Feel free to submit issues, feature requests, or pull requests to contribute to this project.

## Join the Discord for Support
https://discord.com/invite/5m4Wyu52Ek