using Discord.Interactions;
using Discord;
using Newtonsoft.Json;

namespace PlexBot.Core.AutoComplete
{
    public class AutoConfig() : AutocompleteHandler
    {

        /// <summary>Provides autocomplete suggestions for template names by querying the list of templates from the Supabase database.</summary>
        public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, 
            IParameterInfo parameter, IServiceProvider services)
        {
            try
            {
                // get the config json file that is in the root of the project called config.json
                string config = File.ReadAllText("config.json");
                //  parse the json file into a dictionary
                var templates = JsonConvert.DeserializeObject<Dictionary<string, string>>(config);
                if (templates != null)
                {
                    var results = templates.Keys
                        .Select(name => new AutocompleteResult(name, name))
                        .Take(25)
                        .ToList();

                    return AutocompletionResult.FromSuccess(results);
                }

                return AutocompletionResult.FromSuccess([]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in autocomplete handler: {ex.Message}");
                return AutocompletionResult.FromSuccess([]);
            }
        }
    }
}
