# Plex Music Bot
This Discord bot allows users to play audio from their Plex library and YouTube videos in a voice channel.

# Features
Search for and play songs from a Plex library
Play audio from YouTube videos
Manage a song queue with play, pause, resume, skip, and shuffle functionality
Display the current song and queue

# Prerequisites
Python 3.7 or higher
A Discord bot token
A Plex server with valid credentials

# Installation
Clone this repository or download it as a ZIP file and extract it.

git clone https://github.com/yourusername/your-repo.git
Change the working directory to the project folder.

cd your-repo
Create a virtual environment and activate it.

python -m venv venv
source venv/bin/activate  # For Windows: venv\Scripts\activate
Install the required dependencies.

pip install -r requirements.txt
Set up environment variables for your Discord bot token, Plex username, and Plex password. You can either use an .env file or set the environment variables in your shell

# Using an .env file
echo "DISCORD_TOKEN=your-discord-bot-token" > .env
echo "PLEX_USERNAME=your-plex-username" >> .env
echo "PLEX_PASSWORD=your-plex-password" >> .env

# Or setting environment variables in your shell (Linux and macOS)
export DISCORD_TOKEN=your-discord-bot-token
export PLEX_USERNAME=your-plex-username
export PLEX_PASSWORD=your-plex-password

# Or setting environment variables in your shell (Windows)
set DISCORD_TOKEN=your-discord-bot-token
set PLEX_USERNAME=your-plex-username
set PLEX_PASSWORD=your-plex-password
Modify the bot script to use the environment variables instead of hardcoded values:

# ...
Running the bot
Ensure that the virtual environment is activated.

source venv/bin/activate  # For Windows: venv\Scripts\activate
Run the bot script.

python plex_music_bot.py
Invite the bot to your Discord server using the following link, replacing YOUR_CLIENT_ID with your bot's client ID:

https://discord.com/oauth2/authorize?client_id=YOUR_CLIENT_ID&permissions=8&scope=bot
Once the bot is running and invited to your server, you can start using the commands in a text channel. Use !help to see the available commands and their usage.
Updating the bot
To update the bot with new features or bug fixes, you can pull the latest changes from the repository (if you cloned it) or download and extract the new version as a ZIP file.

Pull the latest changes (if you cloned the repository).

git pull
Or download the new version as a ZIP file and extract it.

Update the dependencies in the virtual environment.

pip install -r requirements.txt
Restart the bot by stopping the current instance and running it again.

python plex_music_bot.py


Contributing
Feel free to submit issues, feature requests, or pull requests to contribute to this project.