namespace PlexBot.Core.LavaLink;

public sealed class CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties, LavaLinkCommands lavaLink) : QueuedLavalinkPlayer(properties)
{
    private readonly ITextChannel? _textChannel = properties.Options.Value.TextChannel;
    private readonly LavaLinkCommands _lavaLinkCommands = lavaLink;

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        try
        {
            await base.NotifyTrackStartedAsync(track, cancellationToken).ConfigureAwait(false);
            CustomTrackQueueItem customTrack = (CustomTrackQueueItem)track;
            Dictionary<string, string> customTracks = new()
            {
                ["Title"] = customTrack.Title ?? "Missing Title",
                ["Artist"] = customTrack.Artist ?? "Missing Artist",
                ["Album"] = customTrack.Album ?? "Missing Album",
                ["Duration"] = customTrack.Duration ?? "00:00",
                ["Url"] = customTrack.Url ?? "N/A",
                ["ArtistUrl"] = customTrack.ArtistUrl ?? "N/A",
                ["ReleaseDate"] = customTrack.ReleaseDate ?? "N/A",
                ["Artwork"] = customTrack.Artwork ?? "https://via.placeholder.com/150",
                ["Studio"] = customTrack.Studio ?? "Missing Studio"
            };
            //Console.WriteLine($"Track: {customTracks["Title"]}, Artist: {customTracks["Artist"]}, Duration: {customTracks["Duration"]}"); // debug
            // Build the new player embed using the custom track information
            using MemoryStream memoryStream = new();
            Image<Rgba64> image = await BuildImage.BuildPlayerImage(customTracks);
            image.SaveAsPng(memoryStream);
            memoryStream.Position = 0;
            FileAttachment fileAttachment = new(memoryStream, "playerImage.png");
            string fileName = "playerImage.png";
            EmbedBuilder player = Players.Players.BuildAndSendPlayer(customTracks, $"attachment://{fileName}");
            // Create a ComponentBuilder for the buttons
            ComponentBuilder components = new ComponentBuilder()
                .WithButton("Pause", "pause_resume:pause", ButtonStyle.Secondary)
                .WithButton("Skip", "skip:skip", ButtonStyle.Primary)
                .WithButton("Queue Options", "queue_options:options:1", ButtonStyle.Success)
                .WithButton("Repeat", "repeat:select", ButtonStyle.Secondary)
                .WithButton("Kill", "kill:kill", ButtonStyle.Danger);
            // Find and delete the last player message (if it exists)
            IEnumerable<IMessage> messages = await _textChannel!.GetMessagesAsync(5).FlattenAsync().ConfigureAwait(false);
            IMessage? lastPlayerMessage = messages.FirstOrDefault(m => m.Embeds.Any(e => e.Title == "Now Playing"));
            if (lastPlayerMessage != null)
            {
                await lastPlayerMessage.DeleteAsync().ConfigureAwait(false);
                Console.WriteLine("Deleted last player message."); // debug
            }
            string tempFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            using (FileStream fileStream = new(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                memoryStream.CopyTo(fileStream);
            }
            await _textChannel.SendFileAsync(
                filePath: tempFilePath,
                embed: player.Build(),
                components: components.Build()
            ).ConfigureAwait(false);
            //await _textChannel.SendMessageAsync(components: components.Build(), embed: player.Build()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while notifying track started: {ex.Message}");
            await _textChannel!.SendMessageAsync("An error occurred while starting the track.").ConfigureAwait(false);
        }
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queueItem);
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);
        string trackTitle = queueItem.Track?.Title ?? "Default Title";
        //Console.WriteLine($"Track ended: {trackTitle}"); // debug
    }

    public ValueTask NotifyPlayerActiveAsync(CancellationToken cancellationToken = default)
    {
        // This method is called when the player was previously inactive and is now active again.
        // For example: All users in the voice channel left and now a user joined the voice channel again.
        cancellationToken.ThrowIfCancellationRequested();
        return default; // do nothing
    }

    public async ValueTask NotifyPlayerInactiveAsync(CancellationToken cancellationToken = default)
    {
        // This method is called when the player reached the inactivity deadline.
        // For example: All users in the voice channel left and the player was inactive for longer than 30 seconds.
        cancellationToken.ThrowIfCancellationRequested();

        // Add your custom logic here to handle the event when the player becomes inactive
        // For example, you can stop the player and send a message:
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await _textChannel!.SendMessageAsync("The player has been stopped due to inactivity.").ConfigureAwait(false);
    }

    public ValueTask NotifyPlayerTrackedAsync(CancellationToken cancellationToken = default)
    {
        // This method is called when the player was previously active and is now inactive.
        // For example: A user left the voice channel and now all users left the voice channel.
        cancellationToken.ThrowIfCancellationRequested();
        return default; // do nothing
    }
}

public sealed record class CustomPlayerOptions : QueuedLavalinkPlayerOptions
{
    public ITextChannel? TextChannel { get; init; }

    public CustomPlayerOptions()
    {
        DisconnectOnStop = false;
    }
}
