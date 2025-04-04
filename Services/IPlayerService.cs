using PlexBot.Core.Models.Media;

namespace PlexBot.Services;

/// <summary>
/// Defines the contract for services that manage audio players.
/// This interface abstracts the playback functionality, handling the creation
/// and control of audio players for Discord voice channels while hiding the
/// underlying audio streaming implementation details.
/// </summary>
public interface IPlayerService
{
    /// <summary>
    /// Gets or creates a player for a specific guild and interaction.
    /// This method handles the logic of retrieving an existing player if one
    /// exists for the guild, or creating a new one if needed. It also manages
    /// connecting to voice channels as needed based on the specified behavior.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="connectToVoiceChannel">
    /// Whether to automatically connect to the user's voice channel when creating a player
    /// </param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>
    /// A player instance if created successfully; otherwise, null if the
    /// operation failed (e.g., user not in a voice channel)
    /// </returns>
    /// <exception cref="PlayerException">Thrown when player creation or retrieval fails</exception>
    Task<QueuedLavalinkPlayer?> GetPlayerAsync(
        IDiscordInteraction interaction,
        bool connectToVoiceChannel = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Plays a single track in the voice channel.
    /// Handles all aspects of playing a track, including retrieving the track from its source,
    /// converting it to a format suitable for playback, and starting playback with appropriate
    /// settings. This method will either play the track immediately or add it to the queue
    /// based on the player's current state.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="track">The track to play</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="PlayerException">Thrown when playback fails</exception>
    Task PlayTrackAsync(
        IDiscordInteraction interaction,
        Track track,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds multiple tracks to the player's queue.
    /// Processes a collection of tracks and adds them to the queue in the specified order.
    /// This is useful for adding entire albums, playlists, or artist discographies to the queue.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="tracks">The collection of tracks to add to the queue</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="PlayerException">Thrown when the operation fails</exception>
    Task AddToQueueAsync(
        IDiscordInteraction interaction,
        IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the playback state between paused and playing.
    /// Provides a convenient way to pause or resume playback with a single call,
    /// automatically determining the appropriate action based on the current state.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>
    /// A string describing the new state ("Paused" or "Resumed") that can be
    /// displayed to the user
    /// </returns>
    /// <exception cref="PlayerException">Thrown when the operation fails</exception>
    Task<string> TogglePauseResumeAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Skips the currently playing track.
    /// Stops the current track and begins playing the next track in the queue
    /// if one exists. If repeat mode is set to track, this method overrides that
    /// setting to advance to the next track.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="PlayerException">Thrown when the operation fails</exception>
    Task SkipTrackAsync(
        IDiscordInteraction interaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the repeat mode for the player.
    /// Configures how the player should handle the end of the current track or queue,
    /// with options to repeat a single track, the entire queue, or not repeat at all.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="repeatMode">The repeat mode to set</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="PlayerException">Thrown when the operation fails</exception>
    Task SetRepeatModeAsync(
        IDiscordInteraction interaction,
        TrackRepeatMode repeatMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops playback and clears the queue.
    /// Immediately stops the current track, clears any queued tracks, and optionally
    /// disconnects from the voice channel based on the provided parameter.
    /// </summary>
    /// <param name="interaction">The Discord interaction that triggered this request</param>
    /// <param name="disconnect">Whether to disconnect from the voice channel after stopping</param>
    /// <param name="cancellationToken">Optional token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="PlayerException">Thrown when the operation fails</exception>
    Task StopAsync(
        IDiscordInteraction interaction,
        bool disconnect = false,
        CancellationToken cancellationToken = default);
}