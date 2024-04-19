using System;
using System.Threading.Tasks;
using Lavalink4NET;
using Lavalink4NET.Rest;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Interactions;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Players;
using Lavalink4NET.DiscordNet;
using Microsoft.Extensions.Options;

namespace PlexBot.Core.LavaLink
{
    public class LavaLinkCommands
    {
        private readonly IAudioService _audioService;
        private readonly DiscordSocketClient _discordClient;
        private readonly Core.Commands.SlashCommands _commands;

        public LavaLinkCommands(IAudioService audioService, DiscordSocketClient discordClient)
        {
            _audioService = audioService;
            _discordClient = discordClient;
        }

        public async ValueTask<ILavalinkPlayer?> GetPlayerAsync(SocketSlashCommand command, bool connectToVoiceChannel = true)
        {
            var user = command.User as SocketGuildUser;
            if (user == null || user.VoiceChannel == null)
            {
                await command.FollowupAsync("You must be in a voice channel to play music.").ConfigureAwait(false);
                return null;
            }

            var guildId = user.Guild.Id;
            var voiceChannelId = user.VoiceChannel.Id;

            var channelBehavior = connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None;
            var retrieveOptions = new PlayerRetrieveOptions(channelBehavior);

            // Create the player options
            var options = new QueuedLavalinkPlayerOptions
            {
                DisconnectOnStop = false  // Example setting, adjust as necessary
            };

            // Wrap the options in an IOptions container
            var optionsWrapper = Options.Create(options);

            // Retrieve or create the player
            var result = await _audioService.Players
                .RetrieveAsync(guildId, voiceChannelId, PlayerFactory.Queued, optionsWrapper, retrieveOptions)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                    _ => "Unknown error.",
                };

                await command.FollowupAsync(errorMessage).ConfigureAwait(false);
                return null;
            }

            return result.Player;
        }
    }
}
