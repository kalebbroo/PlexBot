# ![PlexBot Logo](./Docs/plexbot-logo.png)

# PlexBot <sup><kbd>Alpha 0.5</kbd></sup>

> **Play your Plex music in Discord with style.**

---

<!-- PLACEHOLDER: Add screenshots of the Modern Visual Player and Classic Player Embed below -->

| Modern Visual Player | Classic Player Embed |
|:-------------------:|:-------------------:|
| ![Modern Player Screenshot](./Docs/screenshots/modern-player.png) | ![Classic Player Screenshot](./Docs/screenshots/classic-player.png) |

---

## What Does this bot do and why did I make it?

**PlexBot** is a next-generation Discord music bot designed for Plex users. Seamlessly stream your personal music library (and more!) into your server’s voice channels, enjoy a beautiful visual player, and take advantage of a robust extension system for ultimate flexibility.

---

## ⭐ Features

- **Stream from Plex**: Play tracks, albums, artists, and playlists directly from your Plex server.
- **YouTube Support**: Search and play music from YouTube.
- **Interactive Player UI**: Choose between a modern image-based player or a classic Discord embed.
- **Static Player Channel**: Optionally dedicate a channel for the persistent player UI.
- **Rich Queue Management**: Add, remove, shuffle, and loop tracks with intuitive controls.
- **Slash Commands**: Clean, discoverable, and autocomplete-enabled.
- **Extensible**: Powerful [Extensions system](./Docs/Extensions/CreatingExtensions.md) for custom features.
- **Easy Setup**: [Guided installation](./Docs/Setup/Installation.md) and [Docker support](./Docs/Setup/Docker-Guide.md).
- **Troubleshooting & Guides**: [Player UI Guide](./Docs/Guides/Player-UI-Guide.md), [Troubleshooting](./Docs/Guides/Troubleshooting.md), and more.

---

## 🚧 Planned & Upcoming Features

- **More Music Sources**: Spotify, SoundCloud, and additional streaming integrations.
- **User Custom Playlists**: Save, manage, and share your own playlists within Discord.
- **Expanded Command Set**: More slash commands for advanced control and new features.
- **Command Panel UI**: In static player channels, use an interactive command panel (embed with buttons) for a seamless experience (no slash commands needed).
- **Additional Visual Player Styles**: Choose from more themes and layouts for the player UI.
- **And much more...**

---

## 🎨 Visual Player Styles

PlexBot offers two distinct player UIs:

### 1. Modern Visual Player
- **Sleek, image-based**: Uses album art as a background, overlaying track info and controls for a rich, modern look.
- **Best for dedicated channels**: Looks stunning as a persistent player in a static channel.

### 2. Classic Player Embed
- **Traditional Discord embed**: Familiar, compact, and works anywhere.
- **Great for multi-purpose channels**: Shows album art as a thumbnail.

#### Player Style Configuration
- Set the player style in your `.env` file:
  - `PLAYER_STYLE_VISUAL=true` (Modern Visual)
  - `PLAYER_STYLE_VISUAL=false` (Classic Embed)
- To use a static player channel, set:
  - `USE_STATIC_PLAYER_CHANNEL=true`
  - `STATIC_PLAYER_CHANNEL_ID=<channel_id>`

For more, see the [Player UI Guide](./Docs/Guides/Player-UI-Guide.md).

---

## ⚡ Slash Commands

<details>
<summary><b>/search [query] [source]</b></summary>
Search your Plex library or YouTube. Interactive menus for artists, albums, and tracks.
<br>Example: <code>/search query:"The Beatles" source:"plex"</code>
</details>

<details>
<summary><b>/playlist [playlist] [shuffle]</b></summary>
Play a full Plex playlist, optionally shuffled.
<br>Example: <code>/playlist playlist:"Summer Hits" shuffle:true</code>
</details>

<details>
<summary><b>/play [query]</b></summary>
Quickly play a track, album, or artist by search term.
<br>Example: <code>/play query:"Bohemian Rhapsody"</code>
</details>

<details>
<summary><b>/help</b></summary>
Show an interactive help menu with all commands and usage tips.
</details>

---

## 🚀 Getting Started

See the [Installation Guide](./Docs/Setup/Installation.md) and [Configuration Guide](./Docs/Setup/Configuration.md) for full details.

### Prerequisites
- Docker (Required)
- Discord bot token
- Plex server credentials (token & base URL)

### Quick Install
```bash
git clone https://github.com/kalebbroo/PlexBot.git
cd PlexBot
```
Config the .env and run the install_win.bat/install_linux.sh. This will build the Docker image, install dependencies, and start the container. 

### Basic Configuration
Rename `.envrename.txt` to `.env` and fill in your credentials. Example:
```env
DISCORD_TOKEN=your-discord-bot-token
PLEX_TOKEN=your-plex-token
PLEX_BASE_URL=your-plex-base-url
```

---

## 🐳 Docker Support

PlexBot supports Docker for easy deployment. See the [Docker Guide](./Docs/Setup/Docker-Guide.md).

---

## 🛠️ Extensions & Customization

PlexBot’s [Extensions system](./Docs/Extensions/CreatingExtensions.md) lets you add custom features, integrations, and automations. Build your own or browse community extensions.

---

## ❓ Support & Troubleshooting

- [Troubleshooting Guide](./Docs/Guides/Troubleshooting.md)
- [Player UI Guide](./Docs/Guides/Player-UI-Guide.md)
- [Command Reference](./Docs/Guides/Commands.md)

---

## 📜 License

MIT License. See [LICENSE](./LICENSE).

DOWNLOADING OR USING THIS SOFTWARE CONSTITUTES ACCEPTANCE OF THE TERMS AND CONDITIONS OF THE MIT LICENSE. THIS SOFTWARE IS PROVIDED "AS IS" AND WITHOUT WARRANTIES OF ANY KIND, EITHER EXPRESSED OR IMPLIED. 

---

> PlexBot is not affiliated with Plex, YouTube, or Discord.
> PlexBot and Kaalebbroo.Dev are affiliated with Hartsy.AI (Allowing artists to control how their work is used)