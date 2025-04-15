using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Lavalink4NET.DiscordNet;
using PlexBot.Core.Models.Players;
using PlexBot.Services;
using PlexBot.Services.LavaLink;
using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;

namespace PlexBot.Core.Discord.Embeds;

public class VisualPlayer(VisualPlayerStateManager stateManager, IOptions<PlayerOptions> playerOptions, IAudioService audioService)
{
    /// <summary>Updates or creates the player UI with current track information and buttons</summary>
    public async Task AddOrUpdateVisualPlayerAsync(ComponentBuilder components, bool recreateImage = false)
    {
        try
        {
            // Simple button update if we don't need to recreate the image and have an existing message
            if (!recreateImage && stateManager.CurrentPlayerMessage != null)
            {
                await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                {
                    msg.Components = components.Build();
                }).ConfigureAwait(false);
                Logs.Debug("Updated player buttons successfully");
                return;
            }
            ulong guildId = stateManager.CurrentPlayerChannel.GuildId;
            if (await audioService.Players.GetPlayerAsync(guildId) is not CustomLavaLinkPlayer player)
            {
                Logs.Error("Cannot update visual player: No active player found in AddOrUpdateVisualPlayerAsync");
                return;
            }
            // For visual changes or new messages, we need the track and channel
            if (player?.CurrentItem is not CustomTrackQueueItem currentTrack)
            {
                Logs.Warning("Cannot update visual player: No current track");
                return;
            }
            if (stateManager.CurrentPlayerMessage != null)
            {
                try
                {
                    if (stateManager.UseVisualPlayer)
                    {
                        using MemoryStream memoryStream = new();
                        SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(currentTrack, player);
                        await image.SaveAsync(memoryStream, new PngEncoder());
                        memoryStream.Position = 0;
                        FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                        await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                        {
                            msg.Attachments = new[] { fileAttachment };
                            msg.Components = components.Build();
                            msg.Embed = null;
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        // Update using the Classic Visual Player
                        EmbedBuilder embed = DiscordEmbedBuilder.BuildPlayerEmbed(currentTrack, currentTrack.Artwork);
                        await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                        {
                            msg.Embed = embed.Build();
                            msg.Components = components.Build();
                            msg.Attachments = new List<FileAttachment>();
                        }).ConfigureAwait(false);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Logs.Warning($"Failed to update existing player, creating new one: {ex.Message}");
                    try
                    {
                        await stateManager.CurrentPlayerMessage.DeleteAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore delete failures
                    }
                    stateManager.CurrentPlayerMessage = null;
                }
            }
            else
            {
                if (stateManager.UseVisualPlayer)
                {
                    using MemoryStream memoryStream = new();
                    SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(currentTrack, player);
                    await image.SaveAsync(memoryStream, new PngEncoder());
                    memoryStream.Position = 0;
                    FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                    stateManager.CurrentPlayerMessage = await stateManager.CurrentPlayerChannel.SendFileAsync(fileAttachment, 
                        components: components.Build()).ConfigureAwait(false);
                }
                else
                {
                    EmbedBuilder embed = DiscordEmbedBuilder.BuildPlayerEmbed(currentTrack, currentTrack.Artwork);
                    stateManager.CurrentPlayerMessage = await stateManager.CurrentPlayerChannel.SendMessageAsync(embed: embed.Build(), 
                        components: components.Build()).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error updating visual player: {ex.Message}");
        }
    }
}
