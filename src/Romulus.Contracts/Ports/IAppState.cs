namespace Romulus.Contracts.Ports;

/// <summary>
/// Port interface for application state management with undo/redo.
/// Maps to New-AppStatePort in PortInterfaces.ps1.
/// </summary>
public interface IAppState
{
    IDictionary<string, object?> Get();
    void Set(IDictionary<string, object?> patch, string reason = "update");
    IDisposable Watch(Action<IDictionary<string, object?>> handler);
    bool Undo();
    bool Redo();
    T? GetValue<T>(string key, T? defaultValue = default);
    void SetValue(string key, object? value);
    bool TestCancel();
    void RequestCancel();
    void ResetCancel();
}
