# ADR 0001: Profilbasierter Modul-Loader

## Status
Accepted

## Kontext
Der Start von `simple_sort.ps1` soll schnell bleiben, während GUI-spezifische Module nur bei Bedarf geladen werden.

## Entscheidung
- Default-Profil: `core`
- WPF-spezifische Module werden lazy über Feature-Import geladen.

## Konsequenzen
- Schnellere Startzeit in CLI-/Core-Szenarien
- Klare Trennung zwischen Core und WPF-Oberfläche
