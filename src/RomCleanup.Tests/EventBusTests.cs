using RomCleanup.Infrastructure.Events;
using Xunit;

namespace RomCleanup.Tests;

public class EventBusTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void Subscribe_ReturnsUniqueIds()
    {
        var id1 = _bus.Subscribe("test", _ => { });
        var id2 = _bus.Subscribe("test", _ => { });
        Assert.NotEqual(id1, id2);
        Assert.StartsWith("sub-", id1);
    }

    [Fact]
    public void Publish_DeliversToSubscriber()
    {
        int callCount = 0;
        _bus.Subscribe("scan.complete", _ => callCount++);
        _bus.Publish("scan.complete");
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Publish_NoSubscribers_ReturnsZero()
    {
        var delivered = _bus.Publish("unused.topic");
        Assert.Equal(0, delivered);
    }

    [Fact]
    public void Publish_MultipleSubscribers()
    {
        int count = 0;
        _bus.Subscribe("event", _ => count++);
        _bus.Subscribe("event", _ => count += 10);
        _bus.Publish("event");
        Assert.Equal(11, count);
    }

    [Fact]
    public void Publish_WildcardMatch()
    {
        int count = 0;
        _bus.Subscribe("AppState.*", _ => count++);
        _bus.Publish("AppState.Changed");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Publish_WildcardNoMatch()
    {
        int count = 0;
        _bus.Subscribe("AppState.*", _ => count++);
        _bus.Publish("Scan.Complete");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Publish_ExactAndWildcard_BothFire()
    {
        int exactCount = 0, wildcardCount = 0;
        _bus.Subscribe("run.done", _ => exactCount++);
        _bus.Subscribe("run.*", _ => wildcardCount++);
        _bus.Publish("run.done");
        Assert.Equal(1, exactCount);
        Assert.Equal(1, wildcardCount);
    }

    [Fact]
    public void Publish_SubscriberThrows_OthersContinue()
    {
        int count = 0;
        _bus.Subscribe("test", _ => throw new Exception("boom"));
        _bus.Subscribe("test", _ => count++);
        _bus.Publish("test");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Unsubscribe_RemovesHandler()
    {
        int count = 0;
        var id = _bus.Subscribe("test", _ => count++);
        _bus.Unsubscribe(id);
        _bus.Publish("test");
        Assert.Equal(0, count);
    }

    [Fact]
    public void Unsubscribe_InvalidId_ReturnsFalse()
    {
        Assert.False(_bus.Unsubscribe("sub-999"));
    }

    [Fact]
    public void Initialize_ClearsAll()
    {
        int count = 0;
        _bus.Subscribe("test", _ => count++);
        _bus.Initialize();
        _bus.Publish("test");
        Assert.Equal(0, count);
        Assert.Equal(0, _bus.SubscriptionCount);
    }

    [Fact]
    public void Publish_PayloadContainsData()
    {
        object? received = null;
        _bus.Subscribe("data", p => received = p.Data);
        _bus.Publish("data", 42);
        Assert.Equal(42, received);
    }

    [Fact]
    public void Publish_PayloadContainsTopic()
    {
        string? topic = null;
        _bus.Subscribe("info", p => topic = p.Topic);
        _bus.Publish("info");
        Assert.Equal("info", topic);
    }

    [Fact]
    public void SubscriptionCount_TracksCorrectly()
    {
        Assert.Equal(0, _bus.SubscriptionCount);
        _bus.Subscribe("a", _ => { });
        _bus.Subscribe("b", _ => { });
        Assert.Equal(2, _bus.SubscriptionCount);
    }
}
