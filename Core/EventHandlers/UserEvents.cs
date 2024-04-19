using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PlexBot.Core.EventHandlers
{
    internal class UserEvents(DiscordSocketClient client)
    {
        private readonly DiscordSocketClient _client = client;

        /// <summary>Registers the necessary Discord event handlers.</summary>
        public void RegisterHandlers()
        {
            _client.UserJoined += OnUserJoinedAsync;
        }

        /// <summary>Handles the event when a user joins the guild.</summary>
        /// <param name="user">The user who joined the guild.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task OnUserJoinedAsync(SocketGuildUser user)
        {
            string[] channelNames = ["welcome", "rules", "generate", "info"];
            Dictionary<string, SocketTextChannel> channels = [];
        }
    }

}
