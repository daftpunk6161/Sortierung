# ADR 0001: Profilbasierter Modul-Loader

## Status
Superseded (2026-03-11)

> **Abgelöst durch:** C# Dependency Injection (Konstruktor-Injection via .NET DI Container).
> Dieses ADR beschrieb das PowerShell-Modul-Laden mit `core`/`wpf`-Profilen.
> In der C#-Migration entfällt das Konzept vollständig — .NET DI registriert
> Services beim App-Start, WPF-spezifische Abhängigkeiten leben im UI.Wpf-Projekt.

## Kontext (historisch)
Der Start von `simple_sort.ps1` sollte schnell bleiben, während GUI-spezifische Module nur bei Bedarf geladen wurden.

## Entscheidung (historisch)
- Default-Profil: `core`
- WPF-spezifische Module wurden lazy über Feature-Import geladen.

## Konsequenzen
- ~~Schnellere Startzeit in CLI-/Core-Szenarien~~ → In C# durch separate Projekte (CLI, Api, UI.Wpf) gelöst.
- ~~Klare Trennung zwischen Core und WPF-Oberfläche~~ → In C# durch Projektstruktur und DI gelöst.
