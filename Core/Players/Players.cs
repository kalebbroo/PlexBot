using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlexBot.Core.Players
{
    internal class Players
    {
        // Not to be confused with the Players class in Lavalink4NET.Players
        // This class will contain methods for the visual representation of players in the bot
        // The players will be embeds that will contain now playing information, queue information, and other player information
        // The embeds will be made up of images overlayed on top of each other with text. Similar a Discord rank card
        // They may be updated every 5 seconds or so to show the current state of the player? if not it will update on next song that plays

        // Should the player be removed when songs are done playing? Or should it stay until the user leaves the voice channel?
        // What details should it contain? Should it contain the current song, the queue, the volume, the repeat mode, the shuffle mode, etc?

        // logic needed to get all info from Lavalink4NET.Players.Queued.QueuedLavalinkPlayer

        // Logic needed for image editing and text overlaying
    }
}
