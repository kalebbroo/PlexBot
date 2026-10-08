using System.Diagnostics;

namespace PlexBot.Core.Services.LavaLink;

/// <summary>Process-wide limit on Lavalink loads of Plex file URLs, shared by every guild. Plex drops parallel file
/// requests (headers, then a closed connection with no body), so loads are capped, and one failure makes every
/// caller back off together instead of each track retrying into the same burst.</summary>
public sealed class PlexStreamGate
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeProvider _time;
    private long _cooldownUntil; // TimeProvider timestamp; 0 when no cooldown is active

    public PlexStreamGate(int maxConcurrentLoads, TimeProvider? time = null)
    {
        MaxConcurrentLoads = Math.Max(1, maxConcurrentLoads);
        _slots = new SemaphoreSlim(MaxConcurrentLoads, MaxConcurrentLoads);
        _time = time ?? TimeProvider.System;
    }

    public int MaxConcurrentLoads { get; }

    /// <summary>Time left before new loads may start, or zero</summary>
    public TimeSpan CooldownRemaining
    {
        get
        {
            long until = Interlocked.Read(ref _cooldownUntil);
            if (until == 0) return TimeSpan.Zero;
            TimeSpan left = _time.GetElapsedTime(_time.GetTimestamp(), until);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Holds new loads back for at least this long. An existing longer cooldown is kept.</summary>
    public void Cooldown(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        long target = _time.GetTimestamp() + (long)(duration.TotalSeconds * _time.TimestampFrequency);
        long current;
        do
        {
            current = Interlocked.Read(ref _cooldownUntil);
            if (current >= target) return;
        }
        while (Interlocked.CompareExchange(ref _cooldownUntil, target, current) != current);
    }

    /// <summary>Waits for any cooldown and a free slot, runs the load, then frees the slot</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        await WaitForCooldownAsync(cancellationToken).ConfigureAwait(false);
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A cooldown may have started while this caller waited for a slot
            await WaitForCooldownAsync(cancellationToken).ConfigureAwait(false);
            return await load(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task WaitForCooldownAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        while ((wait = CooldownRemaining) > TimeSpan.Zero)
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The gate for this process, sized from <c>plex.stream.maxConcurrentLoads</c> (falling back to
    /// <c>plex.maxConcurrentResolves</c>)</summary>
    public static PlexStreamGate FromConfig() =>
        new(Utils.BotConfig.GetInt("plex.stream.maxConcurrentLoads", Utils.BotConfig.GetInt("plex.maxConcurrentResolves", 2)));
}

/// <summary>How a Lavalink load of a Plex URL ended, and whether trying again can help</summary>
public enum LoadOutcome { Loaded, NotFound, Retriable }

/// <summary>Retry schedule and failure classification for Plex loads. Kept free of I/O so it can be unit tested.</summary>
public sealed class PlexLoadRetryPolicy(IReadOnlyList<TimeSpan> delays)
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    public IReadOnlyList<TimeSpan> Delays { get; } = delays.Count > 0 ? delays : DefaultDelays;

    /// <summary>Total attempts, the first load plus one per delay</summary>
    public int MaxAttempts => Delays.Count + 1;

    /// <summary>Delay before the given retry (attempt 1 is the first retry)</summary>
    public TimeSpan DelayBefore(int attempt) => Delays[Math.Clamp(attempt - 1, 0, Delays.Count - 1)];

    /// <summary>Lavalink reports Plex's dropped responses only as a generic "fault" ("Something went wrong while
    /// looking up the track"), so every error is treated as retriable. No match (404, or not a playable URL that
    /// Lavalink recognises) is permanent.</summary>
    public static LoadOutcome Classify(bool hasTrack, bool isError, bool timedOut)
    {
        if (hasTrack) return LoadOutcome.Loaded;
        if (isError || timedOut) return LoadOutcome.Retriable;
        return LoadOutcome.NotFound;
    }

    /// <summary>Reads <c>plex.stream.retryDelaysSeconds</c>, a comma-separated list such as "2, 5, 15"</summary>
    public static PlexLoadRetryPolicy FromConfig()
    {
        string raw = Utils.BotConfig.GetString("plex.stream.retryDelaysSeconds", "2, 5, 15");
        return new PlexLoadRetryPolicy(ParseDelays(raw));
    }

    public static List<TimeSpan> ParseDelays(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : -1)
            .Where(v => v >= 0)
            .Select(TimeSpan.FromSeconds)
            .ToList();
}
