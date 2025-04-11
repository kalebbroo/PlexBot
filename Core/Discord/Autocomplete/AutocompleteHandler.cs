using PlexBot.Core.Models.Media;
using PlexBot.Services;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Autocomplete;

/// <summary>Provides autocomplete suggestions for music sources.
/// This handler dynamically generates source options based on the
/// configured music sources in the environment variables.</summary>
public class SourceAutocompleteHandler : AutocompleteHandler
{
    /// <summary>Generates source suggestions based on the configured sources.
    /// Checks environment variables to determine which sources are enabled
    /// and provides them as autocomplete options.</summary>
    /// <param name="context">The interaction context</param>
    /// <param name="autocompleteInteraction">The autocomplete interaction</param>
    /// <param name="parameter">The parameter to provide suggestions for</param>
    /// <param name="service">The service provider</param>
    /// <returns>Autocomplete results containing available sources</returns>
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, 
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        try
        {
            Logs.Debug("Generating source suggestions for autocomplete");
            // Start with Plex which is always available
            List<AutocompleteResult> results =
            [
                new AutocompleteResult("Plex", "plex")
            ];
            results.Add(new AutocompleteResult("YouTube", "youtube"));

            // TODO: Add more sources like Spotify, SoundCloud, etc.

            return Task.FromResult(AutocompletionResult.FromSuccess(results));
        }
        catch (Exception ex)
        {
            Logs.Error($"Error generating source suggestions: {ex.Message}");
            return Task.FromResult(AutocompletionResult.FromError(ex));
        }
    }
}

/// <summary>Provides autocomplete suggestions for Plex playlists.
/// This handler dynamically fetches playlists from the Plex server
/// and formats them for display in the Discord autocomplete dropdown.</summary>
public class PlaylistAutocompleteHandler : AutocompleteHandler
{
    /// <summary>Generates playlist suggestions by fetching available playlists from Plex.
    /// Formats each playlist with its track count for better user experience.</summary>
    /// <param name="context">The interaction context</param>
    /// <param name="autocompleteInteraction">The autocomplete interaction</param>
    /// <param name="parameter">The parameter to provide suggestions for</param>
    /// <param name="service">The service provider</param>
    /// <returns>Autocomplete results containing available playlists</returns>
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        try
        {
            Logs.Debug("Generating playlist suggestions for autocomplete");
            // Get the filter text the user has typed so far
            string currentInput = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;
            // Get the Plex music service
            IPlexMusicService plexMusicService = service.GetRequiredService<IPlexMusicService>();
            // Fetch all playlists
            List<Playlist> playlists = await plexMusicService.GetPlaylistsAsync();
            if (playlists == null || playlists.Count == 0)
            {
                Logs.Warning("No playlists found in Plex library");
                return AutocompletionResult.FromError(
                    InteractionCommandError.ParseFailed,
                    "No playlists available. Check your Plex server connection.");
            }
            Logs.Debug($"Found {playlists.Count} playlists in Plex library");
            // Filter playlists if user has started typing
            if (!string.IsNullOrWhiteSpace(currentInput))
            {
                playlists = playlists
                    .Where(p => p.Title.Contains(currentInput, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                Logs.Debug($"Filtered to {playlists.Count} playlists matching '{currentInput}'");
            }
            // Create autocomplete results (maximum 25 as per Discord's limit)
            List<AutocompleteResult> results = playlists
                .Take(25)
                .Select(playlist => new AutocompleteResult(
                    $"{playlist.Title} ({playlist.TrackCount} tracks)",
                    playlist.SourceKey
                ))
                .ToList();
            return AutocompletionResult.FromSuccess(results);
        }
        catch (Exception ex)
        {
            Logs.Error($"Error generating playlist suggestions: {ex.Message}");
            // Return a friendly error message but still let the dropdown appear
            return AutocompletionResult.FromSuccess([
                new AutocompleteResult("Error loading playlists. Try again later.", "error")
            ]);
        }
    }
}