using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services.Music;

/// <summary>Plex music provider that wraps IPlexMusicService behind the generic IMusicProvider interface.
/// This is a thin facade — IPlexMusicService remains unchanged and available for Plex-specific code.</summary>
public class PlexMusicProvider(IPlexMusicService plexMusicService) : IMusicProvider
{
    public string Id => "plex";
    public string DisplayName => "Plex";
    public bool IsAvailable => true;
    public int Priority => 0;
    public MusicProviderCapabilities Capabilities =>
        MusicProviderCapabilities.Search | MusicProviderCapabilities.TrackDetails |
        MusicProviderCapabilities.Albums | MusicProviderCapabilities.Playlists |
        MusicProviderCapabilities.ArtistBrowse;

    public Task<SearchResults> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        plexMusicService.SearchLibraryAsync(query, cancellationToken);

    public Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default) =>
        plexMusicService.GetTrackDetailsAsync(trackKey, cancellationToken);

    public Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default) =>
        plexMusicService.GetTracksAsync(containerKey, cancellationToken);

    public Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default) =>
        plexMusicService.GetAlbumsAsync(artistKey, cancellationToken);

    public Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken cancellationToken = default) =>
        plexMusicService.GetAllArtistTracksAsync(artistKey, cancellationToken);

    public Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default) =>
        plexMusicService.GetPlaylistsAsync(cancellationToken);

    public async Task<Playlist?> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default) =>
        await plexMusicService.GetPlaylistDetailsAsync(playlistKey, cancellationToken);
}
