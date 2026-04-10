# ADR 0003: Externes WPF-XAML statt Inline-String

## Status
Superseded (2026-03-11)

> **Abgelöst durch:** Native C# WPF-Projektstruktur.
> In der PowerShell-Version war XAML als String in Skript-Dateien eingebettet.
> Dieses ADR entschied, XAML in externe `.xaml`-Dateien auszulagern.
>
> In der C#-Lösung (`src/Romulus.UI.Wpf/`) ist XAML nativ Teil des
> Build-Prozesses — es gibt keine Inline-Strings mehr. Das Problem existiert nicht.

## Kontext (historisch)
Inline-XAML in Skriptdateien war fehleranfällig und schwer wartbar.

## Entscheidung (historisch)
- Hauptfenster-XAML liegt in `dev/modules/wpf/MainWindow.xaml`.
- Theme-Ressourcen liegen in `dev/modules/wpf/Theme.Resources.xaml`.

## Aktueller Stand (C#)
- `src/Romulus.UI.Wpf/MainWindow.xaml` — Hauptfenster
- `src/Romulus.UI.Wpf/App.xaml` — Application-Root
- `src/Romulus.UI.Wpf/Themes/` — ResourceDictionaries (Dark + Neon Accent)
