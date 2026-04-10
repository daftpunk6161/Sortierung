using Romulus.Infrastructure.State;
using Xunit;

namespace Romulus.Tests;

public class AppStateStoreTests
{
    private readonly AppStateStore _store = new();

    [Fact]
    public void Get_InitialState_Empty()
    {
        var state = _store.Get();
        Assert.Empty(state);
    }

    [Fact]
    public void SetAndGet_StoresValue()
    {
        _store.Set(new Dictionary<string, object?> { ["mode"] = "DryRun" });

        var state = _store.Get();
        Assert.Equal("DryRun", state["mode"]);
    }

    [Fact]
    public void SetValue_GetValue_Typed()
    {
        _store.SetValue("count", 42);

        Assert.Equal(42, _store.GetValue<int>("count"));
    }

    [Fact]
    public void GetValue_Missing_ReturnsDefault()
    {
        Assert.Equal(0, _store.GetValue<int>("nope"));
        Assert.Null(_store.GetValue<string>("nope"));
        Assert.Equal("fallback", _store.GetValue("nope", "fallback"));
    }

    [Fact]
    public void Undo_RevertsLastChange()
    {
        _store.SetValue("x", 1);
        _store.SetValue("x", 2);

        Assert.Equal(2, _store.GetValue<int>("x"));

        var undone = _store.Undo();
        Assert.True(undone);
        Assert.Equal(1, _store.GetValue<int>("x"));
    }

    [Fact]
    public void Redo_ReappliesUndoneChange()
    {
        _store.SetValue("x", 1);
        _store.SetValue("x", 2);
        _store.Undo();

        var redone = _store.Redo();
        Assert.True(redone);
        Assert.Equal(2, _store.GetValue<int>("x"));
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsFalse()
    {
        Assert.False(_store.Undo());
    }

    [Fact]
    public void Redo_EmptyStack_ReturnsFalse()
    {
        Assert.False(_store.Redo());
    }

    [Fact]
    public void Set_ClearsRedoStack()
    {
        _store.SetValue("x", 1);
        _store.SetValue("x", 2);
        _store.Undo();

        // New change should clear redo
        _store.SetValue("x", 3);
        Assert.False(_store.Redo());
    }

    [Fact]
    public void Watch_NotifiesOnChange()
    {
        var notifications = new List<IDictionary<string, object?>>();
        using var _ = _store.Watch(state => notifications.Add(state));

        _store.SetValue("key", "val");

        Assert.Single(notifications);
        Assert.Equal("val", notifications[0]["key"]);
    }

    [Fact]
    public void Watch_DisposableUnsubscribes()
    {
        var count = 0;
        var watcher = _store.Watch(_ => count++);

        _store.SetValue("a", 1);
        Assert.Equal(1, count);

        watcher.Dispose();

        _store.SetValue("b", 2);
        Assert.Equal(1, count); // no additional notification
    }

    [Fact]
    public void TestCancel_DefaultFalse()
    {
        Assert.False(_store.TestCancel());
    }

    [Fact]
    public void RequestCancel_TestCancel_ReturnsTrue()
    {
        _store.RequestCancel();
        Assert.True(_store.TestCancel());
    }

    [Fact]
    public void ResetCancel_AfterRequest_ReturnsFalse()
    {
        _store.RequestCancel();
        _store.ResetCancel();
        Assert.False(_store.TestCancel());
    }

    [Fact]
    public void Set_Patch_MergesIntoState()
    {
        _store.Set(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });
        _store.Set(new Dictionary<string, object?> { ["b"] = 3, ["c"] = 4 });

        var state = _store.Get();
        Assert.Equal(1, state["a"]);
        Assert.Equal(3, state["b"]);
        Assert.Equal(4, state["c"]);
    }

    [Fact]
    public void GetValue_CaseInsensitiveKeys()
    {
        _store.SetValue("MyKey", "value");
        Assert.Equal("value", _store.GetValue<string>("mykey"));
        Assert.Equal("value", _store.GetValue<string>("MYKEY"));
    }
}
