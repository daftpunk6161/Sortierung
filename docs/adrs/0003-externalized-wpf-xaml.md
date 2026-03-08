# ADR 0003: Externes WPF-XAML statt Inline-String

## Status
Accepted

## Kontext
Inline-XAML in Skriptdateien war fehleranfällig und schwer wartbar.

## Entscheidung
- Hauptfenster-XAML liegt in `dev/modules/wpf/MainWindow.xaml`.
- Theme-Ressourcen liegen in `dev/modules/wpf/Theme.Resources.xaml`.

## Konsequenzen
- Bessere Wartbarkeit und Diffbarkeit
- Geringeres Risiko bei UI-Änderungen
- Konsistente Wiederverwendung von Ressourcen
