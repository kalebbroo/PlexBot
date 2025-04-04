using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Models;

/// <summary>
/// Represents the combined search results across different media types.
/// This model aggregates results from searches that may return multiple 
/// types of content (artists, albums, tracks, playlists) in a single response.
/// It serves as a container for presenting organized search results to users.
/// </summary>
public class SearchResults
{
    /// <summary>
    /// Gets or sets the original search query that produced these results.
    /// Stored for context and potential refinement of results.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artists found in the search.
    /// Populated when search results include artist entities.
    /// </summary>
    public List<Artist> Artists { get; set; } = new();

    /// <summary>
    /// Gets or sets the albums found in the search.
    /// Populated when search results include album entities.
    /// </summary>
    public List<Album> Albums { get; set; } = new();

    /// <summary>
    /// Gets or sets the tracks found in the search.
    /// Populated when search results include track entities.
    /// </summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>
    /// Gets or sets the playlists found in the search.
    /// Populated when search results include playlist entities.
    /// </summary>
    public List<Playlist> Playlists { get; set; } = new();

    /// <summary>
    /// Gets the source system that these results came from.
    /// If results are mixed from multiple sources, this will be "mixed".
    /// </summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>
    /// Gets a value indicating whether any results were found.
    /// Used to determine whether to display "no results" messaging.
    /// </summary>
    public bool HasResults => Artists.Count > 0 || Albums.Count > 0 || Tracks.Count > 0 || Playlists.Count > 0;

    /// <summary>
    /// Gets the total number of results across all categories.
    /// Used for pagination and results count display.
    /// </summary>
    public int TotalResultCount => Artists.Count + Albums.Count + Tracks.Count + Playlists.Count;

    /// <summary>
    /// Creates a human-readable representation of the search results primarily for debugging and logging.
    /// Includes the essential counts of different result types.
    /// </summary>
    /// <returns>A string containing the query and result counts</returns>
    public override string ToString()
    {
        return $"Results for '{Query}': {Artists.Count} artists, {Albums.Count} albums, {Tracks.Count} tracks, {Playlists.Count} playlists";
    }

    /// <summary>
    /// Merges additional search results into this instance.
    /// Used when combining results from multiple sources or paginated results.
    /// Does not check for duplicates - that should be handled by the caller if needed.
    /// </summary>
    /// <param name="results">The search results to merge</param>
    public void Merge(SearchResults results)
    {
        Artists.AddRange(results.Artists);
        Albums.AddRange(results.Albums);
        Tracks.AddRange(results.Tracks);
        Playlists.AddRange(results.Playlists);

        if (SourceSystem != results.SourceSystem)
        {
            SourceSystem = "mixed";
        }
    }

    /// <summary>
    /// Creates a new SearchResults instance with limited items per category.
    /// Useful for preview displays where you only want the top few results of each type.
    /// </summary>
    /// <param name="maxItemsPerCategory">Maximum number of items to include in each category</param>
    /// <returns>A new SearchResults instance with limited items</returns>
    public SearchResults GetLimited(int maxItemsPerCategory)
    {
        return new SearchResults
        {
            Query = Query,
            Artists = Artists.Take(maxItemsPerCategory).ToList(),
            Albums = Albums.Take(maxItemsPerCategory).ToList(),
            Tracks = Tracks.Take(maxItemsPerCategory).ToList(),
            Playlists = Playlists.Take(maxItemsPerCategory).ToList(),
            SourceSystem = SourceSystem
        };
    }
}