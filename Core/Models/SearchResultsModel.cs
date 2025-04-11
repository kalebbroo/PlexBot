using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Models;

/// <summary>Aggregates and organizes media search results by type, providing a unified container for presenting rich search functionality to users</summary>
public class SearchResults
{
    /// <summary>The original search text entered by the user, preserved for context in UI displays and for potential follow-up refinement</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Collection of artist matches from the search, representing performers or groups that can be browsed for their albums and tracks</summary>
    public List<Artist> Artists { get; set; } = new();

    /// <summary>Collection of album matches from the search, representing complete music collections that can be played or queued in their entirety</summary>
    public List<Album> Albums { get; set; } = new();

    /// <summary>Collection of individual track matches from the search, representing songs that can be directly played or added to the queue</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>Collection of playlist matches from the search, representing user-curated collections that span across different artists and albums</summary>
    public List<Playlist> Playlists { get; set; } = new();

    /// <summary>Identifies where these results originated from (plex, youtube, spotify, etc.) to help display appropriate context and controls</summary>
    public string SourceSystem { get; set; } = "plex";

    /// <summary>Indicates whether any matching media was found across any category, used to determine whether to show results or an empty state message</summary>
    public bool HasResults => Artists.Count > 0 || Albums.Count > 0 || Tracks.Count > 0 || Playlists.Count > 0;

    /// <summary>The aggregate count of all results across categories, useful for pagination controls and displaying result statistics to users</summary>
    public int TotalResultCount => Artists.Count + Albums.Count + Tracks.Count + Playlists.Count;

    /// <summary>Creates a formatted summary of search results, primarily for logging and debugging to track search effectiveness</summary>
    /// <returns>A concise description of the search query and the counts of each result type</returns>
    public override string ToString()
    {
        return $"Results for '{Query}': {Artists.Count} artists, {Albums.Count} albums, {Tracks.Count} tracks, {Playlists.Count} playlists";
    }

    /// <summary>Combines additional search results with the current results, allowing incremental building of result sets from multiple sources or pages</summary>
    /// <param name="results">The additional results to incorporate into this collection, typically from another search provider or page</param>
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

    /// <summary>Creates a truncated version of these search results with a maximum number of items per category for preview displays</summary>
    /// <param name="maxItemsPerCategory">The maximum number of each media type to include in the limited results</param>
    /// <returns>A new SearchResults instance containing only the top items from each category</returns>
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