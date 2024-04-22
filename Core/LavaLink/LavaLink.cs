using Lavalink4NET;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Players;
using Microsoft.Extensions.Options;

namespace PlexBot.Core.LavaLink
{
    public class LavaLinkCommands(IAudioService audioService, DiscordSocketClient discordClient)
    {
        private readonly IAudioService _audioService = audioService;
        private readonly DiscordSocketClient _discordClient = discordClient;
        private readonly Core.Commands.SlashCommands _commands;

        public async ValueTask<ILavalinkPlayer?> GetPlayerAsync(SocketInteraction interaction, bool connectToVoiceChannel = true)
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
    }
}
