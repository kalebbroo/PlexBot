using PlexBot.Core.Models.Media;
using PlexBot.Core.Services;
using PlexBot.Core.Services.Music;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Autocomplete;

/// <summary>Provides autocomplete suggestions for music sources.
/// Dynamically lists all available providers from MusicProviderRegistry.</summary>
public class SourceAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        try
        {
            MusicProviderRegistry registry = service.GetRequiredService<MusicProviderRegistry>();
            List<AutocompleteResult> results = registry.GetAvailableProviders()
                .Select(p => new AutocompleteResult(p.DisplayName, p.Id))
                .ToList();

            return Task.FromResult(AutocompletionResult.FromSuccess(results));
        }
        catch (Exception ex)
        {
            Logs.Error($"Error generating source suggestions: {ex.Message}");
            return Task.FromResult(AutocompletionResult.FromError(ex));
        }
    }
}

/// <summary>Provides autocomplete suggestions for playlists from all sources.
/// Fetches Plex playlists and custom user playlists (if extension loaded),
/// merging them into a single autocomplete dropdown.</summary>
public class PlaylistAutocompleteHandler : AutocompleteHandler
{
    /// <summary>Generates playlist suggestions from Plex and custom playlists.
    /// Custom playlists are prefixed with [Custom] for visual distinction.</summary>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        try
        {
            Logs.Debug("Generating playlist suggestions for autocomplete");
            string currentInput = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;

            List<AutocompleteResult> results = [];

            // Fetch Plex playlists
            try
            {
                IPlexMusicService plexMusicService = service.GetRequiredService<IPlexMusicService>();
                List<Playlist> plexPlaylists = await plexMusicService.GetPlaylistsAsync();
                if (plexPlaylists?.Count > 0)
                {
                    IEnumerable<Playlist> filtered = plexPlaylists.AsEnumerable();
                    if (!string.IsNullOrWhiteSpace(currentInput))
                        filtered = filtered.Where(p => p.Title.Contains(currentInput, StringComparison.OrdinalIgnoreCase));

                    results.AddRange(filtered.Take(20).Select(p =>
                        new AutocompleteResult($"{p.Title} ({p.TrackCount} tracks)", p.SourceKey)));
                }
            }
            catch (Exception ex)
            {
                Logs.Warning($"Failed to fetch Plex playlists for autocomplete: {ex.Message}");
            }

            // Fetch custom playlists from the playlists extension (if loaded)
            try
            {
                MusicProviderRegistry registry = service.GetRequiredService<MusicProviderRegistry>();
                IMusicProvider? playlistProvider = registry.GetProvider("playlists");
                if (playlistProvider is not null && playlistProvider.IsAvailable)
                {
                    List<Playlist> customPlaylists = await playlistProvider.GetPlaylistsAsync();
                    if (customPlaylists.Count > 0)
                    {
                        IEnumerable<Playlist> filtered = customPlaylists.AsEnumerable();
                        if (!string.IsNullOrWhiteSpace(currentInput))
                            filtered = filtered.Where(p => p.Title.Contains(currentInput, StringComparison.OrdinalIgnoreCase));

                        results.AddRange(filtered.Take(10).Select(p =>
                            new AutocompleteResult($"[Custom] {p.Title} ({p.TrackCount} tracks)", p.SourceKey)));
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"Custom playlists not available for autocomplete: {ex.Message}");
            }

            if (results.Count == 0)
            {
                Logs.Debug("No playlists found from any source");
                return AutocompletionResult.FromSuccess([]);
            }

            return AutocompletionResult.FromSuccess(results.Take(25));
        }
        catch (Exception ex)
        {
            Logs.Error($"Error generating playlist suggestions: {ex.Message}");
            return AutocompletionResult.FromSuccess([]);
        }
    }
}