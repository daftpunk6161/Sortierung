using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// GUI-047: JSON-based localization service with runtime locale switching.
/// Loads strings from data/i18n/{locale}.json. Falls back to de.json.
/// Implements INotifyPropertyChanged so XAML bindings auto-update on locale change.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private Dictionary<string, string> _strings = new();
    private string _currentLocale = "de";

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationService()
    {
        LoadStrings("de");
    }

    // Some WPF dependency properties default to TwoWay binding.
    // A no-op setter keeps accidental TwoWay bindings from crashing at startup.
    public string this[string key]
    {
        get => _strings.TryGetValue(key, out var value) ? value : $"[{key}]";
        set { /* intentionally ignored */ }
    }

    public string CurrentLocale => _currentLocale;

    public IReadOnlyList<string> AvailableLocales => DiscoverLocales();

    public void SetLocale(string locale)
    {
        if (string.Equals(_currentLocale, locale, StringComparison.OrdinalIgnoreCase))
            return;

        LoadStrings(locale);
        _currentLocale = locale;

        // Raise for indexer ("Item[]") so all XAML bindings refresh
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLocale)));
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        if (template.StartsWith('[') && template.EndsWith(']'))
            return template; // key not found
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    private void LoadStrings(string locale)
    {
        var baseDict = FeatureService.LoadLocale("de");
        if (string.Equals(locale, "de", StringComparison.OrdinalIgnoreCase))
        {
            _strings = baseDict;
            return;
        }

        var overlay = FeatureService.LoadLocale(locale);
        if (overlay.Count == 0)
        {
            _strings = baseDict;
            return;
        }

        // Per-key fallback: DE base + overlay from target locale
        foreach (var kv in overlay)
            baseDict[kv.Key] = kv.Value;

        _strings = baseDict;
    }

    private static List<string> DiscoverLocales()
    {
        var dir = FeatureService.ResolveDataDirectory("i18n");
        if (dir is null || !Directory.Exists(dir))
            return ["de"];

        return Directory.GetFiles(dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !n.StartsWith('_'))
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
    }
}
