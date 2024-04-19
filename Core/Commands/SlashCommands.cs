using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using PlexBot.Core.LavaLink;
using PlexBot.Core.PlexAPI;

namespace PlexBot.Core.Commands
{
    public class SlashCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly IAudioService _audioService;
        private readonly LavaLinkCommands _lavaLinkCommands;
        private readonly PlexApi _plexApi;
        public SlashCommands(IAudioService audioService, LavaLinkCommands lavaLinkCommands, PlexApi plexApi)
        {
            _audioService = audioService;
            _lavaLinkCommands = lavaLinkCommands;
            _plexApi = plexApi;
        }

        /// <summary>Responds with help information about how to use the bot, including available commands.</summary>
        [SlashCommand("help", "Learn how to use the bot")]
        public async Task HelpCommand()
        {
            try
            {
                EmbedBuilder embed = new EmbedBuilder()
                    .WithTitle("Hartsy.AI Bot Help")
                    .WithThumbnailUrl(Context.Guild.IconUrl)
                    .WithDescription("Hartsy.AI is the premier Stable Diffusion platform for generating images with text directly in Discord. " +
                    "\n\nOur custom Discord bot enables users to generate images with text using our fine-tuned templates, choose your favorite " +
                    "images to send to #showcase for community voting, and potentially get featured weekly on the server. \n\nDiscover more and subscribe at: https://hartsy.ai")
                    .AddField("Available Slash Commands", "Checked the pinned messages for a more detailed explanation of these commands.", false)
                    .AddField("/generate", "Generate an image based on the text you provide, select a template, and optionally add extra prompt " +
                    "information. Example: `/generate_logo text:\"Your Text\" template:\"Template Name\" additions:\"Extra Prompt\"`", false)
                    .AddField("/user_info", "Check the status of your subscription and see how many tokens you have left for image generation. Example: `/user_info`", false)
                    .AddField("/help", "Shows this help message. Example: `/help`", false)
                    .WithColor(Color.Blue)
                    .WithFooter(footer => footer.Text = "For more information, visit Hartsy.AI")
                    .WithCurrentTimestamp();

                await RespondAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                await RespondAsync($"An error occurred: {ex.Message}", ephemeral: true);
            }
        }

        /// <summary>Main play command for plex</summary>
        [SlashCommand("play", "Plays music from YouTube", runMode: RunMode.Async)]
        public async Task Play(string query)
        {
            await DeferAsync().ConfigureAwait(false);
            SocketSlashCommand command = Context.Interaction as SocketSlashCommand;
            var player = await _lavaLinkCommands.GetPlayerAsync(command, connectToVoiceChannel: true).ConfigureAwait(false);

            if (player == null)
            {
                await FollowupAsync("You need to be in a voice channel.").ConfigureAwait(false);
                return;
            }

            // Load the track from YouTube using the query provided
            var track = await _audioService.Tracks
                .LoadTrackAsync(query, TrackSearchMode.YouTube)
                .ConfigureAwait(false);
            // If no track was found, we send an error message to the user.
            if (track is null)
            {
                await FollowupAsync("😖 No results.").ConfigureAwait(false);
                return;
            }

            // Play the track
            await player.PlayAsync(track);
            await FollowupAsync($"Playing: {track.Title}").ConfigureAwait(false);
        }

        /// <summary>Main search command for Plex</summary>
        [SlashCommand("search", "Search Plex for media")]
        public async Task SearchCommand(string query, string type = "track")
        {
            await RespondAsync($"Searching for: {query} as a {type}...");

            try
            {
                var results = await _plexApi.SearchLibraryAsync(query, type);
                if (results == null || results.Count == 0)
                {
                    await FollowupAsync("No results found.");
                    return;
                }

                // For simplicity, let's use the first result
                var firstResult = results.First();

                // Handling based on type
                string mediaUrl = "";
                if (type == "track" && !string.IsNullOrEmpty(firstResult.PartKey))
                {
                    mediaUrl = _plexApi.GetPlaybackUrl(firstResult.PartKey);
                }

                // Create and send an embed with details about the result
                var embed = new EmbedBuilder()
                    .WithTitle($"Search Result for {type}")
                    .WithDescription($"**Title:** {firstResult.Title}\n**Artist:** {firstResult.Artist}\n**Album:** {firstResult.Album}")
                    //.WithThumbnailUrl(firstResult.Thumb)
                    .WithColor(Color.Blue)
                    .Build();

                // Provide a link or command depending on the type
                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    embed.Fields.Add(new EmbedFieldBuilder()
                        .WithName("Playback")
                        .WithValue($"[Play]({mediaUrl})")
                        .Build());
                }

                // Additional handling if it's a track and you want to play it directly
                if (type == "track")
                {
                    FileStream file = File.OpenRead(mediaUrl);
                    FileAttachment attachment = new(file, "media.mp3");
                    await FollowupWithFileAsync(embed: embed, attachment: attachment);
                }
                else
                {
                    await FollowupAsync(embed: embed);
                }
            }
            catch (Exception ex)
            {
                await FollowupAsync($"An error occurred: {ex.Message}");
            }
        }

        /// <summary>Main playlist command for plex</summary>
        [SlashCommand("playlist", "Play a playlist")]
        public async Task PlaylistCommand()
        {
            await RespondAsync("Playing a playlist");
        }
    }
}
