using Discord;
using Discord.WebSocket;

namespace PlexBot.Core.EventHandlers
{
    // TODO: Actually do something with this class or remove it
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
            //string[] channelNames = ["welcome", "rules", "generate", "info"];
            //Dictionary<string, SocketTextChannel> channels = [];
            await user.SendMessageAsync("Welcome to the server!");
        }
    }

}
