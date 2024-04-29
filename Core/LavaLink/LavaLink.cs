using Lavalink4NET;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players;
using Microsoft.Extensions.Options;
using PlexBot.Core.Commands;
using Discord;
using Microsoft.Extensions.Caching.Memory;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System;
using Lavalink4NET.Events.Players;
using Lavalink4NET.DiscordNet;
using PlexBot.Core.Players;
using PlexBot.Core.LavaLink;

namespace PlexBot.Core.LavaLink
{
    public class LavaLinkCommands(IAudioService audioService, DiscordSocketClient discordClient, IMemoryCache memoryCache, Players.Players visualPlayer)
    {
        private readonly IAudioService _audioService = audioService;
        private readonly DiscordSocketClient _discordClient = discordClient;
        private readonly IMemoryCache _memoryCache = memoryCache;
        private readonly Players.Players _players = visualPlayer;

        public async Task<CustomPlayer?> GetPlayerAsync(SocketInteraction interaction, bool connectToVoiceChannel = true)
        {
            if (interaction.User is not SocketGuildUser user || user.VoiceChannel == null)
            {
                await interaction.FollowupAsync("You must be in a voice channel to play music.").ConfigureAwait(false);
                return null;
            }

            ulong guildId = user.Guild.Id;
            ulong voiceChannelId = user.VoiceChannel.Id;

            PlayerChannelBehavior channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            PlayerRetrieveOptions retrieveOptions = new(channelBehavior);

            // Create the player options
            CustomPlayerOptions options = new()
            {
                DisconnectOnStop = false,
                TextChannel = interaction.Channel as ITextChannel
            };
            // Wrap the options in an IOptions container 
            IOptions<CustomPlayerOptions> optionsWrapper = Options.Create(options);

            // Create a factory delegate for the custom player
            PlayerFactory<CustomPlayer, CustomPlayerOptions> factory = (properties, options) =>
            {
                return new ValueTask<CustomPlayer>(new CustomPlayer(properties, _players, this));
            };

            // Retrieve or create the player
            PlayerResult<CustomPlayer> result = await _audioService.Players
                .RetrieveAsync<CustomPlayer, CustomPlayerOptions>(guildId, voiceChannelId, factory, optionsWrapper, retrieveOptions)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                string errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                    _ => "Unknown error.",
                };
                await interaction.FollowupAsync(errorMessage).ConfigureAwait(false);
                return null;
            }
            return result.Player;
        }

        public async Task<bool> PlayMedia(SocketInteraction interaction, string url)
        {
            // Get the queue and make the cache match the queue Or maybe not use cache at all? and just save info to the player instance?
            QueuedLavalinkPlayer? player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                Console.WriteLine("Player not found.");
            }
            bool queue = true;
            if (player!.Queue.IsEmpty)
            {
                queue = false;
                Console.WriteLine("Queue is empty.");
            }
            
            // Play the track
            await player.PlayAsync(url).ConfigureAwait(false);
            return queue;
        }

        public async Task<string> TogglePauseResume(SocketInteraction interaction)
        {
            var player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                return "Player not found.";
            }

            if (player.State == PlayerState.Paused)
            {
                await player.ResumeAsync();
                return "Resumed.";
            }
            else
            {
                await player.PauseAsync();
                return "Paused.";
            }
        }

        public async Task DisplayQueueAsync(SocketInteraction interaction)
        {
            var player = await GetPlayerAsync(interaction, true);
            if (player == null)
            {
                //await RespondAsync("No queued player is currently active.", ephemeral: true);
                return;
            }

            if (player.Queue.IsEmpty)
            {
                //await RespondAsync("The queue is currently empty.", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("Current Music Queue")
                .WithDescription("Here are the details of the tracks in the queue:");

            foreach (var item in player.Queue)
            {
                // Access track details
                var track = item.Track;
                //embed.AddField(track.Title, $"Artist: {track.Author}\nDuration: {TimeSpan.FromMilliseconds(track.Duration).ToString(@"hh\:mm\:ss")}\nURL: [Listen]({track.Uri})");
            }

            //await RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        public void AddTracksToCache(List<Dictionary<string, string>> tracks)
        {
            string cacheKey = "queuedSongs";

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromHours(1))
                .SetPriority(CacheItemPriority.High);

            cacheEntryOptions.RegisterPostEvictionCallback((key, value, reason, state) =>
                Console.WriteLine($"Cache item evicted: {key} due to {reason}")
            );

            memoryCache.Set(cacheKey, tracks, cacheEntryOptions);
        }

        public void RemoveTrackFromCache(string trackId)
        {
            string cacheKey = "queuedSongs";

            if (memoryCache.TryGetValue(cacheKey, out object value) && value is List<Dictionary<string, string>> queuedSongs)
            {
                var trackToRemove = queuedSongs.FirstOrDefault(track => track["Url"] == trackId);
                if (trackToRemove != null)
                {
                    queuedSongs.Remove(trackToRemove);
                    memoryCache.Set(cacheKey, queuedSongs);
                }
            }
        }

        public List<Dictionary<string, string>> GetQueuedSongs()
        {
            string cacheKey = "queuedSongs";

            if (memoryCache.TryGetValue(cacheKey, out object value) && value is List<Dictionary<string, string>> queuedSongs)
            {
                return queuedSongs;
            }

            return new List<Dictionary<string, string>>();
        }
    }
}
