namespace PlexBot.Utils;

/// <summary>Shared formatting utilities for display strings</summary>
public static class FormatHelper
{
    /// <summary>Formats a duration in milliseconds as a human-readable string like "3:45" or "1:23:45"</summary>
    public static string FormatDuration(long durationMs)
    {
        TimeSpan timeSpan = TimeSpan.FromMilliseconds(durationMs);
        return FormatDuration(timeSpan);
    }

    /// <summary>Formats a TimeSpan as a human-readable string like "3:45" or "1:23:45"</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
    }
}
