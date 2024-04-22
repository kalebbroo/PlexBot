using Discord.WebSocket;
using PlexBot.Core.PlexAPI;
using Discord.Interactions;
using PlexBot.Core.LavaLink;
using Lavalink4NET.Players;

namespace PlexBot.Core.InteractionComponents
{
    public class SelectMenus(DiscordSocketClient client, PlexApi plexApi, LavaLinkCommands lavaLink) : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DiscordSocketClient _client = client;
        private readonly PlexApi _plexApi = plexApi;
        private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;

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
            ILavalinkPlayer? player = await _lavaLinkCommands.GetPlayerAsync(interaction, connectToVoiceChannel: true);
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
                        await player.PlayAsync(url);
                        await FollowupAsync("Playing...");
                    }
                    break;
                case "album":
                    var albumTracks = await plexApi.GetTracks(selectedValue);
                    foreach (var track in albumTracks)
                    {
                        string trackUrl = plexApi.GetPlaybackUrl(track);
                        if (!string.IsNullOrEmpty(trackUrl))
                        {
                            Console.WriteLine($"Queuing album track: {trackUrl}");
                            await player.PlayAsync(trackUrl);
                        }
                    }
                    await FollowupAsync("Album queued for playback.");
                    break;
                case "artist":
                    var artistTracks = await plexApi.GetTracks(selectedValue);
                    foreach (var track in artistTracks)
                    {
                        string trackUrl = plexApi.GetPlaybackUrl(track);
                        if (!string.IsNullOrEmpty(trackUrl))
                        {
                            Console.WriteLine($"Queuing artist track: {trackUrl}");
                            await player.PlayAsync(trackUrl);
                        }
                    }
                    await FollowupAsync("Artist's tracks queued for playback.");
                    break;
            }
        }
    }
}
