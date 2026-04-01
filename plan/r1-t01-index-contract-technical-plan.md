# R1-T01 Index Contract Technical Plan

Stand: 2026-04-01

Ziel: Den minimalen, stabilen Vertragsrahmen fuer persistente Collection-States, Hash-Cache und Run-Historie festziehen, ohne bestehende fachliche Wahrheiten zu duplizieren oder bestehende Laufzeitpfade still zu brechen.

## Scope

- [x] V1-Datenmodell fuer Collection-Entries definieren
- [x] V1-Datenmodell fuer Hash-Cache definieren
- [x] V1-Datenmodell fuer persistierte Run-Snapshots definieren
- [x] Port fuer Index-Zugriff in `RomCleanup.Contracts.Ports` definieren
- [x] Technische Invarianten fuer Pfade, UTC-Werte und Hash-Casing festziehen
- [x] Testmatrix fuer spaetere Implementierung festziehen

## Nicht-Scope

- Keine LiteDB-Implementierung
- Keine DI-Verdrahtung
- Keine Scanner- oder RunOrchestrator-Integration
- Keine GUI/API/CLI-Anpassungen
- Keine Entfernung bestehender Legacy-Modelle in diesem Schritt

## Repo-Befunde, die der Vertrag beruecksichtigen muss

- [ ] `src/RomCleanup.Contracts/Models/AdvancedModels.cs` enthaelt bereits `RunHistoryEntry` und `ScanIndexEntry`, aber beide Modelle sind fuer C1 zu schwach und semantisch nicht passend.
- [ ] `src/RomCleanup.Infrastructure/Hashing/FileHashService.cs` persistiert heute bereits einen produktiven JSON-Hash-Cache (`file-hashes-v1.json`) auf Basis von `path + hashType + lastWriteUtc + length`.
- [ ] `src/RomCleanup.Infrastructure/Hashing/FileHashService.cs` nutzt heute `LocalApplicationData`, waehrend andere Artefakte im Projekt meist unter `%APPDATA%\\RomCleanupRegionDedupe\\` liegen. Der Contract darf deshalb keinen impliziten Speicherort fest verdrahten.
- [ ] `src/RomCleanup.Api/RunLifecycleManager.cs` haelt Run-Historie aktuell nur in-memory; das ist Live-Run-Zustand, nicht persistente Collection-Historie.
- [ ] `src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs` lebt aktuell in `Infrastructure`; ein Contract darf deshalb nicht direkt von `RunProjection` abhaengen.
- [ ] `src/RomCleanup.Infrastructure/Analysis/IntegrityService.cs` und `src/RomCleanup.UI.Wpf/Services/FeatureService.Security.cs` zeigen bereits doppelte History-/Trend-Logik. Der neue Vertrag darf diese Lage nicht weiter verschlechtern.
- [ ] `src/RomCleanup.Tests/HygieneCleanupRegressionTests.cs` blockiert bewusst Typnamen wie `RunHistoryService` und `ScanIndexService`. Neue Komponenten duerfen diese Namen nicht wieder einfuehren.

## Entscheidungen

- [x] Neue Vertragsdatei statt Wiederverwendung von `AdvancedModels.cs`
- [x] Neuer Port `ICollectionIndex` statt lose Einzelservices
- [x] Bestehende `RunHistoryEntry`- und `ScanIndexEntry`-Typen in T01 unangetastet lassen
- [x] Persistierte Run-Snapshots nur aus bereits berechneten Run-Ergebnissen ableiten, nicht neu berechnen
- [x] Hashwerte im Contract standardisiert als lowercase hex behandeln
- [x] Alle Zeitstempel im Contract explizit als UTC behandeln
- [x] Alle Pfade im Contract als vollqualifizierte, normalisierte Windows-Pfade behandeln

## Geplante Dateien

- [x] `src/RomCleanup.Contracts/Models/CollectionIndexModels.cs`
- [x] `src/RomCleanup.Contracts/Ports/ICollectionIndex.cs`
- [x] `src/RomCleanup.Tests/CollectionIndexContractTests.cs`

## Namens- und Strukturentscheidungen

- [x] Keine neuen Typen namens `RunHistoryService`
- [x] Keine neuen Typen namens `ScanIndexService`
- [x] Kein Reuse des Namens `RunHistoryEntry` fuer die neue persistente Historie
- [x] Kein Reuse des Namens `ScanIndexEntry` fuer den neuen Collection-State

Empfohlene neue Typnamen:

- [x] `CollectionIndexMetadata`
- [x] `CollectionIndexEntry`
- [x] `CollectionHashCacheEntry`
- [x] `CollectionRunSnapshot`
- [x] `ICollectionIndex`

## Vertragsinvarianten

- [x] `Path` und `Root` sind absolute, normalisierte Pfade
- [x] `Path` liegt immer innerhalb von `Root`
- [x] `Extension` enthaelt immer den fuehrenden Punkt oder ist leer
- [x] `PrimaryHashType` und `Algorithm` sind kanonisch uppercase (`SHA1`, `SHA256`, `MD5`, `CRC32`)
- [x] `PrimaryHash` und `Hash` sind lowercase hex oder `null`
- [x] `LastWriteUtc`, `LastScannedUtc`, `StartedUtc`, `CompletedUtc`, `CreatedUtc`, `UpdatedUtc` sind UTC
- [x] Persistierte Run-Status verwenden die bestehenden `RunConstants`-Statuswerte
- [x] Der Contract enthaelt keine I/O-, JSON-, LiteDB- oder Dateisystemtypen

## V1 Vertragsoberflaeche

```csharp
namespace RomCleanup.Contracts.Ports;

using RomCleanup.Contracts.Models;

public interface ICollectionIndex
{
    ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default);

    ValueTask<int> CountEntriesAsync(CancellationToken ct = default);

    ValueTask<CollectionIndexEntry?> TryGetByPathAsync(
        string path,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(
        string consoleKey,
        CancellationToken ct = default);

    ValueTask UpsertEntriesAsync(
        IReadOnlyList<CollectionIndexEntry> entries,
        CancellationToken ct = default);

    ValueTask RemovePathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);

    ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(
        string path,
        string algorithm,
        long sizeBytes,
        DateTime lastWriteUtc,
        CancellationToken ct = default);

    ValueTask SetHashAsync(
        CollectionHashCacheEntry entry,
        CancellationToken ct = default);

    ValueTask AppendRunSnapshotAsync(
        CollectionRunSnapshot snapshot,
        CancellationToken ct = default);

    ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(
        int limit = 50,
        CancellationToken ct = default);
}
```

## V1 Datenmodelle

```csharp
namespace RomCleanup.Contracts.Models;

public sealed record CollectionIndexMetadata
{
    public int SchemaVersion { get; init; } = 1;
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed record CollectionIndexEntry
{
    public string Path { get; init; } = "";
    public string Root { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public DateTime LastScannedUtc { get; init; }
    public string PrimaryHashType { get; init; } = "SHA1";
    public string? PrimaryHash { get; init; }
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public string GameKey { get; init; } = "";
    public string Region { get; init; } = "UNKNOWN";
    public FileCategory Category { get; init; } = FileCategory.Game;
    public bool DatMatch { get; init; }
    public string? DatGameName { get; init; }
    public DatAuditStatus DatAuditStatus { get; init; } = DatAuditStatus.Unknown;
    public SortDecision SortDecision { get; init; } = SortDecision.Blocked;
    public DecisionClass DecisionClass { get; init; } = DecisionClass.Unknown;
    public EvidenceTier EvidenceTier { get; init; } = EvidenceTier.Tier4_Unknown;
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;
    public int DetectionConfidence { get; init; }
    public bool DetectionConflict { get; init; }
    public string ClassificationReasonCode { get; init; } = "game-default";
    public int ClassificationConfidence { get; init; } = 100;
}

public sealed record CollectionHashCacheEntry
{
    public string Path { get; init; } = "";
    public string Algorithm { get; init; } = "SHA1";
    public long SizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public string Hash { get; init; } = "";
    public DateTime RecordedUtc { get; init; }
}

public sealed record CollectionRunSnapshot
{
    public string RunId { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public string Mode { get; init; } = RunConstants.ModeDryRun;
    public string Status { get; init; } = RunConstants.StatusOk;
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public string RootFingerprint { get; init; } = "";
    public long DurationMs { get; init; }
    public int TotalFiles { get; init; }
    public int Games { get; init; }
    public int Dupes { get; init; }
    public int Junk { get; init; }
    public int DatMatches { get; init; }
    public int ConvertedCount { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int HealthScore { get; init; }
}
```

## Begruendung der Modellwahl

- [x] `CollectionIndexEntry` ist bewusst ein stabiler Snapshot und nicht einfach `RomCandidate` 1:1, damit Storage-Schema und Laufzeitmodell nicht unkontrolliert gekoppelt werden.
- [x] `PrimaryHash` bleibt im Entry erhalten, obwohl ein separater Hash-Cache existiert. Der Entry speichert den fachlich zuletzt verwendeten Primary Hash; der Cache bleibt eine technische Wiederverwendungsstruktur.
- [x] `CollectionRunSnapshot` speichert nur Kennzahlen, die spaeter fuer Trend, Diff und Storage-Analysen benoetigt werden.
- [x] `CollectionRunSnapshot` darf spaeter nur aus der bestehenden `RunProjection` oder direkt aus `RunResult` via bestehender Projektionslogik befuellt werden.

## Abgrenzungen zu bestehendem Code

- [x] `RunLifecycleManager` bleibt fuer aktive und kuerzlich abgeschlossene API-Runs zustaendig
- [x] `CollectionRunSnapshot` ist keine Ersetzung fuer `RunRecord`
- [x] `FileHashService` bleibt in T01 unangetastet
- [x] Der Vertrag bleibt speicherort-agnostisch; `%APPDATA%`, `LocalApplicationData` oder Portable Mode werden erst in T02/T03 auf Adapter-Ebene entschieden
- [x] Der bestehende JSON-Hash-Cache wird in T01 nicht verschoben oder geloescht
- [x] `AdvancedModels.cs` bleibt in T01 unveraendert, um unnoetige Seiteneffekte zu vermeiden

## Offene Architekturentscheidungen fuer T02/T03

- [ ] Ob `LiteDbCollectionIndex` den bestehenden JSON-Hash-Cache einmalig importiert oder zunaechst parallel weiterverwendet
- [ ] Ob `RunProjection` spaeter nach `Contracts` verschoben oder als Mapping-Quelle in `Infrastructure` belassen wird
- [ ] Ob geloeschte Dateien physisch aus dem Index entfernt oder ueber einen spaeteren Tombstone-Mechanismus behandelt werden
- [ ] Ob `ListByConsoleAsync` fuer grosse Sammlungen spaeter pagingfaehig erweitert werden muss

## Umsetzungsschritte fuer T01

- [x] Neue Datei `CollectionIndexModels.cs` anlegen
- [x] Neue Port-Datei `ICollectionIndex.cs` anlegen
- [x] XML-Dokumentation fuer Invarianten direkt an Modellen und Port ergraenzen
- [x] Vorhandene Alt-Typen in `AdvancedModels.cs` bewusst unberuehrt lassen
- [x] `r1-foundation-execution.md` auf den Detailplan verlinken
- [x] Vertragstest-Datei fuer Defaultwerte, UTC- und Serialisierungsannahmen vorbereiten

## Testmatrix fuer die spaetere Implementierung

### Contract-Tests

- [x] `CollectionIndexMetadata_DefaultSchemaVersion_IsOne`
- [x] `CollectionIndexEntry_Defaults_AreDeterministic`
- [x] `CollectionHashCacheEntry_Defaults_AreDeterministic`
- [x] `CollectionRunSnapshot_Defaults_UseRunConstants`
- [x] `CollectionIndexModels_SystemTextJson_RoundTrip_PreservesFields`

### Negative / Edge Tests

- [ ] Leerpfade werden durch spaetere Adaptervalidierung abgefangen
- [ ] Nicht-UTC-Werte werden durch Adapter normalisiert oder geblockt
- [ ] Hashwerte mit falschem Casing werden normalisiert
- [ ] Leere oder ungueltige Algorithmen werden nicht akzeptiert

### Mapping-Tests fuer spaetere Tickets

- [ ] `RunProjection` -> `CollectionRunSnapshot` ohne KPI-Verlust fuer V1-Felder
- [ ] `RomCandidate` -> `CollectionIndexEntry` ohne Verlust der benoetigten Sorting-/Recognition-Felder
- [ ] JSON-Hash-Cache-Fingerprint -> `CollectionHashCacheEntry` ohne Bedeutungswechsel

## Fertig, wenn

- [x] V1-Port und V1-Modelle dokumentiert und benannt sind
- [x] Keine Kollision mit bestehenden Alt-Typen oder Hygiene-Tests entsteht
- [x] Die Contract-Invarianten fuer Pfade, UTC und Hash-Casing explizit festgeschrieben sind
- [x] Der Vertrag T02 und T03 ermoeglicht, ohne heute schon Infrastruktur festzuschreiben
