using Lavalink4NET;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players;
using Microsoft.Extensions.Options;
using PlexBot.Core.Commands;
using Discord;

namespace PlexBot.Core.LavaLink
{
    public class LavaLinkCommands(IAudioService audioService, DiscordSocketClient discordClient)
    {
        private readonly IAudioService _audioService = audioService;
        private readonly DiscordSocketClient _discordClient = discordClient;
        private readonly SlashCommands _commands;

        public async ValueTask<QueuedLavalinkPlayer?> GetPlayerAsync(SocketInteraction interaction, bool connectToVoiceChannel = true)
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
            QueuedLavalinkPlayerOptions options = new()
            {
                DisconnectOnStop = false
            };

            // Wrap the options in an IOptions container 
            IOptions<QueuedLavalinkPlayerOptions> optionsWrapper = Options.Create(options);

            // Retrieve or create the player
            PlayerResult<QueuedLavalinkPlayer> result = await _audioService.Players
                .RetrieveAsync(guildId, voiceChannelId, PlayerFactory.Queued, optionsWrapper, retrieveOptions)
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


    }
}
