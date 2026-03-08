# Contributing

## Voraussetzungen
- PowerShell 7+
- Windows (WPF GUI)
- Pester Modul installiert

## Setup
1. Repository klonen
2. In Repo-Root wechseln
3. Tests ausführen:
   - `pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage unit`
   - `pwsh -NoProfile -File ./dev/tools/pipeline/Invoke-TestPipeline.ps1 -Stage all`

## Architektur
- Produktivcode liegt in `dev/modules/`
- `simple_sort.ps1` ist GUI-Komposition/Entry
- Background-Ausführung über `dev/modules/BackgroundOps.ps1`

### WPF-Module (aktiver GUI-Stack)
- `dev/modules/WpfShims.ps1` (WPF-Typen/Binding-Hilfen)
- `dev/modules/WpfXaml.ps1` (XAML-Definition)
- `dev/modules/WpfHost.ps1` (Window-Host/Parse/Context)
- `dev/modules/WpfEventHandlers.ps1` (Event-Wiring)
- `dev/modules/WpfSelectionConfig.ps1` (Advanced-Options-Maps)
- `dev/modules/SimpleSort.WpfMain.ps1` (Start-WpfGui)

## Coding Standards
- Kleine, fokussierte Änderungen
- Öffentliche APIs stabil halten
- Keine hardcodierten Pfade/Farben außerhalb bestehender Designsystem-Primitiven
- Änderungen mit Unit-Tests validieren

## Teststrategie
- Erst zielgerichtete Tests für den geänderten Bereich
- Danach `tests: unit`
- Bei Infrastruktur-/Flow-Änderungen `tests: all`

## Pull Request Checkliste
- [ ] Relevante Tests lokal grün
- [ ] Keine Parser-/Lint-Fehler
- [ ] Backlog/Docs aktualisiert (wenn Scope betroffen)
- [ ] Breaking Changes dokumentiert
