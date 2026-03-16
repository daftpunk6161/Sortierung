using System.ComponentModel;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-047: Testable interface for localization/i18n.</summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>XAML-bindable indexer: {Binding Loc[Key]}.</summary>
    string this[string key] { get; }

    /// <summary>Current locale code (de, en, fr).</summary>
    string CurrentLocale { get; }

    /// <summary>Available locale codes.</summary>
    IReadOnlyList<string> AvailableLocales { get; }

    /// <summary>Switch locale at runtime. Raises PropertyChanged for indexer.</summary>
    void SetLocale(string locale);

    /// <summary>Get localized string with format arguments.</summary>
    string Format(string key, params object[] args);
}
