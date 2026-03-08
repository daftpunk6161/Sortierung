# PR-Review-Checklist — Naming, Struktur & Hygiene-Gates

> Stand: 2026-03-02 | Ref: OPEN_ITEMS_CONSOLIDATED §2.3, NAMING_GUIDE.md

---

## Vor jedem PR: Autor prüft

### 1 Naming & Sprache
- [ ] Funktionsnamen nutzen PowerShell-Approved-Verben (`Get-Verb`)
- [ ] Keine deutschen Funktions- oder Variablennamen
- [ ] Neue Log-Nachrichten sind auf Englisch
- [ ] UI-Texte verwenden i18n-Keys aus `data/i18n/de.json`
- [ ] Parameter sind PascalCase, lokale Variablen camelCase

### 2 Struktur & Größe
- [ ] Keine Datei überschreitet 2000 LOC (Warn) / 4000 LOC (Hard)
- [ ] Keine Funktion überschreitet 200 LOC (Warn) / 500 LOC (Hard)
- [ ] Neue Module sind in `ModuleFileList.ps1` registriert
- [ ] Modulabhängigkeiten respektieren Schichtengrenzen (ADR-0004)

### 3 Dead Code & Duplikate
- [ ] Keine ungenutzten öffentlichen Funktionen eingeführt
- [ ] Keine Kopien bestehender Patterns — stattdessen Shared-Utility nutzen
- [ ] Kein Inline-XAML geändert ohne Sync mit `wpf/MainWindow.xaml`
- [ ] Catch-Blöcke sind nicht leer — mindestens `Write-CatchGuardLog` oder Kommentar

### 4 Tests
- [ ] Unit-Tests für neue/geänderte Funktionen vorhanden
- [ ] `Invoke-TestPipeline.ps1 -Stage unit` grün
- [ ] ModuleDependencyBoundary-Tests grün
- [ ] Governance-Gate bestanden (`Invoke-GovernanceGate.ps1`)

### 5 Fehlerbehandlung
- [ ] Keine neuen silent catches in Domain-/Application-/IO-Pfaden
- [ ] Fehler haben mindestens eine der Klassen: `Transient`, `Recoverable`, `Critical`
- [ ] Legacy-Pfade nicht erweitert — stattdessen aktuelle API nutzen

### 6 Dokumentation
- [ ] ADR geschrieben bei architekturrelevanten Entscheidungen
- [ ] ARCHITECTURE_MAP.md aktualisiert bei neuem Modul
- [ ] OPEN_ITEMS_CONSOLIDATED.md aktualisiert bei erledigtem Punkt

---

## Für Reviewer

### Quick-Gates (Block-Kriterien)
| Gate | Tool | Automatisiert? |
|------|------|----------------|
| Dateigröße | `Invoke-GovernanceGate.ps1` | ✅ Ja |
| PSScriptAnalyzer | `PSScriptAnalyzerSettings.psd1` | ✅ Ja |
| Unit-Tests | `Invoke-TestPipeline.ps1 -Stage unit` | ✅ Ja |
| Dependency-Boundary | `ModuleDependencyBoundary.Tests.ps1` | ✅ Ja |
| XAML-Sync | Manuell (bis XAML-SYNC-GATE CI implementiert) | ❌ Nein |
| Naming-Policy | Manuell (Review) | ❌ Nein |

### Review-Fokus
1. **Schichtverletzungen:** Greift UI-Code auf Domain zu? Greift Domain auf WPF zu?
2. **Silent Catches:** Neuer Catch ohne Logging → nachhaken
3. **Duplikation:** Pattern schon in Shared-Utility? → Konsolidieren
4. **Legacy-Erweiterung:** Wird ein Legacy-Shim erweitert statt ersetzt? → Hinterfragen
