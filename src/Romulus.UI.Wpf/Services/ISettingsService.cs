using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

/// <summary>RF-009: Testable interface for settings persistence.</summary>
public interface ISettingsService
{
    string? LastAuditPath { get; }
    string LastTheme { get; }

    /// <summary>RF-010: Load settings from disk as DTO (decoupled from ViewModel).</summary>
    SettingsDto? Load();

    void LoadInto(MainViewModel vm);
    bool SaveFrom(MainViewModel vm, string? lastAuditPath = null);
}
