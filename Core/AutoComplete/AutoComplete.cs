using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using PlexBot.Core.PlexAPI;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PlexBot.Core.AutoComplete
{
    public class AutoComplete : AutocompleteHandler
    {
        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
            IParameterInfo parameter, IServiceProvider services)
        {
            // Ensure this autocomplete is for the correct parameter, for example, "playlist"
            if (parameter.Name.Equals("playlist", StringComparison.OrdinalIgnoreCase))
            {
                PlexApi plexApi = services.GetRequiredService<PlexApi>();
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
            return AutocompletionResult.FromSuccess([]);
        }
    }
}
