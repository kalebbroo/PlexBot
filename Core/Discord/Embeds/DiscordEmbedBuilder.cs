using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;
using System;
using System.Collections.Generic;

using Color = Discord.Color;

namespace PlexBot.Core.Discord.Embeds
{
    /// <summary>Utility for creating standardized, visually appealing Discord embeds across the application</summary>
    public static class DiscordEmbedBuilder
    {
        // Standard colors for different types of embeds
        private static readonly Color SuccessColor = new(0, 255, 127);    // Green
        private static readonly Color ErrorColor = new(255, 69, 0);       // Red-Orange
        private static readonly Color InfoColor = new(30, 144, 255);      // Dodger Blue
        private static readonly Color WarningColor = new(255, 215, 0);    // Gold
        private static readonly Color MusicColor = new(138, 43, 226);     // Purple

        // Emojis for different types of messages
        private const string SuccessEmoji = "✅";
        private const string ErrorEmoji = "❌";
        private const string InfoEmoji = "ℹ️";
        private const string WarningEmoji = "⚠️";
        private const string MusicEmoji = "🎵";
        private const string SearchEmoji = "🔍";
        private const string QueueEmoji = "📋";
        private const string PlaylistEmoji = "📂";
        private const string VolumeEmoji = "🔊";
        private const string PauseEmoji = "⏸️";
        private const string PlayEmoji = "▶️";
        private const string SkipEmoji = "⏭️";
        private const string StopEmoji = "⏹️";

        /// <summary>Creates a success embed with standardized formatting</summary>
        /// <param name="title">The title of the embed</param>
        /// <param name="description">The main message content</param>
        /// <param name="includeTimestamp">Whether to include the current time</param>
        /// <returns>A configured embed builder</returns>
        public static Embed Success(string title, string description, bool includeTimestamp = true)
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle($"{SuccessEmoji} {title}")
                .WithDescription(description)
                .WithColor(SuccessColor);
            if (includeTimestamp)
                embed.WithCurrentTimestamp();
            return embed.Build();
        }

        /// <summary>Creates an error embed with standardized formatting</summary>
        /// <param name="title">The title of the embed</param>
        /// <param name="description">The error message</param>
        /// <param name="includeTimestamp">Whether to include the current time</param>
        /// <returns>A configured embed builder</returns>
        public static Embed Error(string title, string description, bool includeTimestamp = true)
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle($"{ErrorEmoji} {title}")
                .WithDescription(description)
                .WithColor(ErrorColor);
            if (includeTimestamp)
                embed.WithCurrentTimestamp();
            return embed.Build();
        }

        /// <summary>Creates an info embed with standardized formatting</summary>
        /// <param name="title">The title of the embed</param>
        /// <param name="description">The informational message</param>
        /// <param name="includeTimestamp">Whether to include the current time</param>
        /// <returns>A configured embed builder</returns>
        public static Embed Info(string title, string description, bool includeTimestamp = true)
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle($"{InfoEmoji} {title}")
                .WithDescription(description)
                .WithColor(InfoColor);
            if (includeTimestamp)
                embed.WithCurrentTimestamp();
            return embed.Build();
        }

        /// <summary>Creates a warning embed with standardized formatting</summary>
        /// <param name="title">The title of the embed</param>
        /// <param name="description">The warning message</param>
        /// <param name="includeTimestamp">Whether to include the current time</param>
        /// <returns>A configured embed builder</returns>
        public static Embed Warning(string title, string description, bool includeTimestamp = true)
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle($"{WarningEmoji} {title}")
                .WithDescription(description)
                .WithColor(WarningColor);
            if (includeTimestamp)
                embed.WithCurrentTimestamp();
            return embed.Build();
        }

        /// <summary>Creates a search results embed with standardized formatting</summary>
        /// <param name="query">The search query</param>
        /// <param name="results">The search results description</param>
        /// <param name="thumbnailUrl">Optional URL for a thumbnail image</param>
        /// <returns>A configured embed builder</returns>
        public static Embed SearchResults(string query, string results, string? thumbnailUrl = null)
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithTitle($"{SearchEmoji} Search Results for: {query}")
                .WithDescription(results)
                .WithColor(InfoColor)
                .WithCurrentTimestamp();
            if (!string.IsNullOrEmpty(thumbnailUrl))
                embed.WithThumbnailUrl(thumbnailUrl);
            return embed.Build();
        }

        /// <summary>Creates a command error embed for interaction failures</summary>
        /// <param name="errorType">The type of error that occurred</param>
        /// <param name="errorReason">The reason for the error</param>
        /// <returns>A configured embed builder</returns>
        public static Embed CommandError(InteractionCommandError? errorType, string errorReason)
        {
            string title;
            string description;
            if (errorType.HasValue)
            {
                switch (errorType.Value)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        title = "Permission Denied";
                        description = $"You don't have permission to use this command: {errorReason}";
                        break;
                    case InteractionCommandError.UnknownCommand:
                        title = "Unknown Command";
                        description = "This command is not recognized. It may have been removed or updated.";
                        break;
                    case InteractionCommandError.BadArgs:
                        title = "Invalid Arguments";
                        description = "The command arguments were invalid. Please check your input and try again.";
                        break;
                    case InteractionCommandError.Exception:
                        title = "Command Error";
                        description = "An error occurred while processing your command. Please try again later.";
                        Logs.Error($"Command exception: {errorReason}");
                        break;
                    default:
                        title = "Unknown Error";
                        description = "An unknown error occurred. Please try again later.";
                        break;
                }
            }
            else
            {
                title = "Unknown Error";
                description = "An unknown error occurred. Please try again later.";
            }
            return Error(title, description);
        }

        /// <summary>Builds a player embed with track information and image.
        /// Creates a rich Discord embed that displays the current track's details.</summary>
        /// <param name="track">Dictionary containing track information</param>
        /// <param name="imageUrl">URL to the player image (usually an attachment)</param>
        /// <returns>An EmbedBuilder with the configured embed</returns>
        public static EmbedBuilder BuildPlayerEmbed(CustomTrackQueueItem track, string imageUrl)
        {
            try
            {
                // Get volume from environment
                string volume = Environment.GetEnvironmentVariable("VOLUME") ?? "100";
                // Build description with track info
                string description = $"{track.Artist ?? "Unknown Artist"} - {track.Title ?? "Unknown Title"}\n" +
                                    $"{track.Album ?? "Unknown Album"} - {track.Studio ?? "Unknown Studio"}\n\n" +
                                    $"Duration: {track.Duration ?? "0:00"}";
                // Create the embed
                EmbedBuilder embed = new EmbedBuilder()
                    .WithTitle($"{PlayEmoji} Now Playing")
                    .WithDescription(description)
                    .WithImageUrl(imageUrl)
                    .WithFooter($"{VolumeEmoji} Volume: {volume}%")
                    .WithColor(MusicColor)
                    .WithCurrentTimestamp();
                return embed;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to build player embed: {ex.Message}");
                // Return a simple fallback embed
                return new EmbedBuilder()
                    .WithTitle($"{ErrorEmoji} Now Playing")
                    .WithDescription("An error occurred while building the player display.")
                    .WithColor(ErrorColor);
            }
        }

        /// <summary>Builds a queue embed showing the current queue state.
        /// Creates a paginated embed display of the track queue.</summary>
        /// <param name="queue">List of tracks in the queue</param>
        /// <param name="currentTrack">The currently playing track</param>
        /// <param name="currentPage">The current page to display</param>
        /// <param name="itemsPerPage">Number of items to show per page</param>
        /// <returns>An EmbedBuilder with the configured embed</returns>
        public static EmbedBuilder BuildQueueEmbed(IReadOnlyList<CustomTrackQueueItem> queue, CustomTrackQueueItem? currentTrack,
            int currentPage, int itemsPerPage = 10)
        {
            try
            {
                // Calculate pagination info
                int totalTracks = queue.Count;
                int totalPages = (totalTracks + itemsPerPage - 1) / itemsPerPage;
                currentPage = Math.Clamp(currentPage, 1, Math.Max(1, totalPages));
                // Create the base embed
                EmbedBuilder embed = new EmbedBuilder()
                    .WithTitle($"{QueueEmoji} Current Music Queue")
                    .WithColor(MusicColor)
                    .WithFooter($"Page {currentPage} of {totalPages} ({totalTracks} Queued Tracks)")
                    .WithCurrentTimestamp();
                // Add the currently playing track (only on first page)
                if (currentPage == 1 && currentTrack != null)
                {
                    embed.AddField(
                        $"{PlayEmoji} Now Playing: {currentTrack.Title}",
                        $"Artist: {currentTrack.Artist}\nAlbum: {currentTrack.Album}\nDuration: {currentTrack.Duration}",
                        inline: false
                    );
                }
                // Add queue items for the current page
                int startIndex = (currentPage - 1) * itemsPerPage;
                int endIndex = Math.Min(startIndex + itemsPerPage, totalTracks);
                for (int i = startIndex; i < endIndex; i++)
                {
                    CustomTrackQueueItem item = queue[i];
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
                    .WithTitle($"{ErrorEmoji} Queue")
                    .WithDescription("An error occurred while building the queue display.")
                    .WithColor(ErrorColor);
            }
        }
    }
}
