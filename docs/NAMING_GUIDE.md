# Naming-Guide — Sprach- & Benennungsregeln

> Stand: 2026-03-02 | Ref: OPEN_ITEMS_CONSOLIDATED §2.3

---

## 1 Sprachpolicy (DE/EN)

### 1.1 Grundregel

| Kontext | Sprache | Begründung |
|---------|---------|------------|
| **Funktionsnamen** | Englisch | PowerShell-Verb-Noun-Konvention (`Invoke-`, `Get-`, `Set-`) |
| **Variablen & Parameter** | Englisch | Konsistenz mit Funktionsnamen |
| **Code-Kommentare** | Englisch | Einheitlich für alle Entwickler |
| **UI-Texte (Labels, Buttons)** | Deutsch via i18n | `data/i18n/de.json` ist Primärquelle |
| **Log-Nachrichten** | Englisch | Maschinenlesbar, Support-tauglich |
| **Fehler-/Warnmeldungen (User)** | Deutsch via i18n | Über Localization-Modul |
| **Commit-Messages** | Englisch | Standard für Git-Historie |
| **Dokumentation (Docs-Ordner)** | Deutsch | Zielgruppe ist deutschsprachig |
| **ADRs** | Englisch | Architektur-Entscheidungen maschinenlesbar |

### 1.2 Log-Migration (Ist-Zustand)

| Kategorie | Anteil | Ziel |
|-----------|--------|------|
| Deutsch-hardcoded Log-Strings | ≈42% | → Englisch oder i18n-Key |
| Englische Log-Strings | ≈5% | Beibehalten |
| i18n-gesteuert (nur UI) | ≈53% | Beibehalten |

**Migrationsstrategie:**
- Neue Log-Nachrichten **müssen** Englisch sein.
- Bestehende deutsche Log-Strings werden bei Änderung der betroffenen Funktion auf Englisch migriert (Opportunistic Refactoring).
- `LogLanguagePolicy.ps1` bleibt Referenzmodul für die Policy-Durchsetzung.
- Keine Big-Bang-Migration — schrittweise bei Wartungsarbeiten.

---

## 2 Funktionsbenennungen

### 2.1 PowerShell Verb-Noun-Konvention

```
<ApprovedVerb>-<Prefix><Noun>
```

| Element | Regel | Beispiel |
|---------|-------|----------|
| Verb | Nur `Get-Verb`-approved Verben | `Invoke-`, `Get-`, `Set-`, `New-`, `Remove-`, `Test-`, `Export-` |
| Prefix | Modul/Domäne (optional bei eindeutiger Zuordnung) | `RomCleanup`, `Wpf`, `Dat`, `Run` |
| Noun | PascalCase, beschreibend, Singular | `DedupeService`, `MemoryBudget`, `PhaseMetric` |

### 2.2 Schichtenspezifische Prefixe

| Schicht | Prefix-Muster | Beispiel |
|---------|---------------|----------|
| Domain | `<Domäne>` | `Get-FormatScore`, `Test-MemoryBudget` |
| Application | `Invoke-Run<Use-Case>Service` | `Invoke-RunDedupeService` |
| Adapter (WPF) | `<Wpf>Slice.<Feature>` | `WpfSlice.Roots.ps1` |
| Adapter (CLI) | `Invoke-Cli<X>Adapter` | `Invoke-CliRunAdapter` |
| Adapter (API) | `Invoke-Api<X>` | `Invoke-ApiServer` |
| Infrastructure | `<Concern>` | `Write-CatchGuardLog`, `Export-PhaseMetrics` |

### 2.3 Verbotene Muster

| Anti-Pattern | Grund | Korrektur |
|--------------|-------|-----------|
| `Do-Something` | `Do` ist kein approved Verb | `Invoke-Something` |
| `Process-XYZ` | Zu generisch | Spezifischeres Verb wählen |
| `*Legacy*` im Funktionsnamen | Neue Funktionen nicht mit Legacy benennen | Funktionalen Namen wählen |
| Deutsche Funktionsnamen | Inkonsistenz | Englisch |
| Mehr als 500 LOC pro Datei | Governance-Gate | Aufteilen in fokussierte Module |

---

## 3 Dateibenennungen

### 3.1 Modulnamen

```
<Layer>.<Feature>.ps1        — für Schicht-zugeordnete Module
<Feature>.ps1                — für domänenspezifische Module
<Feature>.Tests.ps1          — für Testdateien
```

| Muster | Beispiel |
|--------|----------|
| Domain | `Dedupe.ps1`, `FormatScoring.ps1`, `Classification.ps1` |
| Application | `ApplicationServices.ps1`, `RunOrchestrationService.ps1` |
| WPF-Slice | `WpfSlice.Roots.ps1`, `WpfSlice.Settings.ps1` |
| Infrastructure | `RunspaceLifecycle.ps1`, `CatchGuard.ps1` |
| Contracts | `UseCaseContracts.ps1`, `DataContracts.ps1`, `ErrorContracts.ps1` |
| Tests | `Dedupe.Tests.ps1`, `ModuleDependencyBoundary.Tests.ps1` |

### 3.2 Datenfiles

| Typ | Konvention | Beispiel |
|-----|-----------|----------|
| JSON-Konfiguration | kebab-case | `console-maps.json`, `tool-hashes.json` |
| i18n | ISO 639-1 | `de.json`, `en.json` |
| XAML | PascalCase | `MainWindow.xaml`, `Theme.Resources.xaml` |
| Reports | `<type>-<timestamp>.<ext>` | `move-plan-20260301-054011.json` |

---

## 4 Variablen & Scoping

| Scope | Konvention | Beispiel |
|-------|-----------|----------|
| Lokale Variablen | `$camelCase` | `$result`, `$toolPaths`, `$dedupeParams` |
| Script-Scope | `$script:PascalCase` | `$script:RC_XAML_MAIN`, `$script:AppState` |
| Parameter | `$PascalCase` | `$Roots`, `$Extensions`, `$Mode` |
| Konstanten | `$UPPER_SNAKE_CASE` | `$MAX_PARALLEL`, `$DEFAULT_HASH` |
| Hashtable-Keys | `camelCase` | `@{ toolPaths = ...; datRoot = ... }` |

---

## 5 Compliance-Prüfung

- **Neue Funktionen:** Müssen dieser Policy folgen — PR-Gate in Review-Checklist.
- **Bestehende Funktionen:** Bei Änderung opportunistisch anpassen.
- **PSScriptAnalyzer:** `PSScriptAnalyzerSettings.psd1` erzwingt approved Verben.
- **Governance-Gate:** `dev/tools/Invoke-GovernanceGate.ps1` prüft Dateigrößen (2000/4000 Warn/Hard, 200/500 Funktionen).
