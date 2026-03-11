using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Events;

/// <summary>
/// Lightweight in-process pub/sub event bus.
/// Mirrors EventBus.ps1 with topic-based routing and wildcard matching.
/// Thread-safe for single-threaded environments (mirrors PS behavior).
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<string, List<EventSubscription>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private int _sequence;

    /// <summary>
    /// Resets the event bus (new session).
    /// </summary>
    public void Initialize()
    {
        _subscriptions.Clear();
        _sequence = 0;
    }

    /// <summary>
    /// Subscribes a handler to a topic. Returns subscription ID.
    /// </summary>
    public string Subscribe(string topic, Action<EventPayload> handler)
    {
        var id = $"sub-{++_sequence}";
        var subscription = new EventSubscription { Id = id, Topic = topic, Handler = handler };

        if (!_subscriptions.TryGetValue(topic, out var list))
        {
            list = new List<EventSubscription>();
            _subscriptions[topic] = list;
        }
        list.Add(subscription);

        return id;
    }

    /// <summary>
    /// Unsubscribes a handler by subscription ID.
    /// </summary>
    public bool Unsubscribe(string subscriptionId)
    {
        foreach (var (_, list) in _subscriptions)
        {
            var idx = list.FindIndex(s => s.Id == subscriptionId);
            if (idx >= 0)
            {
                list.RemoveAt(idx);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Publishes an event to all matching subscribers (exact + wildcard).
    /// Continues to remaining subscribers even if one throws.
    /// </summary>
    public int Publish(string topic, object? data = null)
    {
        var payload = new EventPayload
        {
            Topic = topic,
            Data = data,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        var handlers = CollectHandlers(topic);
        int delivered = 0;

        foreach (var handler in handlers)
        {
            try
            {
                handler(payload);
                delivered++;
            }
            catch
            {
                // Continue to remaining subscribers (mirrors PS behavior)
            }
        }

        return delivered;
    }

    /// <summary>
    /// Gets the current subscription count.
    /// </summary>
    public int SubscriptionCount => _subscriptions.Values.Sum(l => l.Count);

    private List<Action<EventPayload>> CollectHandlers(string topic)
    {
        var handlers = new List<Action<EventPayload>>();

        // Exact matches
        if (_subscriptions.TryGetValue(topic, out var exact))
            handlers.AddRange(exact.Select(s => s.Handler));

        // Wildcard matches (e.g. "AppState.*" matches "AppState.Changed")
        foreach (var (pattern, subs) in _subscriptions)
        {
            if (pattern == topic) continue; // already handled
            if (IsWildcardMatch(pattern, topic))
                handlers.AddRange(subs.Select(s => s.Handler));
        }

        return handlers;
    }

    private static bool IsWildcardMatch(string pattern, string topic)
    {
        if (!pattern.Contains('*')) return false;
        var prefix = pattern.Replace(".*", ".");
        if (prefix.EndsWith('.'))
            return topic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        // Generic wildcard: "scan*" matches "scan-complete"
        var basePattern = pattern.Replace("*", "");
        return topic.StartsWith(basePattern, StringComparison.OrdinalIgnoreCase);
    }
}
