using System.Globalization;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

public sealed class WpfDialogServiceLocalizationTests
{
    [Fact]
    public void F006_ResolveLocalizedDefault_UsesLocaleValue_ForEmptyInput()
    {
        using var cultureScope = new UiCultureScope("en");

        var resolved = WpfDialogService.ResolveLocalizedDefault(
            string.Empty,
            "Ordner auswählen",
            "Dialog.BrowseFolder.FolderTitle");

        Assert.Equal("Select folder", resolved);
    }

    [Fact]
    public void F006_ResolveLocalizedDefault_UsesLocaleValue_ForGermanDefaultLiteral()
    {
        using var cultureScope = new UiCultureScope("en");

        var resolved = WpfDialogService.ResolveLocalizedDefault(
            "Alle Dateien|*.*",
            "Alle Dateien|*.*",
            "Dialog.FileFilter.AllFiles");

        Assert.Equal("All Files|*.*", resolved);
    }

    [Fact]
    public void F006_ResolveLocalizedDefault_PreservesExplicitCustomValue()
    {
        using var cultureScope = new UiCultureScope("fr");

        var resolved = WpfDialogService.ResolveLocalizedDefault(
            "My Explicit Title",
            "Ordner auswählen",
            "Dialog.BrowseFolder.FolderTitle");

        Assert.Equal("My Explicit Title", resolved);
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previousCurrentCulture;
        private readonly CultureInfo _previousCurrentUiCulture;

        public UiCultureScope(string cultureName)
        {
            _previousCurrentCulture = CultureInfo.CurrentCulture;
            _previousCurrentUiCulture = CultureInfo.CurrentUICulture;

            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCurrentCulture;
            CultureInfo.CurrentUICulture = _previousCurrentUiCulture;
        }
    }
}
