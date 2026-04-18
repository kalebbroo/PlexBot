using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services;

/// <summary>Defines the contract for Plex Sonic features including radio stations, mood/genre browsing,
/// sonically similar tracks, and sonic adventure paths</summary>
public interface IPlexSonicService
{
    /// <summary>Auto-discovers and returns the music library section ID from the Plex server</summary>
    Task<string> GetMusicSectionIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets available mood tags for filtering tracks</summary>
    Task<List<MoodTag>> GetAvailableMoodsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets available genre tags for filtering tracks</summary>
    Task<List<GenreTag>> GetAvailableGenresAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets tracks matching a mood filter</summary>
    Task<List<Track>> GetMoodTracksAsync(string moodId, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets tracks matching a genre filter</summary>
    Task<List<Track>> GetGenreTracksAsync(string genreId, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets sonically similar tracks to a given track by its rating key</summary>
    Task<List<Track>> GetSimilarTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets a Sonic Adventure path between two tracks, traveling through sonic space</summary>
    Task<List<Track>> GetSonicAdventureAsync(string startRatingKey, string endRatingKey, CancellationToken cancellationToken = default);

    /// <summary>Gets radio station tracks seeded from a specific item (track/artist/album) by its rating key</summary>
    Task<List<Track>> GetRadioTracksAsync(string ratingKey, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Gets hub/station listings for the music section</summary>
    Task<List<RadioStation>> GetRadioStationsAsync(CancellationToken cancellationToken = default);
}
