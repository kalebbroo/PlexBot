using PlexBot.Core.Models.Media;
using PlexBot.Core.Services;
using PlexBot.Core.Services.Music;
using PlexBot.Core.Services.PlexApi;
using PlexBot.Utils;

namespace PlexBot.Core.Discord.Autocomplete;


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

/// <summary>Unified search mode autocomplete that combines Plex sonic features with
/// dynamically registered extension providers (YouTube, SoundCloud, etc.) into one dropdown</summary>
public class SearchModeAutocompleteHandler : AutocompleteHandler
{
    private static readonly List<(string Name, string Value)> BuiltInModes =
    [
        ("Plex Library", "plex"),
        ("Find by Mood", "mood"),
        ("Find by Genre", "genre"),
        ("Radio Station", "radio")
    ];

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        string input = autocompleteInteraction.Data.Current.Value as string ?? "";
        List<(string Name, string Value)> allModes = [.. BuiltInModes];

        try
        {
            MusicProviderRegistry registry = service.GetRequiredService<MusicProviderRegistry>();
            foreach (IMusicProvider provider in registry.GetAvailableProviders())
            {
                if (provider.Id.Equals("plex", StringComparison.OrdinalIgnoreCase)) continue;
                if (!provider.Capabilities.HasFlag(MusicProviderCapabilities.Search)) continue;
                allModes.Add((provider.DisplayName, provider.Id));
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Could not load extension providers for autocomplete: {ex.Message}");
        }

        IEnumerable<(string Name, string Value)> filtered = allModes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(input))
            filtered = filtered.Where(m => m.Name.Contains(input, StringComparison.OrdinalIgnoreCase)
                || m.Value.Contains(input, StringComparison.OrdinalIgnoreCase));

        List<AutocompleteResult> results = filtered
            .Select(m => new AutocompleteResult(m.Name, m.Value))
            .ToList();

        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

/// <summary>Context-aware query autocomplete that populates suggestions based on the selected search mode.
/// For mood/genre/radio modes, fetches real options from the Plex API. Other modes return no suggestions
/// (user types free-text).</summary>
public class SearchQueryAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider service)
    {
        try
        {
            string currentInput = autocompleteInteraction.Data.Current.Value as string ?? "";

            // Read the mode parameter value from the interaction
            string mode = "";
            foreach (AutocompleteOption option in autocompleteInteraction.Data.Options)
            {
                if (option.Name == "mode" && option.Value is string modeValue)
                {
                    mode = modeValue.ToLowerInvariant();
                    break;
                }
            }

            Logs.Debug($"Query autocomplete: mode='{mode}', input='{currentInput}'");

            if (string.IsNullOrEmpty(mode))
                return AutocompletionResult.FromSuccess([]);

            IPlexSonicService sonicService = service.GetRequiredService<IPlexSonicService>();

            return mode switch
            {
                "mood" => await GetMoodSuggestionsAsync(sonicService, currentInput),
                "genre" => await GetGenreSuggestionsAsync(sonicService, currentInput),
                "radio" => await GetStationSuggestionsAsync(sonicService, currentInput),
                _ => GetSearchHint(currentInput)
            };
        }
        catch (Exception ex)
        {
            Logs.Warning($"Query autocomplete error: {ex.Message}");
            return AutocompletionResult.FromSuccess([]);
        }
    }

    private static async Task<AutocompletionResult> GetMoodSuggestionsAsync(IPlexSonicService sonicService, string input)
    {
        List<MoodTag> moods = await sonicService.GetAvailableMoodsAsync();
        IEnumerable<MoodTag> filtered = moods.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(input))
            filtered = filtered.Where(m => m.Name.Contains(input, StringComparison.OrdinalIgnoreCase));
        else
            filtered = filtered.OrderBy(_ => Random.Shared.Next()); // Randomize when no filter (278 moods, Discord limit 25)

        List<AutocompleteResult> results = filtered
            .Take(25)
            .Select(m => new AutocompleteResult(m.Name, m.Id))
            .ToList();
        return AutocompletionResult.FromSuccess(results);
    }

    private static async Task<AutocompletionResult> GetGenreSuggestionsAsync(IPlexSonicService sonicService, string input)
    {
        List<GenreTag> genres = await sonicService.GetAvailableGenresAsync();
        IEnumerable<GenreTag> filtered = genres.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(input))
            filtered = filtered.Where(g => g.Name.Contains(input, StringComparison.OrdinalIgnoreCase));

        List<AutocompleteResult> results = filtered
            .Take(25)
            .Select(g => new AutocompleteResult(g.Name, g.Id))
            .ToList();
        return AutocompletionResult.FromSuccess(results);
    }

    private static async Task<AutocompletionResult> GetStationSuggestionsAsync(IPlexSonicService sonicService, string input)
    {
        List<RadioStation> stations = await sonicService.GetRadioStationsAsync();
        IEnumerable<RadioStation> filtered = stations.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(input))
            filtered = filtered.Where(s => s.Title.Contains(input, StringComparison.OrdinalIgnoreCase));

        List<AutocompleteResult> results = filtered
            .Take(25)
            .Select(s => new AutocompleteResult(s.Title, s.SourceKey))
            .ToList();
        return AutocompletionResult.FromSuccess(results);
    }

    private static AutocompletionResult GetSearchHint(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return AutocompletionResult.FromSuccess(
            [
                new AutocompleteResult("\u266b Type to search for artists, albums, or tracks", "hint_search")
            ]);
        return AutocompletionResult.FromSuccess([]);
    }
}