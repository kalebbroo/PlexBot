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
            ILavalinkPlayer? player = await lavaLink.GetPlayerAsync(interaction, connectToVoiceChannel: true);
            if (player == null)
            {
                await FollowupAsync("You need to be in a voice channel.");
                Console.WriteLine("Player is null.");
                return;
            }

            switch (customId)
            {
                case "track":
                    string url = plexApi.GetPlaybackUrl(selectedValue) ?? "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        Console.WriteLine($"Playing: {url}");
                        string uri = "/library/metadata/54186";
                        uri = _plexApi.GetPlaybackUrl(uri);
                        string jsonResponse = await plexApi.PerformRequestAsync(uri);
                        Dictionary<string, Dictionary<string, string>> parseTrack = await plexApi.ParseSearchResults(jsonResponse, customId);
                        List<Dictionary<string, string>> track = new List<Dictionary<string, string>>();

                        // Iterate through each entry in the original dictionary
                        foreach (var entry in parseTrack)
                        {
                            // Create a new dictionary for each entry
                            Dictionary<string, string> newDict = new Dictionary<string, string>();

                            // Copy key-value pairs from the inner dictionary to the new dictionary
                            foreach (var innerEntry in entry.Value)
                            {
                                newDict.Add(innerEntry.Key, innerEntry.Value);
                            }

                            // Add the new dictionary to the list
                            track.Add(newDict);
                        }
                        EmbedBuilder visualPlayer = await visualPlayers.BuildAndSendPlayer(interaction, track);
                        await FollowupAsync("Playing...", ephemeral: true);
                        // send new message to channel with visual player embed
                        await interaction.Channel.SendMessageAsync(embed: visualPlayer.Build());
                    }
                    break;
                case "album":
                case "artist":
                    var tracks = await plexApi.GetTracks(selectedValue);
                    Console.WriteLine($"Tracks: {tracks.Count}");
                    Console.WriteLine($"Selected Value: {selectedValue}");
                    foreach (var trackDetail in tracks)
                    {
                        string trackUrl = trackDetail["Url"];
                        if (!string.IsNullOrEmpty(trackUrl))
                        {
                            
                            trackUrl = plexApi.GetPlaybackUrl(trackUrl);
                            Console.WriteLine($"Queuing track: {trackUrl}");
                            await player.PlayAsync(trackUrl);
                        }
                    }
                    await FollowupAsync($"{customId} queued for playback.");
                    break;
            }
        }
    }
}
