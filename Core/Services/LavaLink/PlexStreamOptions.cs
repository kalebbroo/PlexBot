using System.Globalization;
using PlexBot.Utils;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Settings for loading Plex files through Lavalink, read once from the <c>plex</c> section of the config and
/// shared by the stream gate, the retry policy, the track resolver and the queue resolver</summary>
/// <param name="MaxConcurrentLoads">Plex loads allowed at once, across every guild</param>
/// <param name="RetryDelays">Wait before each retry of a failed load; one retry per entry</param>
/// <param name="ResolveAhead">Queued items resolved ahead of the one playing</param>
/// <param name="ResolveCacheSize">Most resolved tracks kept in the resolve cache</param>
/// <param name="ResolveCacheLifetime">How long a resolved track stays in the cache</param>
public sealed record PlexStreamOptions(int MaxConcurrentLoads, IReadOnlyList<TimeSpan> RetryDelays, int ResolveAhead,
    int ResolveCacheSize, TimeSpan ResolveCacheLifetime)
{
    /// <summary>Retry schedule used when the configured one is empty or unreadable</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    /// <summary>Reads the options from the bot config. <c>plex.stream.maxConcurrentLoads</c> falls back to the older
    /// <c>plex.maxConcurrentResolves</c> key.</summary>
    public static PlexStreamOptions FromConfig()
    {
        int maxLoads = BotConfig.GetInt("plex.stream.maxConcurrentLoads", BotConfig.GetInt("plex.maxConcurrentResolves", 2));
        List<TimeSpan> delays = ParseDelays(BotConfig.GetString("plex.stream.retryDelaysSeconds", "2, 5, 15"));
        return new PlexStreamOptions(
            MaxConcurrentLoads: Math.Max(1, maxLoads),
            RetryDelays: delays.Count > 0 ? delays : DefaultRetryDelays,
            ResolveAhead: Math.Max(1, BotConfig.GetInt("plex.stream.resolveAhead", 3)),
            ResolveCacheSize: Math.Max(1, BotConfig.GetInt("plex.resolveCacheSize", 500)),
            ResolveCacheLifetime: TimeSpan.FromMinutes(Math.Max(1, BotConfig.GetInt("plex.resolveCacheMinutes", 60))));
    }

    /// <summary>Parses a comma-separated list of seconds such as "2, 5, 15". Invalid and negative entries are skipped.</summary>
    public static List<TimeSpan> ParseDelays(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? seconds : -1)
            .Where(seconds => seconds >= 0)
            .Select(TimeSpan.FromSeconds)
            .ToList();
}
