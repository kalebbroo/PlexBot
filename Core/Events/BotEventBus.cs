using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Events;

/// <summary>Simple pub/sub event bus for bot lifecycle events.
/// Extensions can subscribe to events like track changes, player lifecycle, etc.</summary>
public class BotEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<BotEvent, Task>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>Subscribe a handler to a specific event type</summary>
    public void Subscribe(string eventType, Func<BotEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            if (!_handlers.TryGetValue(eventType, out List<Func<BotEvent, Task>>? handlers))
            {
                handlers = [];
                _handlers[eventType] = handlers;
            }
            handlers.Add(handler);
        }
        Logs.Debug($"Event bus: subscribed to '{eventType}'");
    }

    /// <summary>Unsubscribe a handler from a specific event type</summary>
    public void Unsubscribe(string eventType, Func<BotEvent, Task> handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out List<Func<BotEvent, Task>>? handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    /// <summary>Publish an event to all subscribers. Each handler is called sequentially
    /// with individual try/catch so one failing handler doesn't block others.</summary>
    public async Task PublishAsync(BotEvent botEvent)
    {
        List<Func<BotEvent, Task>>? handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(botEvent.EventType, out handlers) || handlers.Count == 0)
                return;
            handlers = [.. handlers]; // snapshot to avoid mutation during iteration
        }

        foreach (Func<BotEvent, Task> handler in handlers)
        {
            try
            {
                await handler(botEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Error($"Event bus handler error for '{botEvent.EventType}': {ex.Message}");
            }
        }
    }
}

/// <summary>Represents a bot event with type, timestamp, and arbitrary data</summary>
public class BotEvent
{
    /// <summary>The event type identifier (use constants from BotEvents)</summary>
    public required string EventType { get; init; }

    /// <summary>When the event occurred</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Arbitrary event data keyed by name</summary>
    public Dictionary<string, object> Data { get; init; } = [];
}

/// <summary>Well-known event type constants for the bot event bus</summary>
public static class BotEvents
{
    public const string TrackStarted = "track.started";
    public const string TrackEnded = "track.ended";
    public const string QueueChanged = "queue.changed";
    public const string PlayerCreated = "player.created";
    public const string PlayerDestroyed = "player.destroyed";
    public const string ExtensionLoaded = "extension.loaded";
    public const string BotReady = "bot.ready";
}
