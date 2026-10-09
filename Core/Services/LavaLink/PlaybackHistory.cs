namespace PlexBot.Core.Services.LavaLink;

/// <summary>The tracks a player has played, most recent last, for the Back button. Going back does not count as a new
/// play: the track that was playing is queued again rather than recorded, so a second Back goes to the track before.</summary>
/// <param name="limit">How many earlier tracks to keep</param>
public sealed class PlaybackHistory<T>(int limit) where T : class
{
    private readonly List<T> _earlier = [];
    private T? _current;
    private bool _suppressNextStart;

    /// <summary>The track that is playing now, if any</summary>
    public T? Current => _current;

    /// <summary>Number of earlier tracks available to go back to</summary>
    public int Count => _earlier.Count;

    /// <summary>Records that a track started. The track that was playing before it is kept, unless this start is the
    /// one a Back action caused.</summary>
    public void OnStarted(T started)
    {
        if (_suppressNextStart)
            _suppressNextStart = false;
        else if (_current is not null)
        {
            _earlier.Add(_current);
            if (_earlier.Count > limit)
                _earlier.RemoveAt(0);
        }
        _current = started;
    }

    /// <summary>Removes and returns the most recent earlier track, or null when there is none. The next start is treated
    /// as a Back action, so the track that is replaced is not recorded.</summary>
    public T? TakePrevious()
    {
        if (_earlier.Count == 0)
            return null;
        T previous = _earlier[^1];
        _earlier.RemoveAt(_earlier.Count - 1);
        _suppressNextStart = true;
        return previous;
    }
}
