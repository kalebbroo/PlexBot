using Discord;
using Discord.WebSocket;
using PlexBot.Core.LavaLink;
using static System.Net.WebRequestMethods;
// add using for Console


namespace PlexBot.Core.Players
{
    public class Players(LavaLinkCommands lavaLink)
    {
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;
        // Not to be confused with the Players class in Lavalink4NET.Players
        // This class will contain methods for the visual representation of players in the bot
        // The players will be embeds that will contain now playing information, queue information, and other player information
        // The embeds will be made up of images overlayed on top of each other with text. Similar a Discord rank card
        // They may be updated every 5 seconds or so to show the current state of the player? if not it will update on next song that plays

        // Should the player be removed when songs are done playing? Or should it stay until the user leaves the voice channel?
        // What details should it contain? Should it contain the current song, the queue, the volume, the repeat mode, the shuffle mode, etc?

        // logic needed to get all info from Lavalink4NET.Players.Queued.QueuedLavalinkPlayer

        // Logic needed for image editing and text overlaying

        public async Task<EmbedBuilder> BuildAndSendPlayer(SocketInteraction interaction, List<Dictionary<string, string>> tracks)
        {
            // Check if the tracks list is not null and contains at least one dictionary
            if (tracks != null && tracks.Count > 0)
            {
                // Access the first dictionary in the tracks list
                Dictionary<string, string> firstTrack = tracks[0];

                // Try to get the value associated with the "Url" key
                if (firstTrack.TryGetValue("Url", out string url))
                {
                    Console.WriteLine($"The 'Url' key was found in the dictionary: {url}");
                    // If the "Url" key exists in the dictionary, proceed with playing the media
                    url = $"http://72.15.115.79:32400{url}?X-Plex-Token=GEqKQjTDC3DtXy4CToyr";
                    bool hasQueue = await lavaLink.PlayMedia(interaction, url);
                    EmbedBuilder player = await CreatePlayer(tracks, hasQueue);
                    return player;
                }
                else
                {
                    // Handle the case where the "Url" key is not found in the dictionary
                    Console.WriteLine("The 'Url' key was not found in the dictionary.");
                    // You might want to return an error message or take appropriate action here
                }
            }
            else
            {
                // Handle the case where the tracks list is either null or empty
                Console.WriteLine("The tracks list is either null or empty.");
                // You might want to return an error message or take appropriate action here
            }

            // Return null if there was an error or if the tracks list was empty
            return null;
        }

        public async Task<EmbedBuilder> CreatePlayer(List<Dictionary<string, string>> tracks, bool hasQueue)
        {
            Dictionary<string, string> firstTrack = tracks[0];
            // parse the tracks to get the current song
            string artistName = firstTrack.ContainsKey("grandparentTitle") ? firstTrack["grandparentTitle"] : "Unknown Artist";
            string songName = firstTrack.ContainsKey("title") ? firstTrack["title"] : "Unknown Song";
            string albumImage = firstTrack.ContainsKey("thumb") ? firstTrack["thumb"] : "";
            string albumName = firstTrack.ContainsKey("parentTitle") ? firstTrack["parentTitle"] : "Unknown Album";
            string studioName = firstTrack.ContainsKey("studio") ? firstTrack["studio"] : "Unknown Studio";
            string songDuration = firstTrack.ContainsKey("duration") ? firstTrack["duration"] : "0";
            string songVolume = "100%";
            string songProgress = "0:00";

            string title = "Now Playing";
            string description = $"{artistName} - {songName}\n{albumName} - {studioName}\n\n{songProgress}/{songDuration}";
            string imageUrl = albumImage;
            string footer = $"Volume: {songVolume}";

            if (hasQueue)
            {
                title = "Waiting for songs to be played";
                description = "No songs are currently playing";
                imageUrl = "";
                footer = "Volume: 100%";

            }
            // Create a new embed with the player information
            // Add the embed to the player list
            EmbedBuilder embed = new();
            embed.WithTitle(title);
            embed.WithDescription(description);
            embed.WithImageUrl(imageUrl);
            embed.WithFooter(footer);
            embed.WithColor(Color.Blue);
            embed.WithTimestamp(DateTime.Now);

            return embed;
            
        }

        public async Task UpdatePlayer()
        {
            // Update the player embed with the new information
            
        }

        public async Task BuildImage()
        {
            // Build the image for the player embed
            
        }
    }
}
