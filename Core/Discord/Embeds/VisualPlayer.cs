using PlexBot.Core.Models.Players;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;
using SixLabors.ImageSharp.Formats.Png;

namespace PlexBot.Core.Discord.Embeds;

public class VisualPlayer(VisualPlayerStateManager stateManager, IOptions<PlayerOptions> playerOptions, IAudioService audioService)
{
    /// <summary>Updates or creates the player UI with current track information and buttons using Components V2</summary>
    public async Task AddOrUpdateVisualPlayerAsync(ComponentBuilder components, bool recreateImage = false)
    {
        try
        {
            ulong guildId = stateManager.CurrentPlayerChannel?.GuildId ?? 0;
            CustomLavaLinkPlayer? player = guildId > 0
                ? await audioService.Players.GetPlayerAsync(guildId) as CustomLavaLinkPlayer
                : null;

            string statusLine = ComponentV2Builder.BuildPlayerStatusLine(
                player?.Volume ?? 0.2f,
                player?.RepeatMode ?? TrackRepeatMode.None);

            // Button/status-only update (no image regeneration needed)
            if (!recreateImage && stateManager.CurrentPlayerMessage != null)
            {
                MessageComponent cv2 = stateManager.UseModernPlayer
                    ? ComponentV2Builder.BuildModernPlayer(statusLine, components)
                    : BuildClassicCV2(player, statusLine, components);

                await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                {
                    msg.Components = cv2;
                    msg.Embed = null;
                    msg.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
                Logs.Debug("Updated player via CV2 successfully");
                return;
            }

            if (player?.CurrentItem is not CustomTrackQueueItem currentTrack)
            {
                Logs.Warning("Cannot update visual player: No current track");
                return;
            }

            // Update existing message with new image/content
            if (stateManager.CurrentPlayerMessage != null)
            {
                try
                {
                    if (stateManager.UseModernPlayer)
                    {
                        using MemoryStream memoryStream = new();
                        using SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(currentTrack, player);
                        await image.SaveAsync(memoryStream, new PngEncoder());
                        memoryStream.Position = 0;
                        FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                        MessageComponent cv2 = ComponentV2Builder.BuildModernPlayer(statusLine, components);
                        await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                        {
                            msg.Attachments = new[] { fileAttachment };
                            msg.Components = cv2;
                            msg.Embed = null;
                            msg.Flags = MessageFlags.ComponentsV2;
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        MessageComponent cv2 = BuildClassicCV2(player, statusLine, components);
                        await stateManager.CurrentPlayerMessage.ModifyAsync(msg =>
                        {
                            msg.Components = cv2;
                            msg.Embed = null;
                            msg.Attachments = new List<FileAttachment>();
                            msg.Flags = MessageFlags.ComponentsV2;
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

            // Create new player message
            if (stateManager.UseModernPlayer)
            {
                using MemoryStream memoryStream = new();
                using SixLabors.ImageSharp.Image image = await ImageBuilder.BuildPlayerImageAsync(currentTrack, player);
                await image.SaveAsync(memoryStream, new PngEncoder());
                memoryStream.Position = 0;
                FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
                MessageComponent cv2 = ComponentV2Builder.BuildModernPlayer(statusLine, components);
                stateManager.CurrentPlayerMessage = await stateManager.CurrentPlayerChannel!.SendFileAsync(
                    fileAttachment, components: cv2).ConfigureAwait(false);
            }
            else
            {
                MessageComponent cv2 = BuildClassicCV2(player, statusLine, components);
                stateManager.CurrentPlayerMessage = await stateManager.CurrentPlayerChannel!.SendMessageAsync(
                    components: cv2).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error updating visual player: {ex.Message}");
        }
    }

    private static MessageComponent BuildClassicCV2(CustomLavaLinkPlayer? player, string statusLine, ComponentBuilder buttons)
    {
        CustomTrackQueueItem? currentTrack = player?.CurrentItem as CustomTrackQueueItem;
        string trackInfo = currentTrack != null
            ? $"**\u25B6\uFE0F Now Playing**\n{currentTrack.Artist ?? "Unknown Artist"} - {currentTrack.Title ?? "Unknown Title"}\n" +
              $"{currentTrack.Album ?? "Unknown Album"} | {currentTrack.Duration ?? "0:00"}"
            : "**No track playing**";
        return ComponentV2Builder.BuildClassicPlayer(trackInfo, currentTrack?.Artwork, statusLine, buttons);
    }
}
