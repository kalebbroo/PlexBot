using Discord;
using Lavalink4NET.Players.Queued;

namespace PlexBot.Core.Players
{
    public class Players
    {
        public Players()
        {
            // Initialize the Players class
        }
        // Not to be confused with the Players class in Lavalink4NET.Players
        // This class will contain methods for the visual representation of players in the bot
        // The players will be embeds that will contain now playing information, queue information, and other player information
        // The embeds will be made up of images overlayed on top of each other with text. Similar a Discord rank card
        // They may be updated every 5 seconds or so to show the current state of the player? if not it will update on next song that plays

        // Should the player be removed when songs are done playing? Or should it stay until the user leaves the voice channel?
        // What details should it contain? Should it contain the current song, the queue, the volume, the repeat mode, the shuffle mode, etc?

        // logic needed to get all info from Lavalink4NET.Players.Queued.QueuedLavalinkPlayer

        // Logic needed for image editing and text overlaying

        public static EmbedBuilder BuildAndSendPlayer(Dictionary<string, string> firstTrack)
        {
            EmbedBuilder player = CreatePlayer(firstTrack);
            return player;
        }

        private static EmbedBuilder CreatePlayer(Dictionary<string, string> firstTrack)
        {
            Dictionary<string, string> variables = [];
            foreach (KeyValuePair<string, string> kvp in firstTrack)
            {
                variables[kvp.Key] = kvp.Value;
                Console.WriteLine($"Key = {kvp.Key}, Value = {kvp.Value}");
            }
            // TODO: Convert milliseconds to minutes and seconds
            string title = "Now Playing";
            string description = $"{variables["Artist"]} - {variables["Title"]}\n{variables["Album"]} - {variables["Studio"]}\n\n" +
                $"{variables.GetValueOrDefault("Progress", "0:00")}/{variables["Duration"]}";
            string imageUrl = variables["Artwork"];
            string footer = $"Volume: {variables.GetValueOrDefault("Volume", "100%")}";
            // Create a new embed with the player information
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithImageUrl(imageUrl)
                .WithFooter(footer)
                .WithColor(Color.Blue)
                .WithTimestamp(DateTime.Now);
            return embed;
        }

        public async Task BuildImage()
        {
            // Build the image for the player embed
        }
    }
}
