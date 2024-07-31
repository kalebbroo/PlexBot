namespace PlexBot.Core.AutoComplete;

public class AutoComplete : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        if (parameter.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
        {
            PlexMusic plexApi = services.GetRequiredService<PlexMusic>();
            Dictionary<string, Dictionary<string, string>> playlists = await plexApi.GetPlaylists();
            if (playlists != null && playlists.Count != 0)
            {
                List<AutocompleteResult> results = playlists.Keys
                    .Select(key => {
                        Dictionary<string, string> playlist = playlists[key];
                        string name = playlist["Title"];
                        string trackCount = playlist["TrackCount"];
                        string url = playlist["Url"];
                        return new AutocompleteResult($"{name} ({trackCount} tracks)", url);
                    })
                    .Take(25)
                    .ToList();
                return AutocompletionResult.FromSuccess(results);
            }
        }
        if (parameter.Name.Equals("source", StringComparison.OrdinalIgnoreCase))
        {
            List<AutocompleteResult> results = [new("Plex", "plex")];
            if (bool.TryParse(Environment.GetEnvironmentVariable("LAVALINK_SERVER_SOURCES_YOUTUBE"), out bool enableYouTube) && enableYouTube)
                results.Add(new AutocompleteResult("YouTube", "youtube"));
            if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_SOUNDCLOUD"), out bool enableSoundCloud) && enableSoundCloud)
                results.Add(new AutocompleteResult("SoundCloud", "soundcloud"));
            if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_TWITCH"), out bool enableTwitch) && enableTwitch)
                results.Add(new AutocompleteResult("Twitch", "twitch"));
            if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_VIMEO"), out bool enableVimeo) && enableVimeo)
                results.Add(new AutocompleteResult("Vimeo", "vimeo"));
            if (bool.TryParse(Environment.GetEnvironmentVariable("ENABLE_BANDCAMP"), out bool enableBandcamp) && enableBandcamp)
                results.Add(new AutocompleteResult("Bandcamp", "bandcamp"));
            return AutocompletionResult.FromSuccess(results);
        }
        return AutocompletionResult.FromSuccess([]);
    }
}
