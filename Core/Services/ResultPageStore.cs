using System.Collections.Concurrent;
using PlexBot.Core.Models.Media;

namespace PlexBot.Core.Services;

/// <summary>Holds a result list between a message and its Previous / Next buttons, so paging doesn't run the search
/// again. Entries expire after <paramref name="lifetime"/>; an expired list asks the user to run the command again.</summary>
/// <param name="time">Clock for expiry, replaceable in tests</param>
/// <param name="lifetime">How long a list stays available for paging</param>
public sealed class ResultPageStore<T>(TimeProvider time, TimeSpan lifetime)
{
    private readonly ConcurrentDictionary<string, (T Value, DateTimeOffset Stored)> _items = new(StringComparer.Ordinal);

    /// <summary>Stores a result list and returns the id to put in the button custom ids</summary>
    public string Save(T value)
    {
        Prune();
        string id = Guid.NewGuid().ToString("N")[..10];
        _items[id] = (value, time.GetUtcNow());
        return id;
    }

    /// <summary>The stored list, or default when the id is unknown or expired</summary>
    public T? Get(string id)
    {
        Prune();
        return _items.TryGetValue(id, out (T Value, DateTimeOffset Stored) entry) ? entry.Value : default;
    }

    private void Prune()
    {
        DateTimeOffset cutoff = time.GetUtcNow() - lifetime;
        foreach (KeyValuePair<string, (T Value, DateTimeOffset Stored)> item in _items)
        {
            if (item.Value.Stored < cutoff)
                _items.TryRemove(item.Key, out _);
        }
    }
}

/// <summary>Paging arithmetic for select menus, which show at most 25 options</summary>
public static class ResultPaging
{
    /// <summary>Discord's select menu limit</summary>
    public const int PageSize = 25;

    /// <summary>Number of pages for a list, at least one</summary>
    public static int PageCount(int total) => Math.Max(1, (total + PageSize - 1) / PageSize);

    /// <summary>The requested page, clamped to the pages that exist</summary>
    public static int ClampPage(int page, int total) => Math.Clamp(page, 1, PageCount(total));

    /// <summary>Index of the first item on a page (zero-based)</summary>
    public static int StartIndex(int page, int total) => (ClampPage(page, total) - 1) * PageSize;

    /// <summary>Items on a page, as an inclusive first and last number for display (1-based)</summary>
    public static (int First, int Last) DisplayRange(int page, int total)
    {
        int start = StartIndex(page, total);
        int end = Math.Min(start + PageSize, total);
        return (start + 1, end);
    }
}

/// <summary>A similar-tracks list and the seed it was built from, kept for paging</summary>
public sealed record SimilarResults(string SeedTitle, string SeedArtist, string RatingKey, IReadOnlyList<Track> Tracks);
