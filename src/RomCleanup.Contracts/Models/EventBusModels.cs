namespace RomCleanup.Contracts.Models;

/// <summary>
/// Event subscription for the in-process event bus.
/// </summary>
public sealed class EventSubscription
{
    public string Id { get; set; } = "";
    public string Topic { get; set; } = "";
    public Action<EventPayload> Handler { get; set; } = _ => { };
}

/// <summary>
/// Payload published through the event bus.
/// </summary>
public sealed class EventPayload
{
    public string Topic { get; set; } = "";
    public object? Data { get; set; }
    public string Timestamp { get; set; } = "";
}
