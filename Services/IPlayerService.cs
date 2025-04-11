using PlexBot.Core.Models.Media;

namespace PlexBot.Services;

/// <summary>Defines an interface for audio playback that abstracts the complexities of Discord voice integration and audio streaming</summary>
public interface IPlayerService
{
    /// <summary>Retrieves or creates a player for a Discord guild, managing connection to voice channels based on context and user location</summary>
    /// <param name="interaction">The Discord interaction containing user and guild context, providing voice channel information</param>
    /// <param name="connectToVoiceChannel">Whether to automatically join the user's current voice channel, useful for immediate playback commands</param>
    /// <param name="cancellationToken">Optional token to cancel the operation if the request times out or is abandoned</param>
    /// <returns>A configured player ready for audio operations, or null if the user isn't in a voice channel when required</returns>
    /// <exception cref="PlayerException">Thrown when voice connection fails or the player cannot be initialized properly</exception>
    Task<QueuedLavalinkPlayer?> GetPlayerAsync(IDiscordInteraction interaction, bool connectToVoiceChannel = true,
        CancellationToken cancellationToken = default);

    /// <summary>Initiates playback of a single track, handling all aspects from source retrieval to streaming setup</summary>
    /// <param name="interaction">The Discord interaction providing guild and channel context for the playback</param>
    /// <param name="track">The track to play, containing all necessary metadata and source information</param>
    /// <param name="cancellationToken">Optional token to cancel long-running track preparation operations</param>
    /// <returns>A task that completes when the track has been sent to the player (not when playback finishes)</returns>
    /// <exception cref="PlayerException">Thrown when the track cannot be played due to format issues, connection problems, or permissions</exception>
    Task PlayTrackAsync(IDiscordInteraction interaction, Track track, CancellationToken cancellationToken = default);

    /// <summary>Adds multiple tracks to the playback queue, enabling batch operations for albums, playlists, and search results</summary>
    /// <param name="interaction">The Discord interaction providing context for queue management</param>
    /// <param name="tracks">The collection of tracks to append to the current queue</param>
    /// <param name="cancellationToken">Optional token to cancel the operation if it takes too long</param>
    /// <returns>A task that completes when all tracks have been processed and added to the queue</returns>
    /// <exception cref="PlayerException">Thrown when the tracks cannot be added to the queue due to format or connection issues</exception>
    Task AddToQueueAsync(IDiscordInteraction interaction, IEnumerable<Track> tracks, CancellationToken cancellationToken = default);

    /// <summary>Toggles between paused and playing states, serving as a convenience method for the most common playback control action</summary>
    /// <param name="interaction">The Discord interaction containing guild context to identify the correct player</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A user-friendly status message indicating the new state ("Paused" or "Resumed") for display in Discord</returns>
    /// <exception cref="PlayerException">Thrown when the player cannot toggle state due to connection issues or invalid player state</exception>
    Task<string> TogglePauseResumeAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default);

    /// <summary>Advances playback to the next track in queue, overriding any active repeat settings to force progression</summary>
    /// <param name="interaction">The Discord interaction containing guild context to identify the correct player</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task that completes when the current track has been stopped and the next track has started (if available)</returns>
    /// <exception cref="PlayerException">Thrown when skipping fails due to player state issues or connection problems</exception>
    Task SkipTrackAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default);

    /// <summary>Configures how playback should continue when tracks end, supporting single-track loops, queue loops, or no repetition</summary>
    /// <param name="interaction">The Discord interaction containing guild context to identify the correct player</param>
    /// <param name="repeatMode">The desired repetition behavior that should be applied to current and future tracks</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task that completes when the repeat mode has been updated in the player</returns>
    /// <exception cref="PlayerException">Thrown when the operation fails due to player state or connection issues</exception>
    Task SetRepeatModeAsync(
        IDiscordInteraction interaction,
        TrackRepeatMode repeatMode,
        CancellationToken cancellationToken = default);

    /// <summary>Terminates all playback activity, clearing the queue and optionally disconnecting from voice to free up resources</summary>
    /// <param name="interaction">The Discord interaction containing guild context to identify the correct player</param>
    /// <param name="disconnect">Whether to fully disconnect from the voice channel after stopping, or remain connected for future commands</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task that completes when playback has been stopped and the queue cleared</returns>
    /// <exception cref="PlayerException">Thrown when the stop operation fails due to connection issues</exception>
    Task StopAsync(
        IDiscordInteraction interaction,
        bool disconnect = false,
        CancellationToken cancellationToken = default);
}