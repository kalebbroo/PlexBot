namespace PlexBot.Core.Services.LavaLink;

/// <summary>Process-wide limit on Lavalink loads of Plex file URLs, shared by every guild. Plex drops parallel file
/// requests (headers, then a closed connection with no body), so loads are capped, and one failure makes every
/// caller back off together instead of each track retrying into the same burst.</summary>
/// <param name="maxConcurrentLoads">Loads allowed at once; values below 1 are raised to 1</param>
/// <param name="time">Clock used for the cooldown, replaceable in tests</param>
public sealed class PlexStreamGate(int maxConcurrentLoads, TimeProvider time)
{
    /// <summary>Loads allowed at once</summary>
    public int MaxConcurrentLoads { get; } = Math.Max(1, maxConcurrentLoads);

    /// <summary>One permit per load slot</summary>
    public SemaphoreSlim Slots { get; } = new(Math.Max(1, maxConcurrentLoads), Math.Max(1, maxConcurrentLoads));

    /// <summary>Clock timestamp at which the cooldown ends, or 0 when none is active. A field because it is updated
    /// with Interlocked compare-and-swap, which needs a ref.</summary>
    private long _cooldownUntil;

    /// <summary>Time left before new loads may start, or zero</summary>
    public TimeSpan CooldownRemaining
    {
        get
        {
            long until = Interlocked.Read(ref _cooldownUntil);
            if (until == 0) return TimeSpan.Zero;
            TimeSpan left = time.GetElapsedTime(time.GetTimestamp(), until);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Holds new loads back for at least this long. An existing longer cooldown is kept.</summary>
    public void Cooldown(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        long target = time.GetTimestamp() + (long)(duration.TotalSeconds * time.TimestampFrequency);
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
        await Slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A cooldown may have started while this caller waited for a slot
            await WaitForCooldownAsync(cancellationToken).ConfigureAwait(false);
            return await load(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Slots.Release();
        }
    }

    /// <summary>Waits until no cooldown is active. Loops because another caller may extend the cooldown meanwhile.</summary>
    public async Task WaitForCooldownAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        while ((wait = CooldownRemaining) > TimeSpan.Zero)
            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>How a Lavalink load ended. <see cref="Retriable"/> on a single attempt means trying again may help; on a
/// final <see cref="TrackResolution"/> it means every retry failed.</summary>
public enum LoadOutcome { Loaded, NotFound, Retriable }

/// <summary>The result of resolving a track: the Lavalink track when it loaded, and how the load ended</summary>
public sealed record TrackResolution(LavalinkTrack? Track, LoadOutcome Outcome)
{
    /// <summary>A failed resolve that was not attempted, or whose outcome is unknown</summary>
    public static TrackResolution Failed { get; } = new(null, LoadOutcome.Retriable);

    /// <summary>True when a playable track was loaded</summary>
    public bool IsLoaded => Track is not null;

    /// <summary>Why the track could not be played, worded for the notice posted in the channel</summary>
    public string FailureReason => Outcome == LoadOutcome.NotFound
        ? "Plex couldn't find a playable file for it."
        : "Plex didn't return the file after several tries.";
}

/// <summary>Retry schedule and failure classification for Plex loads. Kept free of I/O so it can be unit tested.</summary>
/// <param name="delays">Wait before each retry; an empty list uses <see cref="PlexStreamOptions.DefaultRetryDelays"/></param>
public sealed class PlexLoadRetryPolicy(IReadOnlyList<TimeSpan> delays)
{
    /// <summary>Wait before each retry, one entry per retry</summary>
    public IReadOnlyList<TimeSpan> Delays { get; } = delays.Count > 0 ? delays : PlexStreamOptions.DefaultRetryDelays;

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
}
