# ADR-0020: DAT Catalog Management Architecture

## Status
Proposed

## Date
2026-03-28

## Context

### Problem
Romulus besitzt einen umfangreichen DAT-Katalog (`data/dat-catalog.json`, ~300 Eintraege),
aber das aktuelle DAT-Management ist ein monolithischer Workflow (`DatAutoUpdateAsync`),
der nur aggregierte Status-Information liefert und keinen browsebaren Ueberblick bietet.

Der Benutzer muss bisher:
1. DatAutoUpdate starten und auf einen Dialog warten
2. einen Confirm-Dialog fuer Auto-Downloads bestaetigen
3. fuer No-Intro und Redump manuell Ordner auswaehlen
4. hat keinen Ueberblick ueber den Einzelstatus jeder DAT-Quelle

### Ziel
Ein DATVault-aehnliches Erlebnis: Eine browsbare Katalog-Ansicht mit per-DAT Status,
Gruppenfilter, Batch-Operationen und One-Click Update/Download — innerhalb der bestehenden
Romulus-Architekturgrenzen und ohne externe Bezahldienst-Abhaengigkeit.

### Referenz: DATVault / RomVault Modell
- DATVault ist ein **bezahlter Proxy-Service** (Patreon), der alle DAT-Quellen crawlt (~6h Intervall)
- Bietet einheitlichen Download aller DATs ueber RomVault-Integration
- RomVault zeigt: Browsbare Grid-Ansicht, Gruppenfilter, Status-Indikatoren, Batch-Operationen
- DATVault-Spalten: Group, System, Status (new/updated/replaced/deleted), Date, Previous/New Version

### Bestehende Infrastruktur
- `data/dat-catalog.json`: Master-Katalog mit ~300 Eintraegen
  - Schema: `{ Id, Group, System, Url, Format, ConsoleKey, PackMatch }`
  - 6 Gruppen: Redump (63), No-Intro (112), Non-Redump (35), MAME (1), Libretro (3), FBNEO (17)
- `DatSourceService.cs`: Download-Service (zip-dat, raw-dat, 7z-dat, nointro-pack Import)
  - Sicherheit: HTTPS-only, SHA256-Verifikation, Zip-Slip-Schutz, 50MB Max
- `FeatureCommandService.Dat.cs`: Monolithischer `DatAutoUpdateAsync()` Workflow
- `DatAnalysisService.cs`: Status-Report (`BuildDatAutoUpdateReport`)

### Download-Einschraenkungen pro Gruppe
| Gruppe     | Anzahl | Format       | Auto-Download? | Einschraenkung           |
|------------|--------|-------------|----------------|--------------------------|
| FBNEO      | 17     | raw-dat     | Ja             | —                        |
| Libretro   | 3      | raw-dat     | Ja             | —                        |
| MAME       | 1      | 7z-dat      | Ja             | 7z-Tool erforderlich     |
| Redump     | 63     | zip-dat     | Nein           | Login auf redump.org     |
| No-Intro   | 112    | nointro-pack| Nein           | Pack von datomatic.no-intro.org |
| Non-Redump | 35     | nointro-pack| Nein           | Pack-Import              |

## Decision

### Architektur-Ueberblick

```
┌─────────────────────────────────────────────────────────┐
│                     GUI (Entry Point)                    │
│  ┌──────────────────────────────────────────────────┐   │
│  │           DatCatalogView.xaml                     │   │
│  │  ┌────────────┐ ┌─────────────────────────────┐  │   │
│  │  │ GroupFilter │ │ DataGrid                    │  │   │
│  │  │ - All       │ │ Group|System|Status|Inst.|  │  │   │
│  │  │ - FBNEO     │ │ Action                     │  │   │
│  │  │ - Libretro  │ │                            │  │   │
│  │  │ - MAME      │ │ ... 300 rows ...           │  │   │
│  │  │ - Redump    │ │                            │  │   │
│  │  │ - No-Intro  │ └─────────────────────────────┘  │   │
│  │  │ - Non-Redum │                                  │   │
│  │  └────────────┘ [Alle aktualisieren] [Import...]  │   │
│  └──────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────┐   │
│  │           DatCatalogViewModel                     │   │
│  │  - CatalogEntries: ObservableCollection<DatCatalogItemVm> │
│  │  - SelectedGroup: string                          │   │
│  │  - FilteredView: ICollectionView                  │   │
│  │  - UpdateAllCommand / ImportPackCommand            │   │
│  │  - RefreshStatusCommand                           │   │
│  └──────────────────────────┬───────────────────────┘   │
└─────────────────────────────┼───────────────────────────┘
                              │
┌─────────────────────────────┼───────────────────────────┐
│              Infrastructure                              │
│  ┌──────────────────────────┴───────────────────────┐   │
│  │         DatCatalogStateService (NEU)              │   │
│  │  - LoadState() → DatCatalogState                  │   │
│  │  - SaveState(DatCatalogState)                     │   │
│  │  - BuildCatalogStatus(catalog, datRoot)           │   │
│  │    → List<DatCatalogStatusEntry>                  │   │
│  └──────────────────────────┬───────────────────────┘   │
│                              │                           │
│  ┌──────────────────────────┴───────────────────────┐   │
│  │         DatSourceService (BESTEHEND)              │   │
│  │  + DownloadDatByFormatAsync (mit Progress)        │   │
│  │  + ImportLocalDatPacks                            │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────┼───────────────────────────┐
│              Contracts                                    │
│  ┌──────────────────────────┴───────────────────────┐   │
│  │  DatCatalogStatusEntry (NEU)                      │   │
│  │  - Id, Group, System, ConsoleKey                  │   │
│  │  - Status: Missing|Installed|Stale|Error          │   │
│  │  - DownloadStrategy: Auto|PackImport|ManualLogin  │   │
│  │  - InstalledDate?, LocalPath?, FileSizeBytes?     │   │
│  │  - CatalogEntry (ref)                             │   │
│  └──────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────┐   │
│  │  DatCatalogState (NEU)                            │   │
│  │  - Entries: Dictionary<string, DatLocalInfo>      │   │
│  │  - LastFullScan: DateTime?                        │   │
│  └──────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────┐   │
│  │  DatLocalInfo (NEU)                               │   │
│  │  - InstalledDate: DateTime                        │   │
│  │  - FileSha256: string                             │   │
│  │  - FileSizeBytes: long                            │   │
│  │  - LocalPath: string                              │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### Neue Komponenten

#### 1. `DatCatalogState` + `DatLocalInfo` (Contracts)
Tracked den lokalen Status jeder DAT-Datei.

```csharp
// Contracts/Models/DatCatalogModels.cs
public sealed class DatCatalogState
{
    public Dictionary<string, DatLocalInfo> Entries { get; set; } = new();
    public DateTime? LastFullScan { get; set; }
}

public sealed class DatLocalInfo
{
    public DateTime InstalledDate { get; set; }
    public string FileSha256 { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string LocalPath { get; set; } = "";
}
```

Speicherort: `%APPDATA%\Romulus\dat-catalog-state.json`

Begruendung:
- Getrennt vom Master-Katalog (`dat-catalog.json`), der zum Repo gehoert
- Neben `settings.json` im gleichen AppData-Ordner
- Leichtgewichtig, kein DB erforderlich

#### 2. `DatCatalogStatusEntry` (Contracts)
Zusammenfuehrung aus Katalog + lokalem Status fuer die Anzeige.

```csharp
public enum DatInstallStatus { Missing, Installed, Stale, Error }
public enum DatDownloadStrategy { Auto, PackImport, ManualLogin }

public sealed class DatCatalogStatusEntry
{
    public string Id { get; init; } = "";
    public string Group { get; init; } = "";
    public string System { get; init; } = "";
    public string ConsoleKey { get; init; } = "";
    public DatInstallStatus Status { get; init; }
    public DatDownloadStrategy DownloadStrategy { get; init; }
    public DateTime? InstalledDate { get; init; }
    public string? LocalPath { get; init; }
    public long? FileSizeBytes { get; init; }
}
```

#### 3. `DatCatalogStateService` (Infrastructure)
Zentrale Logik fuer Status-Berechnung — keine GUI-Abhaengigkeit.

Aufgaben:
- `LoadState(statePath) → DatCatalogState`
- `SaveState(statePath, state)`
- `BuildCatalogStatus(catalog, datRoot, state) → List<DatCatalogStatusEntry>`
  - Scannt `datRoot` nach .dat/.xml Dateien
  - Matched gegen Katalog-Eintraege (Id, ConsoleKey, PackMatch)
  - Berechnet Status (Missing/Installed/Stale basierend auf Alter)
  - Bestimmt DownloadStrategy aus Format+Group
- `UpdateStateAfterDownload(state, catalogId, localPath, sha256, size)`

Architektur-Einordnung: **Infrastructure** (Dateisystem-I/O fuer State + DAT-Scan).

#### 4. `DatCatalogViewModel` (GUI / WPF)
MVVM-ViewModel fuer die Katalog-Ansicht.

```csharp
public sealed class DatCatalogViewModel : ObservableObject
{
    // Collections
    public ObservableCollection<DatCatalogItemVm> AllEntries { get; }
    public ICollectionView FilteredEntries { get; }

    // Filter
    public string SelectedGroup { get; set; } = "All";
    public string SelectedStatus { get; set; } = "All";
    public string SearchText { get; set; } = "";

    // KPIs
    public int TotalCount { get; }
    public int InstalledCount { get; }
    public int MissingCount { get; }
    public int StaleCount { get; }

    // Commands
    public ICommand RefreshCommand { get; }         // Scan + Status aktualisieren
    public ICommand UpdateAutoCommand { get; }      // Alle auto-downloadbaren aktualisieren
    public ICommand ImportNoIntroPackCommand { get; }// No-Intro Pack-Import Dialog
    public ICommand ImportRedumpCommand { get; }    // Redump lokaler Import Dialog
    public ICommand DownloadSelectedCommand { get; }// Selektierte downloaden
}
```

Jedes `DatCatalogItemVm` hat:
- Alle Felder aus `DatCatalogStatusEntry`
- `IsSelected: bool` (fuer Batch-Selektion)
- StatusDisplay: Lokalisierter String + Farbe
- ActionDisplay: "Herunterladen" / "Pack importieren" / "Manuell (redump.org)" / "Aktuell"

#### 5. `DatCatalogView.xaml` (GUI / WPF)
Neue View-Seite im System-Bereich der Hauptnavigation.

Layout:
```
┌──────────────────────────────────────────────────────────┐
│ DAT-Verwaltung                                [Refresh]  │
├──────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Installiert: 127 │ Fehlend: 84 │ Veraltet: 12 │ Ges: 300 │ │
│ └──────────────────────────────────────────────────────┘ │
├──────────┬───────────────────────────────────────────────┤
│ Gruppe   │ [🔍 Suche...]                                 │
│ ──────── │ ┌─────┬────────────────┬────────┬────────────┐│
│ ☐ Alle   │ │ ☐   │ System         │ Gruppe │ Status     ││
│ ☐ FBNEO  │ ├─────┼────────────────┼────────┼────────────┤│
│ ☐ Libret │ │ ☐   │ FBN Arcade     │ FBNEO  │ ✓ Aktuell  ││
│ ☐ MAME   │ │ ☐   │ FBN ColecoV.   │ FBNEO  │ ⟳ Veraltet ││
│ ☐ Redump │ │ ☐   │ No-Intro GBA   │ No-Int │ ✗ Fehlend  ││
│ ☐ No-Int │ │ ☐   │ Redump PS2     │ Redump │ ✗ Fehlend  ││
│ ☐ Non-Re │ │ ...  │ ...            │ ...    │ ...        ││
│          │ └─────┴────────────────┴────────┴────────────┘│
├──────────┴───────────────────────────────────────────────┤
│ [Alle Auto-DATs aktualisieren] [No-Intro Import] [Redump Import] │
└──────────────────────────────────────────────────────────┘
```

### Aenderungen an bestehenden Komponenten

#### DatSourceService (minimal)
- `DownloadDatByFormatAsync`: optionaler `IProgress<string>` Parameter fuer Fortschritt
- Keine Aenderung der bestehenden Sicherheitslogik
- Rueckgabe-Typ erwedern auf `DatDownloadResult` mit `{ Path, Sha256, SizeBytes }`

#### FeatureCommandService.Dat.cs (Refactor)
- `DatAutoUpdateAsync` bleibt bestehen fuer die Quick-Action
- Neue Methode delegiert an `DatCatalogStateService` fuer Status-Berechnung
- Vermeidet doppelte Logik zwischen alter Quick-Action und neuem Katalog

#### MainWindow Navigation
- Neuer Tab/Bereich "DAT-Verwaltung" im System-Tab oder als eigener Top-Level Tab
- Lazy-Loading: View wird erst beim ersten Zugriff initialisiert

### Download-Strategien pro Gruppe

```
DatDownloadStrategy-Entscheidungsbaum:

Format == "raw-dat" oder "zip-dat" oder "7z-dat"
  AND Url != null
  AND Group != "Redump"
  → Auto

Format == "nointro-pack"
  → PackImport

Group == "Redump"
  → ManualLogin

Sonst (kein Url, unbekanntes Format)
  → ManualLogin
```

| Strategie    | GUI-Aktion                                          | Automatisierbar? |
|-------------|-----------------------------------------------------|-----------------|
| Auto        | "Herunterladen" Button → direkter Download          | Ja              |
| PackImport  | "Pack importieren" → Ordner-Dialog                  | Teilweise       |
| ManualLogin | Info-Text "Manuell von redump.org herunterladen"    | Nein            |

### State-File Lifecycle

```
App-Start → LoadState()
         → BuildCatalogStatus(catalog, datRoot, state)
         → UI zeigt Status

Download  → DatSourceService.DownloadDatByFormatAsync(...)
         → UpdateStateAfterDownload(state, id, path, sha256, size)
         → SaveState()
         → UI aktualisiert Einzelzeile

Import    → DatSourceService.ImportLocalDatPacks(...)
         → Fuer jeden importierten: UpdateStateAfterDownload(...)
         → SaveState()

Refresh   → State invalidierten → Voller Rescan
```

## Konsequenzen

### Positiv
- Benutzer sieht auf einen Blick Gesamtstatus aller ~300 DATs
- Batch-Operationen: "Alle FBNEO aktualisieren" mit einem Klick
- Klare Trennung Katalog-Definition (Repo) vs. lokaler Status (AppData)
- Keine externe Service-Abhaengigkeit (kein DATVault-Abo noetig)
- Bestehende Sicherheitslogik (HTTPS, SHA256, Zip-Slip) wird wiederverwendet
- Ehrlich ueber Einschraenkungen: Redump braucht Login, No-Intro braucht Pack

### Negativ
- Kein automatischer Update-Check gegen Remote-Version (anders als DATVault)
  → Stale-Detection basiert auf Alter (>365 Tage), nicht auf Remote-Versions-Vergleich
- Redump bleibt manuell (kein API/Scraping — rechtlich und technisch problematisch)
- No-Intro bleibt Pack-basiert (datomatic.no-intro.org hat keine stabile REST API)

### Risiken
- State-File Korruption → Mitigation: Backup beim Schreiben (.bak)
- Grosse Batch-Downloads koennten langsam sein → Mitigation: Parallelisierung mit Limit (3 concurrent)
- UI-Thread-Blockierung bei Status-Scan → Mitigation: Async auf ThreadPool

## Alternativen betrachtet

### Alternative A: DATVault-Integration
- DATVault API als Proxy fuer alle Downloads nutzen
- **Verworfen**: Bezahlservice, keine stabile oeffentliche API, Abhaengigkeit von Drittanbieter

### Alternative B: Web-Scraping von No-Intro/Redump
- Automatischer Download durch Scraping der Webseiten
- **Verworfen**: Fragil, rechtlich problematisch, TOS-Verletzung moeglich

### Alternative C: Dialog statt Tab
- DAT-Katalog als modaler Dialog statt eingebetteter View
- **Verworfen**: 300 Eintraege brauchen dauerhafte Browsbarkeit, Dialog zu einschraenkend

### Alternative D: Nur CLI/API
- DAT-Katalog nur in CLI/API, kein GUI
- **Verworfen**: Kernzielgruppe nutzt GUI, DATVault-Vergleich referenziert GUI-Erlebnis

## Implementation Phases

### Phase 1: State-Tracking + Status-Anzeige (Minimum Viable)
1. `DatCatalogState` / `DatLocalInfo` Models (Contracts)
2. `DatCatalogStatusEntry` + Enums (Contracts)
3. `DatCatalogStateService` (Infrastructure)
4. `DatCatalogViewModel` + `DatCatalogView.xaml` (GUI, read-only Ansicht)
5. Tests: State Load/Save, Status-Berechnung, Strategy-Mapping

### Phase 2: Download-Integration
1. `DatSourceService` Progress-Support
2. "Alle Auto-DATs aktualisieren" Command
3. "No-Intro Import" + "Redump Import" Commands
4. Activity Log Integration
5. Tests: Download-Flow, State-Update nach Download

### Phase 3: Erweiterte UX
1. Einzelzeilen-Download ("Herunterladen" Button pro Zeile)
2. Selektion + Batch-Download
3. Gruppenfilter + Statusfilter
4. Suche
5. Export Status-Report (CSV)

### Phase 4: CLI/API Paritaet
1. CLI: `romulus dat-catalog --status` / `--update-auto` / `--import-pack <dir>`
2. API: `GET /api/dat/catalog` / `POST /api/dat/update` / `POST /api/dat/import`
3. Gleiche `DatCatalogStateService` Logik, keine Dopplung

## Testbedarf

### Unit Tests
- `DatCatalogStateService.BuildCatalogStatus`: korrekte Status-Ableitung
- DownloadStrategy-Mapping: Format+Group → Auto/PackImport/ManualLogin
- State Load/Save: Roundtrip, korrupte Datei, fehlende Datei
- State-Update nach Download: korrekte Felder

### Integration Tests
- Voller Workflow: Katalog laden → Status berechnen → Download → State aktualisieren
- Pack-Import: Korrekte Matching-Logik

### Edge Cases
- datRoot existiert nicht → graceful degradation
- dat-catalog-state.json korrupt → Reset auf leeren State
- Katalog-Eintrag ohne Url → ManualLogin Strategy
- Concurrent Downloads → State-File Locking

## Betroffene Dateien (geplant)

### Neue Dateien
- `src/Romulus.Contracts/Models/DatCatalogModels.cs`
- `src/Romulus.Infrastructure/Dat/DatCatalogStateService.cs`
- `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs`
- `src/Romulus.UI.Wpf/Views/DatCatalogView.xaml` + `.xaml.cs`

### Geaenderte Dateien
- `src/Romulus.Infrastructure/Dat/DatSourceService.cs` (Progress-Support)
- `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` (Navigation)
- `src/Romulus.UI.Wpf/Views/MainWindow.xaml` (Tab/Navigation-Eintrag)

### Test-Dateien
- `src/Romulus.Tests/DatCatalogStateServiceTests.cs`
- `src/Romulus.Tests/DatCatalogViewModelTests.cs` (optional, ViewModel-Unit-Tests)
