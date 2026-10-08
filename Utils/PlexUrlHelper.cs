using System.Text.RegularExpressions;
using PlexBot.Core.Models.Media;

namespace PlexBot.Utils;

/// <summary>Helpers for Plex file URLs and part keys, used for cache keys and logs so neither ever carries the Plex token</summary>
public static partial class PlexUrlHelper
{
    /// <summary>Removes the X-Plex-Token query parameter from a URL, keeping any other parameters</summary>
    public static string StripToken(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        string stripped = TokenPattern().Replace(url, string.Empty);
        // If the token was the first parameter, the next one is left starting with '&'
        int query = stripped.IndexOf('?');
        int amp = stripped.IndexOf('&');
        if (query < 0 && amp >= 0)
            stripped = string.Concat(stripped.AsSpan(0, amp), "?", stripped.AsSpan(amp + 1));
        return stripped;
    }

    /// <summary>"part 12345" for logs, read from the track's part key or playback URL. Never includes the token.</summary>
    public static string PartId(Track track)
    {
        Match match = PartPattern().Match(!string.IsNullOrEmpty(track.PartKey) ? track.PartKey : track.PlaybackUrl ?? string.Empty);
        return match.Success ? $"part {match.Groups[1].Value}" : "no part id";
    }

    /// <summary>The track title plus its part id, the form every Plex load log line uses</summary>
    public static string Describe(Track track) => $"{track.Title} ({PartId(track)})";

    [GeneratedRegex(@"[?&]X-Plex-Token=[^&]*", RegexOptions.IgnoreCase)]
    public static partial Regex TokenPattern();

    [GeneratedRegex(@"/library/parts/(\d+)")]
    public static partial Regex PartPattern();
}
