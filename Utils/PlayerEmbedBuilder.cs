using PlexBot.Services;

namespace PlexBot.Utils;

/// <summary>Utility class for building Discord embeds for the music player.
/// Creates rich, informative embeds to display track information and playback status
/// in Discord channels, enhancing the user experience with visual feedback.</summary>
public static class PlayerEmbedBuilder
{
    /// <summary>Builds a player embed with track information and image.
    /// Creates a rich Discord embed that displays the current track's details.</summary>
    /// <param name="track">Dictionary containing track information</param>
    /// <param name="imageUrl">URL to the player image (usually an attachment)</param>
    /// <returns>An EmbedBuilder with the configured embed</returns>
    public static EmbedBuilder BuildPlayerEmbed(Dictionary<string, string> track, string imageUrl)
    {
        try
        {
            // Get volume from environment
            string volume = Environment.GetEnvironmentVariable("VOLUME") ?? "100";
            // Build description with track info
            string description = $"{track.GetValueOrDefault("Artist", "Unknown Artist")} - {track.GetValueOrDefault("Title", "Unknown Title")}\n" +
                                $"{track.GetValueOrDefault("Album", "Unknown Album")} - {track.GetValueOrDefault("Studio", "Unknown Studio")}\n\n" +
                                $"Duration: {track.GetValueOrDefault("Duration", "0:00")}";
            // Create the embed
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle("Now Playing")
                .WithDescription(description)
                .WithImageUrl(imageUrl)
                .WithFooter($"Volume: {volume}%")
                .WithColor(Discord.Color.Blue)
                .WithTimestamp(DateTimeOffset.Now);

            return embed;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to build player embed: {ex.Message}");
            // Return a simple fallback embed
            return new EmbedBuilder()
                .WithTitle("Now Playing")
                .WithDescription("An error occurred while building the player display.")
                .WithColor(Discord.Color.Red);
        }
    }

    /// <summary>Builds a queue embed showing the current queue state.
    /// Creates a paginated embed display of the track queue.</summary>
    /// <param name="queue">List of tracks in the queue</param>
    /// <param name="currentTrack">The currently playing track</param>
    /// <param name="currentPage">The current page to display</param>
    /// <param name="itemsPerPage">Number of items to show per page</param>
    /// <returns>An EmbedBuilder with the configured embed</returns>
    public static EmbedBuilder BuildQueueEmbed(
        IReadOnlyList<CustomTrackQueueItem> queue,
        CustomTrackQueueItem? currentTrack,
        int currentPage,
        int itemsPerPage = 10)
    {
        try
        {
            // Calculate pagination info
            int totalTracks = queue.Count;
            int totalPages = (totalTracks + itemsPerPage - 1) / itemsPerPage;
            currentPage = Math.Clamp(currentPage, 1, Math.Max(1, totalPages));
            // Create the base embed
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle("Current Music Queue")
                .WithColor(Discord.Color.Blue)
                .WithFooter($"Page {currentPage} of {totalPages} ({totalTracks} Queued Tracks)")
                .WithTimestamp(DateTimeOffset.Now);
            // Add the currently playing track (only on first page)
            if (currentPage == 1 && currentTrack != null)
            {
                embed.AddField(
                    "Now Playing: " + currentTrack.Title,
                    $"Artist: {currentTrack.Artist}\nAlbum: {currentTrack.Album}\nDuration: {currentTrack.Duration}",
                    inline: false
                );
            }
            // Add queue items for the current page
            int startIndex = (currentPage - 1) * itemsPerPage;
            int endIndex = Math.Min(startIndex + itemsPerPage, totalTracks);
            for (int i = startIndex; i < endIndex; i++)
            {
                var item = queue[i];
                embed.AddField(
                    $"#{i + 1}: {item.Title}",
                    $"Artist: {item.Artist}\nAlbum: {item.Album}\nDuration: {item.Duration}",
                    inline: true
                );
            }
            // If queue is empty, show a message
            if (totalTracks == 0 && currentTrack == null)
            {
                embed.WithDescription("The queue is currently empty.");
            }
            return embed;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to build queue embed: {ex.Message}");
            // Return a simple fallback embed
            return new EmbedBuilder()
                .WithTitle("Queue")
                .WithDescription("An error occurred while building the queue display.")
                .WithColor(Discord.Color.Red);
        }
    }
}