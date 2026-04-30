using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Pin-Test: jede <c>{StaticResource Name}</c> Referenz in einer WPF-XAML-Datei
/// muss in irgendeiner XAML-Datei des UI-Projekts (App.xaml MergedDictionaries
/// inklusive lokaler Window/UserControl-Resources) als <c>x:Key="Name"</c>
/// definiert sein.
///
/// Hintergrund: Am 2026-04-30 ist <c>Romulus.UI.Wpf</c> beim Start mit
/// "Die Ressource mit dem Namen 'SubsectionHeader' kann nicht gefunden werden."
/// abgestuerzt — XamlParseException in MainWindow.xaml. Der Build war gruen,
/// alle Unit-Tests waren gruen, weil StaticResource-Lookups erst zur Laufzeit
/// passieren und kein Test BAML laedt. Dieser Test schliesst die Luecke
/// statisch ueber Text-Analyse, ohne WPF/STA/Application-Init zu benoetigen.
///
/// Ein Treffer in irgendeiner <c>.xaml</c> reicht aus (konservativ): wir wollen
/// den Bug-Typ "x:Key existiert nirgends" verlaesslich blockieren, ohne
/// false-positives durch lokal definierte Resources zu erzeugen.
/// </summary>
public sealed class WpfXamlStaticResourceIntegrityTests
{
    private static readonly Regex KeyRegex = new(
        "x:Key\\s*=\\s*\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex StaticResourceRegex = new(
        "\\{\\s*StaticResource\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*\\}",
        RegexOptions.Compiled);

    /// <summary>
    /// WPF-System-Keys / Framework-Resources, die nicht in Projekt-XAML
    /// deklariert sind, aber zur Laufzeit von WPF bereitgestellt werden.
    /// Liste bewusst klein halten — Erweiterungen nur mit klarer Begruendung.
    /// </summary>
    private static readonly HashSet<string> WellKnownExternalKeys = new(System.StringComparer.Ordinal)
    {
        // Aktuell keine; Liste existiert als Erweiterungspunkt.
    };

    [Fact]
    public void EveryStaticResourceReference_IsDefinedSomewhereInWpfXaml()
    {
        var wpfRoot = FindWpfProjectRoot();
        var xamlFiles = Directory
            .EnumerateFiles(wpfRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(xamlFiles);

        // Schritt 1: alle definierten Keys global einsammeln.
        var definedKeys = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in KeyRegex.Matches(text))
            {
                definedKeys.Add(m.Groups[1].Value);
            }
        }

        // Schritt 2: alle Referenzen einsammeln und gegen definierte Keys + Whitelist pruefen.
        var missing = new List<string>();
        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in StaticResourceRegex.Matches(lines[i]))
                {
                    var key = m.Groups[1].Value;
                    if (definedKeys.Contains(key)) continue;
                    if (WellKnownExternalKeys.Contains(key)) continue;

                    var rel = Path.GetRelativePath(wpfRoot, file).Replace('\\', '/');
                    missing.Add($"{rel}:{i + 1}  StaticResource '{key}' ist in keiner *.xaml als x:Key definiert.");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "Unaufgeloeste StaticResource-Referenzen — diese fuehren zur Laufzeit zu " +
            "XamlParseException beim WPF-Start:\n  " + string.Join("\n  ", missing));
    }

    private static string FindWpfProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "App.xaml")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Konnte src/Romulus.UI.Wpf nicht finden.");
    }
}
