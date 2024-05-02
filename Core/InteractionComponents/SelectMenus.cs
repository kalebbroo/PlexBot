using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using PlexBot.Core.PlexAPI;
using PlexBot.Core.Players;
using PlexBot.Core.LavaLink;
using Lavalink4NET.Players;

namespace PlexBot.Core.InteractionComponents
{
    public class SelectMenus(DiscordSocketClient client, PlexApi plexApi, LavaLinkCommands lavaLink, Players.Players visualPlayers) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client = client;
        private readonly PlexApi _plexApi = plexApi;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;
        private readonly Players.Players _visualPlayers = visualPlayers;

        [ComponentInteraction("search_plex:*", runMode: RunMode.Async)]
        public async Task DisplaySearchResults(string customId, string[] selections)
        {
            await DeferAsync();
            string selectedValue = selections.FirstOrDefault()!;
            Console.WriteLine(selectedValue);
            Console.WriteLine($"Custom ID: {customId}");
            if (string.IsNullOrEmpty(customId))
            {
                await FollowupAsync("No selection made.");
                return;
            }
            SocketInteraction interaction = Context.Interaction;
            if (interaction == null)
            {
                await FollowupAsync("An error occurred.");
                Console.WriteLine("Interaction is null.");
                return;
            }

            switch (customId)
            {
                case "track":
#warning TODO: Right now the selectedValue is the track URL. It needs to be the track ID Then logic to get the url from the ID needs to be added.
                    string url = plexApi.GetPlaybackUrl(selectedValue) ?? "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        Console.WriteLine($"Playing: {url}");

                        string jsonResponse = await plexApi.PerformRequestAsync(url);
                        Dictionary<string, Dictionary<string, string>> parseTrack = await plexApi.ParseSearchResults(jsonResponse, customId);
                        List<Dictionary<string, string>> track = [];

                        // Iterate through each entry in the original dictionary
                        foreach (var entry in parseTrack)
                        {
                            // Create a new dictionary for each entry
                            Dictionary<string, string> newDict = [];

                            // Copy key-value pairs from the inner dictionary to the new dictionary
                            foreach (var innerEntry in entry.Value)
                            {
                                newDict.Add(innerEntry.Key, innerEntry.Value);
                            }
                            // Add the new dictionary to the list
                            track.Add(newDict);
                            // call PlayMedia with the trackUrl
                            //await lavaLink.PlayMedia(interaction, url);
                        }
                        await FollowupAsync("Playing...", ephemeral: true);
                    }
                    break;
                case "album":
                case "artist":
                    #warning TODO: When users click an artist another select menu should appear with the albums
                    #warning TODO: When users click an album another select menu should appear with the tracks

                    #warning TODO: Break this out into a separate method (playAlbums) call it here and in the second select menu that we create for albums of atrists
                    List<Dictionary<string, string>> tracks = await plexApi.GetTracks(selectedValue);
                    Console.WriteLine($"Tracks: {tracks.Count}");
                    Console.WriteLine($"Selected Value: {selectedValue}");
                    await lavaLink.AddToQueue(interaction, tracks);
                    await FollowupAsync($"{customId} queued for playback.", ephemeral: true);
                    break;
            }
        }
    }
}
