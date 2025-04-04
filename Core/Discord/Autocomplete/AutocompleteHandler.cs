using PlexBot.Utils;

namespace PlexBot.Core.Discord.Autocomplete;

/// <summary>
/// Provides autocomplete suggestions for music sources.
/// This handler dynamically generates source options based on the
/// configured music sources in the environment variables.
/// </summary>
public class SourceAutocompleteHandler : AutocompleteHandler
{
    /// <summary>
    /// Generates source suggestions based on the configured sources.
    /// Checks environment variables to determine which sources are enabled
    /// and provides them as autocomplete options.
    /// </summary>
    /// <param name="context">The interaction context</param>
    /// <param name="autocompleteInteraction">The autocomplete interaction</param>
    /// <param name="parameter">The parameter to provide suggestions for</param>
    /// <param name="service">The service provider</param>
    /// <returns>Autocomplete results containing available sources</returns>
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider service)
    {
        try
        {
            Logs.Debug("Generating source suggestions for autocomplete");

            // Start with Plex which is always available
            List<AutocompleteResult> results = new()
            {
                new AutocompleteResult("Plex", "plex")
            };

            // Check for YouTube
            if (EnvConfig.GetBool("LAVALINK_SERVER_SOURCES_YOUTUBE", false) ||
                EnvConfig.GetBool("ENABLE_YOUTUBE", false))
            {
                results.Add(new AutocompleteResult("YouTube", "youtube"));
            }

            // Check for SoundCloud
            if (EnvConfig.GetBool("LAVALINK_SERVER_SOURCES_SOUNDCLOUD", false) ||
                EnvConfig.GetBool("ENABLE_SOUNDCLOUD", false))
            {
                results.Add(new AutocompleteResult("SoundCloud", "soundcloud"));
            }

            // Check for other sources
            if (EnvConfig.GetBool("ENABLE_TWITCH", false))
            {
                results.Add(new AutocompleteResult("Twitch", "twitch"));
            }

            if (EnvConfig.GetBool("ENABLE_VIMEO", false))
            {
                results.Add(new AutocompleteResult("Vimeo", "vimeo"));
            }

            if (EnvConfig.GetBool("ENABLE_BANDCAMP", false))
            {
                results.Add(new AutocompleteResult("Bandcamp", "bandcamp"));
            }

            return Task.FromResult(AutocompletionResult.FromSuccess(results));
        }
        catch (Exception ex)
        {
            Logs.Error($"Error generating source suggestions: {ex.Message}");
            return Task.FromResult(AutocompletionResult.FromError(ex));
        }
    }
}

public class PlaylistAutocompleteHandler : AutocompleteHandler
{
    /// <summary>Generates playlist suggestions based on the configured sources.
    /// This is a placeholder implementation and should be overridden in derived classes.</summary>
    /// <param name="context">The interaction context</param>
    /// <param name="autocompleteInteraction">The autocomplete interaction</param>
    /// <param name="parameter">The parameter to provide suggestions for</param>
    /// <param name="service">The service provider</param>
    /// <returns>Autocomplete results containing available playlists</returns>
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider service)
    {
        return Task.FromResult(AutocompletionResult.FromError(InteractionCommandError.UnknownCommand, "Playlist autocomplete is not implemented yet."));
    }
}