# Conversion Architecture – Romulus

**Stand:** 2026-03-21  
**Typ:** Technische Zielarchitektur  
**Modus:** SE: Architect  
**Basis:** CONVERSION_DOMAIN_AUDIT, historisches [CONVERSION_PRODUCT_MODEL.md](../../archive/completed/CONVERSION_PRODUCT_MODEL.md), CONVERSION_MATRIX, ADR-0007, ARCHITECTURE_MAP  
**Scope:** Komplette Conversion-Engine für 65 Systeme, 76 Conversion-Pfade, 5 Tools

---

## 1. Executive Design

### 1.1 Problem

Die aktuelle Conversion-Implementierung ist ein **monolithischer Adapter** (`FormatConverterAdapter`) mit einer hardcodierten Dictionary-Map (`DefaultBestFormats`), der:

- 22 von 65 Systemen abdeckt, 43 stillschweigend ignoriert
- keine mehrstufigen Pipelines modelliert (CSO→ISO→CHD braucht Zwischenschritte)
- keine ConversionPolicy kennt (Auto/ManualOnly/ArchiveOnly/None)
- keine SourceIntegrity-Klassifikation hat (Lossless/Lossy/Unknown)
- Tool-Auswahl per `switch`-Statement löst statt per Capability-Matching
- keinen ConversionPlan erzeugt, den der Nutzer vor Ausführung sehen kann
- keinen Conversion-Graphen kennt – alle Pfade sind implizite if/else-Ketten

### 1.2 Zielddesign

Eine **graphbasierte, datengesteuerte Conversion-Engine**, die:

1. **Aus Konfiguration liest** (consoles.json + conversion-rules.json), nicht aus Code
2. **Einen gerichteten Conversion-Graphen aufbaut** (Format A → Format B mit Kosten, Tools, Constraints)
3. **Daraus einen ConversionPlan berechnet** (kürzester sicherer Pfad von Source → Preferred Target)
4. **Jeden Schritt einzeln ausführt, verifiziert und auditiert**
5. **Blockierte Pfade explizit kennt** und dem Nutzer erklärt

### 1.3 Architektur-Entscheidungen

| # | Entscheidung | Begründung |
|---|-------------|------------|
| A-1 | **Graph-basiertes Routing** statt Dictionary-Lookup | Ermöglicht mehrstufige Ketten, alternative Pfade, Kosten-Optimierung |
| A-2 | **Konfigurationsgesteuert** statt hardcodiert | consoles.json bekommt `conversionPolicy`, conversion-registry.json definiert alle Edges |
| A-3 | **ConversionPlan als Zwischenprodukt** | Preview/DryRun kann den Plan zeigen, bevor Convert ausgeführt wird |
| A-4 | **Vertrag-orientiert (Contracts first)** | Alle neuen Typen in `Romulus.Contracts`, Engine in `Romulus.Core`, Adapter in `Romulus.Infrastructure` |
| A-5 | **Rückwärtskompatibel** | Bestehende `IFormatConverter`-Schnittstelle bleibt erhalten, neue Engine implementiert sie intern |
| A-6 | **Kein DI-Container-Lock-in** | Engine ist Constructor-Injectable, nicht framework-gekoppelt |

### 1.4 Layer-Zuordnung

```
Contracts/
├── Models/
│   ├── ConversionModels.cs          ← bestehend (erweitern)
│   ├── ConversionGraphModels.cs     ← NEU (Graph, Edge, Capability)
│   ├── ConversionPlanModels.cs      ← NEU (Plan, Step, Intermediate)
│   └── ConversionPolicyModels.cs    ← NEU (Policy, Safety, Integrity)
├── Ports/
│   ├── IFormatConverter.cs          ← bestehend (bleibt)
│   ├── IConversionPlanner.cs        ← NEU
│   ├── IConversionExecutor.cs       ← NEU
│   └── IConversionRegistry.cs       ← NEU

Core/
├── Conversion/
│   ├── ConversionGraph.cs           ← NEU (Graph-Aufbau + Pathfinding)
│   ├── ConversionPlanner.cs         ← NEU (Plan-Berechnung)
│   ├── SourceIntegrityClassifier.cs ← NEU (Lossless/Lossy/Unknown)
│   └── ConversionPolicyEvaluator.cs ← NEU (Policy-Check)

Infrastructure/
├── Conversion/
│   ├── FormatConverterAdapter.cs    ← bestehend (refactored, delegiert an Engine)
│   ├── ConversionExecutor.cs        ← NEU (Step-by-Step Execution)
│   ├── ConversionRegistryLoader.cs  ← NEU (lädt aus JSON)
│   └── ToolInvokers/
│       ├── ChdmanInvoker.cs         ← NEU (extrahiert aus FormatConverterAdapter)
│       ├── DolphinToolInvoker.cs    ← NEU
│       ├── SevenZipInvoker.cs       ← NEU
│       └── PsxtractInvoker.cs       ← NEU
```

---

## 2. Zielobjekte und Services

### 2.1 Domänenmodelle (Contracts/Models)

Alle Typen sind **immutable Records** oder **sealed records**. Keine Vererbungshierarchien.

#### ConversionPolicy

```csharp
/// <summary>
/// Systemweite Regel, ob und wie Conversion erlaubt ist.
/// Geladen aus consoles.json.conversionPolicy pro ConsoleKey.
/// </summary>
public enum ConversionPolicy
{
    /// Automatische Konvertierung in der Standard-Pipeline.
    Auto,

    /// Nur Archivierung (ROM → ZIP). Kein Inhaltseingriff.
    ArchiveOnly,

    /// Technisch möglich, aber riskant. Erfordert explizite Nutzerbestätigung.
    ManualOnly,

    /// Konvertierung ist bewusst blockiert (Set-basiert, encrypted, kein Zielformat).
    None
}
```

#### SourceIntegrity

```csharp
/// <summary>
/// Klassifiziert, ob die Quelldatei vollständige, unveränderte Rohdaten enthält.
/// </summary>
public enum SourceIntegrity
{
    /// Vollständige, unveränderte Rohdaten (cue/bin, iso, gdi, gcm, wbfs, gcz, wia).
    Lossless,

    /// Irreversibler Informationsverlust (nkit, cso padding-stripped, pbp re-encoded, cdi truncated).
    Lossy,

    /// Integrität kann nicht bestimmt werden.
    Unknown
}
```

#### ConversionSafety

```csharp
/// <summary>
/// Sicherheitsklassifikation eines konkreten Conversion-Pfads.
/// Berechnet aus SourceIntegrity + ConversionCapability + VerificationPolicy.
/// </summary>
public enum ConversionSafety
{
    /// Lossless, verifiable, reversible.
    Safe,

    /// Funktional korrekt, aber Quelle war bereits lossy.
    Acceptable,

    /// Ergebnis kann unvollständig oder inkompatibel sein.
    Risky,

    /// Nicht erlaubt (Policy=None, SetProtected, Unknown ConsoleKey).
    Blocked
}
```

#### ConversionCapability (Graph-Kante)

```csharp
/// <summary>
/// Beschreibt eine einzelne Konvertierungsfähigkeit:
/// "Von Format X nach Format Y, mit Tool Z, unter Bedingung W."
/// Dies ist eine Kante im Conversion-Graphen.
/// </summary>
public sealed record ConversionCapability
{
    /// Quellformat-Extension (z.B. ".cue", ".iso", ".cso", ".zip").
    public required string SourceExtension { get; init; }

    /// Zielformat-Extension (z.B. ".chd", ".rvz", ".zip", ".iso").
    public required string TargetExtension { get; init; }

    /// Welches Tool diese Konvertierung durchführt.
    public required ToolRequirement Tool { get; init; }

    /// Welchen Befehl das Tool ausführt (z.B. "createcd", "createdvd", "convert").
    public required string Command { get; init; }

    /// Für welche ConsoleKeys gilt diese Capability? Null = alle.
    public IReadOnlySet<string>? ApplicableConsoles { get; init; }

    /// SourceIntegrity-Anforderung. Null = keine Einschränkung.
    public SourceIntegrity? RequiredSourceIntegrity { get; init; }

    /// Ergebnis-SourceIntegrity nach dieser Konvertierung.
    public required SourceIntegrity ResultIntegrity { get; init; }

    /// Ist dieser Pfad verlustfrei?
    public required bool Lossless { get; init; }

    /// Geschätzte Kosten (0=trivial, 100=teuer). Für Pfad-Optimierung.
    public required int Cost { get; init; }

    /// Verification-Policy nach diesem Schritt.
    public required VerificationMethod Verification { get; init; }

    /// Beschreibung für UI/Reporting.
    public string? Description { get; init; }

    /// Optionale Bedingung (z.B. "fileSize < 700MB" für PS2 CD-Heuristik).
    public string? Condition { get; init; }
}
```

#### ToolRequirement

```csharp
/// <summary>
/// Beschreibt die Anforderung an ein externes Tool für eine ConversionCapability.
/// </summary>
public sealed record ToolRequirement
{
    /// Tool-Name (muss in IToolRunner.FindTool() auffindbar sein).
    public required string ToolName { get; init; }

    /// Erwarteter SHA256-Hash des Binaries (aus tool-hashes.json).
    public string? ExpectedHash { get; init; }

    /// Minimale Version (für Reporting, nicht erzwungen).
    public string? MinVersion { get; init; }
}
```

#### VerificationMethod

```csharp
/// <summary>
/// Definiert, wie nach einer Konvertierung verifiziert wird.
/// </summary>
public enum VerificationMethod
{
    /// chdman verify -i <file> (stark: SHA1 + Hunk-Integrität).
    ChdmanVerify,

    /// Magic-Byte-Check + Dateigrösse (schwach, aber verfügbar).
    RvzMagicByte,

    /// 7z t -y <file> (mittel: CRC32 pro Entry).
    SevenZipTest,

    /// Nur Dateiexistenz + Non-Zero-Size.
    FileExistenceCheck,

    /// Kein Verify verfügbar.
    None
}
```

#### ConversionStep (Plan-Element)

```csharp
/// <summary>
/// Ein einzelner Schritt in einem ConversionPlan.
/// </summary>
public sealed record ConversionStep
{
    /// 0-basierter Index im Plan.
    public required int Order { get; init; }

    /// Eingabeformat dieses Schritts.
    public required string InputExtension { get; init; }

    /// Ausgabeformat dieses Schritts.
    public required string OutputExtension { get; init; }

    /// Die Capability, die diesen Schritt ausführt.
    public required ConversionCapability Capability { get; init; }

    /// Ist dies ein Zwischenschritt (Output wird nach dem nächsten Schritt gelöscht)?
    public required bool IsIntermediate { get; init; }

    /// Erwarteter Pfad des Outputs (berechnet, nicht ausgeführt).
    public string? ExpectedOutputPath { get; init; }
}
```

#### ConversionPlan

```csharp
/// <summary>
/// Der vollständige Plan für die Konvertierung einer Datei.
/// Wird VOR der Ausführung berechnet und kann als Preview angezeigt werden.
/// </summary>
public sealed record ConversionPlan
{
    /// Quelldatei.
    public required string SourcePath { get; init; }

    /// ConsoleKey der Quelldatei.
    public required string ConsoleKey { get; init; }

    /// ConversionPolicy des Systems.
    public required ConversionPolicy Policy { get; init; }

    /// SourceIntegrity der Quelldatei.
    public required SourceIntegrity SourceIntegrity { get; init; }

    /// Safety-Klassifikation des gesamten Plans.
    public required ConversionSafety Safety { get; init; }

    /// Geordnete Liste der Konvertierungsschritte. Leer = keine Konvertierung.
    public required IReadOnlyList<ConversionStep> Steps { get; init; }

    /// Finales Zielformat (.chd, .rvz, .zip, etc.).
    public string? FinalTargetExtension => Steps.Count > 0 ? Steps[^1].OutputExtension : null;

    /// Begründung, wenn keine Konvertierung stattfindet (z.B. "policy-none", "already-target").
    public string? SkipReason { get; init; }

    /// Ob der Plan ausführbar ist.
    public bool IsExecutable => Steps.Count > 0 && Safety != ConversionSafety.Blocked;

    /// Ob der Plan Nutzerbestätigung braucht.
    public bool RequiresReview => Policy == ConversionPolicy.ManualOnly
                                || Safety == ConversionSafety.Risky
                                || SourceIntegrity == SourceIntegrity.Lossy;
}
```

#### ConversionResult (erweitert)

```csharp
/// <summary>
/// Ergebnis einer einzelnen Konvertierung (erweitert bestehenden Record).
/// </summary>
public sealed record ConversionResult(
    string SourcePath,
    string? TargetPath,
    ConversionOutcome Outcome,
    string? Reason = null,
    int ExitCode = 0)
{
    /// Der Plan, der ausgeführt wurde.
    public ConversionPlan? Plan { get; init; }

    /// SourceIntegrity der Quelldatei.
    public SourceIntegrity SourceIntegrity { get; init; }

    /// Safety-Klassifikation.
    public ConversionSafety Safety { get; init; }

    /// Verification-Status nach Konvertierung.
    public VerificationStatus VerificationResult { get; init; }

    /// Dauer der Konvertierung in Millisekunden.
    public long DurationMs { get; init; }
}

public enum VerificationStatus
{
    Verified,
    VerifyFailed,
    VerifyNotAvailable,
    NotAttempted
}

public enum ConversionOutcome
{
    Success,
    Skipped,
    Error,
    Blocked      // NEU: Explizit blockiert durch Policy/Safety
}
```

#### ConversionReport (Aggregat)

```csharp
/// <summary>
/// Zusammenfassung aller Konvertierungen eines Runs.
/// </summary>
public sealed record ConversionReport
{
    public required int TotalPlanned { get; init; }
    public required int Converted { get; init; }
    public required int Skipped { get; init; }
    public required int Errors { get; init; }
    public required int Blocked { get; init; }
    public required int RequiresReview { get; init; }
    public required long TotalSavedBytes { get; init; }
    public required IReadOnlyList<ConversionResult> Results { get; init; }

    /// Gruppiert nach Safety-Level.
    public IReadOnlyDictionary<ConversionSafety, int> BySafety =>
        Results.GroupBy(r => r.Safety)
               .ToDictionary(g => g.Key, g => g.Count());
}
```

### 2.2 Port-Interfaces (Contracts/Ports)

#### IConversionRegistry

```csharp
/// <summary>
/// Liefert Conversion-Capabilities und -Policies aus Konfiguration.
/// Schicht: Contracts (Port).
/// Implementierung: Infrastructure (ConversionRegistryLoader, liest JSON).
/// </summary>
public interface IConversionRegistry
{
    /// Alle registrierten Conversion-Capabilities (Graph-Kanten).
    IReadOnlyList<ConversionCapability> GetCapabilities();

    /// ConversionPolicy für ein System (aus consoles.json).
    ConversionPolicy GetPolicy(string consoleKey);

    /// Bevorzugtes Zielformat für ein System.
    /// Null wenn Policy=None oder kein Target definiert.
    string? GetPreferredTarget(string consoleKey);

    /// Alternative Zielformate (geordnet nach Präferenz).
    IReadOnlyList<string> GetAlternativeTargets(string consoleKey);
}
```

#### IConversionPlanner

```csharp
/// <summary>
/// Berechnet ConversionPlans für Dateien, ohne sie auszuführen.
/// Schicht: Contracts (Port).
/// Implementierung: Core (ConversionPlanner).
/// </summary>
public interface IConversionPlanner
{
    /// Berechnet den optimalen ConversionPlan für eine Datei.
    ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension);

    /// Batch-Planung für mehrere Dateien.
    IReadOnlyList<ConversionPlan> PlanBatch(
        IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates);
}
```

#### IConversionExecutor

```csharp
/// <summary>
/// Führt einen ConversionPlan Schritt für Schritt aus.
/// Schicht: Contracts (Port).
/// Implementierung: Infrastructure (ConversionExecutor).
/// </summary>
public interface IConversionExecutor
{
    /// Führt einen Plan aus. Gibt pro Schritt Feedback.
    ConversionResult Execute(
        ConversionPlan plan,
        Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ergebnis eines einzelnen Schritts.
/// </summary>
public sealed record ConversionStepResult(
    int StepOrder,
    string OutputPath,
    bool Success,
    VerificationStatus Verification,
    string? ErrorReason,
    long DurationMs);
```

### 2.3 Service-Zuordnung nach Layer

| Service | Layer | Abhängigkeiten | Verantwortung |
|---------|-------|----------------|---------------|
| **ConversionGraph** | Core | Contracts (ConversionCapability) | Graph-Aufbau, Pathfinding Quelle→Ziel |
| **ConversionPlanner** | Core | IConversionRegistry, ConversionGraph, SourceIntegrityClassifier, ConversionPolicyEvaluator | Plan-Berechnung ohne I/O |
| **SourceIntegrityClassifier** | Core | Contracts (SourceIntegrity) | Klassifiziert Extension + Dateiname in Lossless/Lossy/Unknown |
| **ConversionPolicyEvaluator** | Core | IConversionRegistry | Prüft Policy-Constraints, gibt Safety zurück |
| **ConversionRegistryLoader** | Infrastructure | JSON-Dateien, Contracts | Lädt Capabilities + Policies aus consoles.json / conversion-registry.json |
| **ConversionExecutor** | Infrastructure | IToolRunner, IFileSystem, IAuditStore | Führt Steps aus, verwaltet Temp-Dateien, auditiert |
| **ChdmanInvoker** | Infrastructure | IToolRunner | Kapselt chdman createcd/createdvd/verify |
| **DolphinToolInvoker** | Infrastructure | IToolRunner | Kapselt dolphintool convert |
| **SevenZipInvoker** | Infrastructure | IToolRunner | Kapselt 7z a/x/t |
| **PsxtractInvoker** | Infrastructure | IToolRunner | Kapselt psxtract pbp2chd |
| **FormatConverterAdapter** | Infrastructure | IConversionPlanner, IConversionExecutor | **Refactored:** Delegiert an Planner+Executor, implementiert weiterhin IFormatConverter |

---

## 3. Conversion Graph / Regelmodell

### 3.1 Graphstruktur

Der Conversion-Graph ist ein **gerichteter, gewichteter Graph** (kein DAG – Zyklen sind durch Policy blockiert, nicht strukturell):

```
Knoten = Format-Extensions (.cue, .bin, .iso, .chd, .rvz, .zip, .cso, .pbp, .gdi, .7z, .rar, ...)
Kanten = ConversionCapability (Tool + Command + Kosten + Constraints)
```

#### Beispiel-Graph (vereinfacht)

```
                          ┌─────────────────────────────────┐
                          │         .chd (Ziel CD/DVD)      │
                          └────────▲──────▲──────▲──────────┘
                                   │      │      │
                        ┌──────────┘      │      └──────────┐
                        │                 │                  │
                ┌───────┴──────┐  ┌───────┴──────┐  ┌───────┴──────┐
                │  .cue/.bin   │  │    .iso      │  │    .gdi      │
                │  (Lossless)  │  │  (Lossless)  │  │  (Lossless)  │
                └──────▲───────┘  └──────▲───────┘  └──────────────┘
                       │                 │
               ┌───────┴──────┐  ┌───────┴──────┐
               │  .pbp        │  │  .cso        │
               │  (Lossy)     │  │  (Lossy)     │
               │  psxtract    │  │  ciso        │
               └──────────────┘  └──────────────┘

                          ┌─────────────────────────────────┐
                          │         .rvz (Ziel GC/Wii)      │
                          └────────▲──────▲──────▲──────────┘
                                   │      │      │
                        ┌──────────┘      │      └──────────┐
                        │                 │                  │
                ┌───────┴──────┐  ┌───────┴──────┐  ┌───────┴──────┐
                │    .iso      │  │   .wbfs      │  │    .gcz      │
                │  (Lossless)  │  │  (Lossless)  │  │  (Lossless)  │
                └──────────────┘  └──────────────┘  └──────────────┘
                                                            ▲
                                                            │
                                                    ┌───────┴──────┐
                                                    │  .nkit.iso   │
                                                    │  (Lossy)     │
                                                    │  dolphintool │
                                                    └──────────────┘

                          ┌─────────────────────────────────┐
                          │         .zip (Ziel Cartridge)    │
                          └────────▲──────▲──────▲──────────┘
                                   │      │      │
                         lose ROMs │  .7z │  .rar│
                                   │  7z  │  7z  │
```

### 3.2 Pathfinding-Algorithmus

**Eingabe:** (ConsoleKey, SourceExtension, SourcePath)  
**Ausgabe:** `ConversionPlan` mit geordneten `ConversionStep`s

```
FUNKTION FindOptimalPath(consoleKey, sourceExt, sourcePath):

    1. policy = registry.GetPolicy(consoleKey)
       WENN policy == None → return BlockedPlan("policy-none")

    2. preferredTarget = registry.GetPreferredTarget(consoleKey)
       WENN preferredTarget == null → return BlockedPlan("no-target-defined")

    3. WENN sourceExt == preferredTarget → return SkippedPlan("already-target-format")

    4. sourceIntegrity = SourceIntegrityClassifier.Classify(sourceExt, sourcePath)

    5. capabilities = registry.GetCapabilities()
       Filtere nach: ConsoleKey-Match UND Conditions erfüllt

    6. graph = BuildWeightedGraph(capabilities)

    7. path = Dijkstra(graph, sourceExt, preferredTarget)
       WENN kein Pfad → alternativeTargets prüfen
       WENN kein alternativer Pfad → return SkippedPlan("no-conversion-path")

    8. safety = EvaluateSafety(policy, sourceIntegrity, path)
       WENN safety == Blocked → return BlockedPlan(reason)

    9. steps = path.Edges.Select((edge, i) => new ConversionStep(
           Order: i,
           InputExtension: edge.Source,
           OutputExtension: edge.Target,
           Capability: edge.Capability,
           IsIntermediate: i < path.Edges.Count - 1
       ))

   10. return new ConversionPlan(
           SourcePath: sourcePath,
           ConsoleKey: consoleKey,
           Policy: policy,
           SourceIntegrity: sourceIntegrity,
           Safety: safety,
           Steps: steps
       )
```

### 3.3 Kostenfunktion

Kosten pro Kante für Dijkstra:

| Eigenschaft | Kosten-Beitrag |
|-------------|---------------|
| Lossless + Strong Verify | 0 |
| Lossless + Weak Verify | +10 |
| Lossy Source | +50 |
| Zwischenschritt (Intermediate) | +20 |
| ManualOnly Tool | +100 |
| Tool nicht verfügbar | +∞ (Blockiert) |

### 3.4 Condition-Evaluation

Bestimmte Capabilities haben Conditions, die zur Laufzeit geprüft werden:

| Condition | Prüfung | Beispiel |
|-----------|---------|---------|
| `fileSize < 700MB` | `new FileInfo(sourcePath).Length < 700 * 1024 * 1024` | PS2 CD-Erkennung |
| `fileSize >= 700MB` | Gegenteil | PS2 DVD-Erkennung |
| `fileName.Contains(".nkit.")` | String-Check im Dateinamen | NKit-Lossy-Warnung |
| `extension == ".wad"` | Extension-Check | Wii WAD-Block |
| `extension == ".cdi"` | Extension-Check | Dreamcast CDI-Risiko |

Conditions werden als **benannte Prädikate** modelliert, nicht als Freitext-Strings:

```csharp
public enum ConversionCondition
{
    None,
    FileSizeLessThan700MB,     // PS2 CD-Heuristik
    FileSizeGreaterEqual700MB, // PS2 DVD-Heuristik
    IsNKitSource,              // .nkit.iso / .nkit.gcz
    IsWadFile,                 // Wii WAD (kein Disc-Image)
    IsCdiSource,               // Dreamcast CDI (truncated risk)
    IsEncryptedPbp             // Encrypted PBP → Block
}
```

### 3.5 Konfigurationsformat (conversion-registry.json)

```jsonc
{
  "schemaVersion": "conversion-registry-v1",
  "capabilities": [
    {
      "id": "cue-to-chd-cd",
      "sourceExtension": ".cue",
      "targetExtension": ".chd",
      "tool": { "toolName": "chdman", "minVersion": "0.262" },
      "command": "createcd",
      "lossless": true,
      "cost": 0,
      "verification": "ChdmanVerify",
      "resultIntegrity": "Lossless",
      "applicableConsoles": ["PS1","SAT","DC","SCD","PCECD","NEOCD","3DO","JAGCD","PCFX","CD32","CDI","FMTOWNS"],
      "description": "CUE/BIN → CHD (CD-based disc systems)"
    },
    {
      "id": "iso-to-chd-dvd",
      "sourceExtension": ".iso",
      "targetExtension": ".chd",
      "tool": { "toolName": "chdman" },
      "command": "createdvd",
      "lossless": true,
      "cost": 0,
      "verification": "ChdmanVerify",
      "resultIntegrity": "Lossless",
      "applicableConsoles": ["PS2","XBOX","X360"],
      "condition": "FileSizeGreaterEqual700MB",
      "description": "ISO → CHD (DVD-based systems, PS2 DVD)"
    },
    {
      "id": "iso-to-chd-cd-ps2",
      "sourceExtension": ".iso",
      "targetExtension": ".chd",
      "tool": { "toolName": "chdman" },
      "command": "createcd",
      "lossless": true,
      "cost": 0,
      "verification": "ChdmanVerify",
      "resultIntegrity": "Lossless",
      "applicableConsoles": ["PS2"],
      "condition": "FileSizeLessThan700MB",
      "description": "ISO → CHD (PS2 CD-based games <700MB)"
    },
    {
      "id": "cso-to-iso",
      "sourceExtension": ".cso",
      "targetExtension": ".iso",
      "tool": { "toolName": "ciso" },
      "command": "decompress",
      "lossless": false,
      "cost": 20,
      "verification": "FileExistenceCheck",
      "resultIntegrity": "Lossy",
      "requiredSourceIntegrity": null,
      "applicableConsoles": ["PSP"],
      "description": "CSO → ISO (decompression, intermediate step)"
    },
    {
      "id": "pbp-to-cuebin",
      "sourceExtension": ".pbp",
      "targetExtension": ".cue",
      "tool": { "toolName": "psxtract" },
      "command": "pbp2chd",
      "lossless": false,
      "cost": 20,
      "verification": "FileExistenceCheck",
      "resultIntegrity": "Lossy",
      "applicableConsoles": ["PS1","PSP"],
      "condition": null,
      "description": "PBP → CUE/BIN (psxtract extraction)"
    },
    {
      "id": "iso-to-rvz",
      "sourceExtension": ".iso",
      "targetExtension": ".rvz",
      "tool": { "toolName": "dolphintool" },
      "command": "convert",
      "lossless": true,
      "cost": 0,
      "verification": "RvzMagicByte",
      "resultIntegrity": "Lossless",
      "applicableConsoles": ["GC","WII"],
      "description": "ISO/GCM → RVZ (Dolphin native)"
    },
    {
      "id": "wbfs-to-rvz",
      "sourceExtension": ".wbfs",
      "targetExtension": ".rvz",
      "tool": { "toolName": "dolphintool" },
      "command": "convert",
      "lossless": true,
      "cost": 0,
      "verification": "RvzMagicByte",
      "resultIntegrity": "Lossless",
      "applicableConsoles": ["WII"],
      "description": "WBFS → RVZ (Wii format upgrade)"
    },
    {
      "id": "rom-to-zip",
      "sourceExtension": "*",
      "targetExtension": ".zip",
      "tool": { "toolName": "7z" },
      "command": "a -tzip",
      "lossless": true,
      "cost": 0,
      "verification": "SevenZipTest",
      "resultIntegrity": "Lossless",
      "description": "Any loose ROM → ZIP archive (ArchiveOnly systems)"
    }
    // ... weitere Capabilities
  ]
}
```

---

## 4. Tool-Modell

### 4.1 Architektur

Tool-Ausführung wird von `FormatConverterAdapter` in dedizierte **Invoker** extrahiert:

```
IToolRunner (Port, bestehend)
    │
    ├── FindTool(name) → path | null
    ├── InvokeProcess(path, args, label) → ToolResult
    └── Invoke7z(path, args) → ToolResult

IToolInvoker (NEU, internes Interface)
    │
    ├── CanHandle(ConversionCapability) → bool
    ├── Invoke(sourcePath, targetPath, capability, ct) → ToolInvocationResult
    └── Verify(targetPath) → VerificationStatus

ChdmanInvoker : IToolInvoker
    ├── Handles: .chd targets (createcd, createdvd)
    ├── Invoke: chdman {command} -i {source} -o {target}
    ├── Verify: chdman verify -i {target}
    └── Security: Zip-Slip guard bei Archiv-Extraktion

DolphinToolInvoker : IToolInvoker
    ├── Handles: .rvz targets (convert)
    ├── Invoke: dolphintool convert -i {source} -o {target} -f rvz -c zstd -l 5 -b 131072
    ├── Verify: Magic-Byte RVZ\x01 + size >4B
    └── Known Weakness: Kein echtes Verify-Kommando

SevenZipInvoker : IToolInvoker
    ├── Handles: .zip targets (a -tzip), extraction (.7z, .zip, .rar)
    ├── Invoke: 7z a -tzip -y {target} {source}
    ├── Verify: 7z t -y {target}
    └── Also: Extract-Support für Archiv→Disc-Conversion

PsxtractInvoker : IToolInvoker
    ├── Handles: .pbp → .cue/.bin (pbp2chd)
    ├── Invoke: psxtract pbp2chd -i {source} -o {target}
    ├── Verify: Nachgelagert (chdman verify auf CHD-End-Output)
    └── Risk: Encrypted PBPs → Error
```

### 4.2 ToolInvocationResult

```csharp
public sealed record ToolInvocationResult(
    bool Success,
    string? OutputPath,
    int ExitCode,
    string? StdOut,
    string? StdErr,
    long DurationMs,
    VerificationStatus Verification);
```

### 4.3 Tool-Dispatch

Der `ConversionExecutor` hält eine `IReadOnlyList<IToolInvoker>` und dispatched per Step:

```csharp
// Keine switch-Statements mehr. Dispatch per Capability-Matching:
var invoker = _invokers.FirstOrDefault(i => i.CanHandle(step.Capability))
    ?? throw new InvalidOperationException($"No invoker for {step.Capability.Tool.ToolName}");

var result = invoker.Invoke(inputPath, outputPath, step.Capability, ct);
```

### 4.4 Tool-Verfügbarkeits-Prüfung

Eingebettet in die Plan-Phase (nicht erst bei Execution):

```csharp
// Im ConversionPlanner:
foreach (var step in candidateSteps)
{
    var toolPath = toolRunner.FindTool(step.Capability.Tool.ToolName);
    if (toolPath is null)
    {
        return SkippedPlan($"tool-not-found:{step.Capability.Tool.ToolName}");
    }
}
```

---

## 5. Verification-Modell

### 5.1 Drei-Stufen-Verifikation

Jeder ConversionStep wird nach Ausführung verifiziert:

```
Stufe 1: Datei-Existenz + Non-Zero-Size
    │  FAIL → CleanupPartialOutput, Error
    │  PASS ↓
Stufe 2: Format-spezifische Verifikation (gemäss VerificationMethod)
    │  FAIL → CleanupPartialOutput, Error
    │  PASS ↓
Stufe 3: Optional: Plausibilitäts-Check (erwartete Grössenordnung)
    │  WARN → Proceed with Warning
    │  PASS ↓
[Step verified – Next Step oder Final]
```

### 5.2 Verifikation pro Format

| VerificationMethod | Tool-Kommando | Stärke | Output-Prüfung |
|-------------------|---------------|--------|----------------|
| **ChdmanVerify** | `chdman verify -i <file>` | Stark | SHA1 + Hunk-Integrität, Exit-Code 0 |
| **RvzMagicByte** | – (Code-basiert) | Schwach | Header = `RVZ\x01`, Size > 4 Bytes |
| **SevenZipTest** | `7z t -y <file>` | Mittel | CRC32 pro Entry, Exit-Code 0 |
| **FileExistenceCheck** | – (Code-basiert) | Minimal | Datei existiert, Size > 0 |
| **None** | – | Keine | Nur Datei-Existenz |

### 5.3 Verify-or-Die Invariante

```
NACH jedem Step:
    WENN Verify FAIL:
        1. Lösche Output dieses Steps
        2. Behalte ALLE vorherigen Outputs (inkl. Intermediates)
        3. ConversionResult = Error, Reason = "verify-failed:{step}"
        4. Audit: CONVERT_FAILED
        5. Source bleibt UNVERÄNDERT an Ort und Stelle

    WENN Verify PASS UND ist letzter Step:
        1. Lösche alle Intermediate-Dateien
        2. Verschiebe Source nach _TRASH_CONVERTED/
        3. ConversionResult = Success
        4. Audit: CONVERT
```

### 5.4 Intermediate-Cleanup

Bei mehrstufigen Ketten (z.B. CSO → ISO → CHD):

```
Step 0: CSO → ciso decompress → ISO (intermediate)
    Verify: FileExistenceCheck
    Intermediate=true → ISO bleibt temporär

Step 1: ISO → chdman createcd → CHD (final)
    Verify: ChdmanVerify
    Intermediate=false → CHD bleibt

Cleanup:
    WENN Step 1 verified:
        Lösche intermediate ISO
        Verschiebe CSO → Trash
    WENN Step 1 FAIL:
        Lösche CHD (partial)
        Behalte intermediate ISO (für Debug/Retry)
        Behalte CSO (unverändert)
```

---

## 6. Safety-Modell

### 6.1 Safety-Berechnung

Safety wird aus drei Inputs berechnet:

```
Safety = f(ConversionPolicy, SourceIntegrity, PathCharacteristics)
```

| Policy | SourceIntegrity | Path | → Safety |
|--------|----------------|------|----------|
| Auto | Lossless | Direct (1 Step) | **Safe** |
| Auto | Lossless | Multi-Step (Intermediates) | **Safe** |
| Auto | Lossy | Any | **Acceptable** (+ Warning) |
| ArchiveOnly | Lossless | → ZIP | **Safe** |
| ArchiveOnly | Unknown | → ZIP | **Safe** (ZIP ändert Inhalt nicht) |
| ManualOnly | Any | Any | **Risky** (erfordert Review) |
| None | Any | Any | **Blocked** |
| Any | Unknown | Disc Transform | **Blocked** |
| Any | Any | Tool nicht verfügbar | **Blocked** |

### 6.2 ConversionPolicyEvaluator (Core)

```csharp
public sealed class ConversionPolicyEvaluator
{
    /// Berechnet ConversionSafety aus Policy, Integrity und Pfad-Eigenschaften.
    public ConversionSafety EvaluateSafety(
        ConversionPolicy policy,
        SourceIntegrity integrity,
        IReadOnlyList<ConversionCapability> pathCapabilities,
        bool allToolsAvailable)
    {
        if (policy == ConversionPolicy.None) return ConversionSafety.Blocked;
        if (!allToolsAvailable) return ConversionSafety.Blocked;

        if (policy == ConversionPolicy.ManualOnly) return ConversionSafety.Risky;

        if (integrity == SourceIntegrity.Unknown &&
            pathCapabilities.Any(c => !c.Lossless))
            return ConversionSafety.Blocked;

        if (integrity == SourceIntegrity.Lossy)
            return ConversionSafety.Acceptable;

        return ConversionSafety.Safe;
    }
}
```

### 6.3 Set-Protection

ARCADE und NEOGEO erhalten ConversionPolicy=None in consoles.json. Zusätzlich:

```csharp
// Hardcoded Safety-Net im ConversionPolicyEvaluator:
private static readonly IReadOnlySet<string> SetProtectedSystems =
    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ARCADE", "NEOGEO" };

public ConversionPolicy GetEffectivePolicy(string consoleKey, ConversionPolicy configuredPolicy)
{
    // Set-Protection ist nicht konfigurierbar – immer None.
    if (SetProtectedSystems.Contains(consoleKey))
        return ConversionPolicy.None;

    return configuredPolicy;
}
```

### 6.4 Lossy-Source-Warnung

Wenn SourceIntegrity=Lossy, wird der ConversionPlan markiert:

```csharp
// Im ConversionPlan:
RequiresReview = true  // (weil SourceIntegrity == Lossy)

// Im ConversionResult:
Reason = "lossy-source:nkit"  // Spezifischer Grund
```

Die UI/CLI zeigt dann:

```
⚠ WARNING: Source is lossy (NKit). Converted output is NOT bit-identical
  to original disc image. DAT matching against Redump will not work.
  Continue? [y/N]
```

---

## 7. Orchestrator-Integration

### 7.1 Aktueller Zustand (Ist)

```
RunOrchestrator
    → WinnerConversionPipelinePhase
        → IFormatConverter.GetTargetFormat()
        → IFormatConverter.Convert()
        → IFormatConverter.Verify()
```

### 7.2 Zielzustand (Soll)

```
RunOrchestrator
    → ConversionPlanningPhase          ← NEU: Berechnet Pläne für alle Kandidaten
        → IConversionPlanner.PlanBatch()
        → Gibt IReadOnlyList<ConversionPlan> zurück
        → Filtert: isExecutable, requiresReview
    → ConversionExecutionPhase         ← NEU: Führt Pläne aus
        → IConversionExecutor.Execute(plan)
        → Audit pro Step
        → Intermediate Cleanup
        → Source → Trash
    → konsolidiert zu ConversionReport
```

### 7.3 Pipeline-Phasen (Refactored)

#### ConversionPlanningPhase

```csharp
public sealed class ConversionPlanningPhase
    : IPipelinePhase<ConversionPlanningInput, ConversionPlanningOutput>
{
    public string Name => "ConversionPlanning";

    public ConversionPlanningOutput Execute(
        ConversionPlanningInput input,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var planner = input.Planner;
        var candidates = input.Candidates
            .Select(c => (c.MainPath, c.ConsoleKey, c.Extension))
            .ToList();

        var plans = planner.PlanBatch(candidates);

        var executable = plans.Where(p => p.IsExecutable && !p.RequiresReview).ToList();
        var needsReview = plans.Where(p => p.IsExecutable && p.RequiresReview).ToList();
        var blocked = plans.Where(p => !p.IsExecutable).ToList();

        return new ConversionPlanningOutput(executable, needsReview, blocked);
    }
}

public sealed record ConversionPlanningInput(
    IReadOnlyList<RomCandidate> Candidates,
    IConversionPlanner Planner);

public sealed record ConversionPlanningOutput(
    IReadOnlyList<ConversionPlan> Executable,
    IReadOnlyList<ConversionPlan> RequiresReview,
    IReadOnlyList<ConversionPlan> Blocked);
```

#### ConversionExecutionPhase

```csharp
public sealed class ConversionExecutionPhase
    : IPipelinePhase<ConversionExecutionInput, ConversionExecutionOutput>
{
    public string Name => "ConversionExecution";

    public ConversionExecutionOutput Execute(
        ConversionExecutionInput input,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var results = new List<ConversionResult>();
        int converted = 0, errors = 0, skipped = 0, blocked = 0;

        foreach (var plan in input.Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!plan.IsExecutable)
            {
                blocked++;
                results.Add(new ConversionResult(plan.SourcePath, null, ConversionOutcome.Blocked,
                    plan.SkipReason) { Plan = plan, Safety = plan.Safety });
                continue;
            }

            var result = input.Executor.Execute(plan, onStepComplete: null, cancellationToken);
            results.Add(result);

            switch (result.Outcome)
            {
                case ConversionOutcome.Success:
                    converted++;
                    PipelinePhaseHelpers.AppendConversionAudit(context, input.Options,
                        result.SourcePath, result.TargetPath, plan.Steps[^1].Capability.Tool.ToolName);
                    PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, input.Options,
                        result.SourcePath, result.TargetPath);
                    break;
                case ConversionOutcome.Error:
                    errors++;
                    PipelinePhaseHelpers.AppendConversionErrorAudit(context, input.Options,
                        result.SourcePath, result.Reason);
                    break;
                default:
                    skipped++;
                    break;
            }

            // Progress
            if ((converted + errors + skipped + blocked) % 25 == 0)
                context.OnProgress?.Invoke($"Conversion: {converted} done, {errors} errors, {skipped} skipped");
        }

        return new ConversionExecutionOutput(
            new ConversionReport
            {
                TotalPlanned = input.Plans.Count,
                Converted = converted,
                Skipped = skipped,
                Errors = errors,
                Blocked = blocked,
                RequiresReview = 0,
                TotalSavedBytes = 0, // TODO: Calculate
                Results = results
            });
    }
}

public sealed record ConversionExecutionInput(
    IReadOnlyList<ConversionPlan> Plans,
    IConversionExecutor Executor,
    RunOptions Options);

public sealed record ConversionExecutionOutput(ConversionReport Report);
```

### 7.4 Orchestrator-Entscheidungsbaum

```
RunOrchestrator.Execute(options):
    │
    ├── options.ConvertFormat == null? → Skip Conversion
    │
    ├── options.ConvertOnly?
    │   └── Scan → ConversionPlanning → ConversionExecution → Report
    │
    └── Standard-Pipeline:
        Scan → Enrich → Dedupe → JunkRemove → Move → Sort
        → ConversionPlanning (nur Winners)
        → ConversionExecution (nur executable Plans)
        → Report
```

### 7.5 DryRun / Preview

Im Preview-Modus wird NUR die Planning-Phase ausgeführt:

```csharp
// DryRun: Nur planen, nicht ausführen
var planningOutput = planningPhase.Execute(input, context, ct);

// ConversionReport im DryRun enthält:
// - Wie viele Dateien konvertiert WÜRDEN
// - Welche Formate → welche Zielformate
// - Welche blockiert/reviewpflichtig sind
// - Geschätzte Platzersparnis
```

### 7.6 RunProjection-Erweiterung

```csharp
public sealed record RunProjection(
    // ... bestehende 25 Metriken ...
    int ConvertedCount,
    int ConvertErrorCount,
    int ConvertSkippedCount,
    int ConvertBlockedCount,       // NEU
    int ConvertReviewCount,        // NEU
    long ConvertSavedBytes         // NEU
);
```

### 7.7 Rückwärtskompatibilität

`FormatConverterAdapter` wird refactored, implementiert aber weiterhin `IFormatConverter`:

```csharp
public sealed class FormatConverterAdapter : IFormatConverter
{
    private readonly IConversionPlanner _planner;
    private readonly IConversionExecutor _executor;

    // Legacy-Interface-Methode: Delegiert intern an neue Engine
    public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
    {
        var plan = _planner.Plan("<virtual>", consoleKey, sourceExtension);
        if (plan.Steps.Count == 0) return null;

        var lastStep = plan.Steps[^1];
        return new ConversionTarget(
            lastStep.OutputExtension,
            lastStep.Capability.Tool.ToolName,
            lastStep.Capability.Command);
    }

    // Legacy-Interface-Methode: Erstellt Plan + führt aus
    public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var consoleKey = /* aus Kontext oder BestFormats-Reverse-Lookup */;
        var plan = _planner.Plan(sourcePath, consoleKey, ext);
        return _executor.Execute(plan, cancellationToken: ct);
    }

    public bool Verify(string targetPath, ConversionTarget target)
    {
        // Delegiert an den letzten Step's Invoker
        var invoker = _invokers.FirstOrDefault(i => i.CanHandle(/* capability von target */));
        return invoker?.Verify(targetPath) == VerificationStatus.Verified;
    }
}
```

---

## 8. Wo Conversion blockiert werden muss

### 8.1 Harte Blocks (nicht übersteuerbar)

| Block-Grund | Prüfstelle | Ergebnis |
|-------------|-----------|----------|
| ConsoleKey == UNKNOWN | ConversionPlanner | ConversionPlan.Safety = Blocked |
| ConsoleKey ∈ {ARCADE, NEOGEO} | ConversionPolicyEvaluator (hardcoded) | ConversionPolicy.None |
| ConversionPolicy == None (aus Config) | ConversionPlanner | ConversionPlan.Safety = Blocked |
| Tool nicht auffindbar (FindTool = null) | ConversionPlanner | ConversionPlan mit SkipReason |
| Tool-Hash stimmt nicht | ToolRunnerAdapter (bestehend) | ToolResult.Success = false |
| Kein Pfad im Graph von Source → Target | ConversionGraph.FindPath | ConversionPlan.Steps = leer |
| Zieldatei existiert bereits | ConversionExecutor | ConversionOutcome.Skipped |
| Quelldatei == Zielformat | ConversionPlanner | ConversionPlan.SkipReason = "already-target" |

### 8.2 Weiche Blocks (Nutzer kann übersteuern bei ManualOnly)

| Block-Grund | Prüfstelle | Ergebnis |
|-------------|-----------|----------|
| ConversionPolicy == ManualOnly | ConversionPlanner | ConversionPlan.RequiresReview = true |
| SourceIntegrity == Lossy | ConversionPlanner | ConversionPlan.RequiresReview = true |
| .cdi-Input (Dreamcast) | SourceIntegrityClassifier | SourceIntegrity = Lossy |
| .nkit.iso / .nkit.gcz | SourceIntegrityClassifier | SourceIntegrity = Lossy |
| Image > 8 GB | ConversionPlanner (Condition) | ConversionPlan.RequiresReview = true |

### 8.3 Block-Kommunikation

Jeder Block erzeugt einen `ConversionPlan` mit:
- `IsExecutable = false`
- `SkipReason = "policy-none:ARCADE"` (maschinenlesbar)
- `Safety = Blocked`

Die Entry Points zeigen dies konsistent:
- **CLI:** JSON-Output mit `"blocked": [...]` Array
- **GUI:** DataGrid-Zeile mit rotem Badge + Tooltip
- **API:** `ConversionReport.Blocked` Counter + Detail-Array

---

## 9. Risiken

### 9.1 Architekturrisiken

| # | Risiko | Impact | Mitigation |
|---|--------|--------|------------|
| R-1 | **Graph-Zyklen** | Endlosschleife im Pathfinding | Dijkstra auf DAG erzwingen: visited-Set, max Pfadlänge 5 |
| R-2 | **Condition-Explosion** | Zu viele Sonderfall-Conditions | Conditions als Enum, max 10 definierte Prädikate, kein Freitext |
| R-3 | **Registry-Drift** | JSON-Config divergiert vom Code | Schema-Validierung beim Laden, Integrationstests gegen alle 65 Systeme |
| R-4 | **Performance bei Batch-Planung** | Graph-Traversal × 10.000 Kandidaten | Graph wird einmal aufgebaut und gecached. Dijkstra ist O(E log V) mit E<100 Kanten |
| R-5 | **Rückwärtskompatibilitäts-Bruch** | Bestehende Tests erwarten altes Verhalten | IFormatConverter-Facade bleibt. Alle 5200+ Tests müssen grün bleiben |
| R-6 | **Intermediate-Datei-Leaks** | Temp-ISOs bleiben liegen bei Crash | Deterministische Temp-Pfade mit Guid, finally-Cleanup, Startup-Scan für Orphans |
| R-7 | **RVZ-Verify zu schwach** | Korrumpierte RVZ nicht erkannt | Kurzfristig: akzeptieren. Mittelfristig: dolphintool convert -f iso → /dev/null als Dry-Verify |

### 9.2 Implementierungsrisiken

| # | Risiko | Mitigation |
|---|--------|------------|
| I-1 | consoles.json-Schema-Erweiterung bricht bestehende Parser | Versioned Schema, optionale Felder mit Defaults |
| I-2 | Neues conversion-registry.json muss konsistent mit consoles.json sein | Cross-Validierung im Loader: jeder ConsoleKey in Capabilities muss in consoles.json existieren |
| I-3 | FormatConverterAdapter-Refactoring betrifft WinnerConversion + ConvertOnlyPipelinePhase | Facade-Pattern: alter IFormatConverter bleibt, delegiert intern |
| I-4 | Neue Pipeline-Phasen müssen in Orchestrator integriert werden | Schrittweise: erst Planning-Phase addieren, dann Execution-Phase ersetzen |

---

## 10. Empfohlene technische Zerlegung

### Phase 1: Foundation (Contracts + Core)

**Scope:** Neue Typen, Graph, Planner – alles ohne I/O

| # | Task | Layer | Dateien | Abhängigkeit |
|---|------|-------|---------|-------------|
| 1.1 | ConversionPolicy + SourceIntegrity + ConversionSafety Enums | Contracts | ConversionPolicyModels.cs | – |
| 1.2 | ConversionCapability + ToolRequirement + VerificationMethod Records | Contracts | ConversionGraphModels.cs | 1.1 |
| 1.3 | ConversionStep + ConversionPlan Records | Contracts | ConversionPlanModels.cs | 1.2 |
| 1.4 | ConversionCondition Enum + Evaluator | Contracts | ConversionPolicyModels.cs | – |
| 1.5 | IConversionRegistry + IConversionPlanner + IConversionExecutor Ports | Contracts | Ports/ | 1.1-1.3 |
| 1.6 | ConversionResult erweitern (Plan, Safety, Verification) | Contracts | ConversionModels.cs | 1.3 |
| 1.7 | SourceIntegrityClassifier (pure, extension-basiert) | Core | Conversion/SourceIntegrityClassifier.cs | 1.1 |
| 1.8 | ConversionPolicyEvaluator (pure) | Core | Conversion/ConversionPolicyEvaluator.cs | 1.1 |
| 1.9 | ConversionGraph (Aufbau + Dijkstra) | Core | Conversion/ConversionGraph.cs | 1.2 |
| 1.10 | ConversionPlanner (Plan-Berechnung) | Core | Conversion/ConversionPlanner.cs | 1.5-1.9 |

**Tests:** Unit-Tests für jeden Core-Typ. Besonders: Graph-Pathfinding, Policy-Evaluation, Integrity-Classification.

### Phase 2: Infrastructure (Registry + Invokers)

**Scope:** JSON-Laden, Tool-Invoker-Extraktion

| # | Task | Layer | Dateien | Abhängigkeit |
|---|------|-------|---------|-------------|
| 2.1 | conversion-registry.json erstellen (alle 76 Pfade) | Data | data/conversion-registry.json | 1.2 |
| 2.2 | consoles.json erweitern: `conversionPolicy` Feld | Data | data/consoles.json | 1.1 |
| 2.3 | ConversionRegistryLoader (liest JSON → IConversionRegistry) | Infrastructure | Conversion/ConversionRegistryLoader.cs | 2.1, 2.2 |
| 2.4 | ChdmanInvoker extrahieren | Infrastructure | Conversion/ToolInvokers/ChdmanInvoker.cs | – |
| 2.5 | DolphinToolInvoker extrahieren | Infrastructure | Conversion/ToolInvokers/DolphinToolInvoker.cs | – |
| 2.6 | SevenZipInvoker extrahieren | Infrastructure | Conversion/ToolInvokers/SevenZipInvoker.cs | – |
| 2.7 | PsxtractInvoker extrahieren | Infrastructure | Conversion/ToolInvokers/PsxtractInvoker.cs | – |
| 2.8 | ConversionExecutor (Schritt-für-Schritt-Ausführung) | Infrastructure | Conversion/ConversionExecutor.cs | 2.4-2.7 |

**Tests:** Integrationstests mit Mocked IToolRunner. Registry-Loader gegen Schema validieren. Invoker-Tests mit Fake-Tools.

### Phase 3: Pipeline-Integration

**Scope:** Orchestrator-Anbindung, Rückwärtskompatibilität

| # | Task | Layer | Dateien | Abhängigkeit |
|---|------|-------|---------|-------------|
| 3.1 | ConversionPlanningPhase | Infrastructure | Orchestration/ConversionPlanningPhase.cs | 1.10, 2.3 |
| 3.2 | ConversionExecutionPhase | Infrastructure | Orchestration/ConversionExecutionPhase.cs | 2.8 |
| 3.3 | FormatConverterAdapter refactoren (Facade → delegiert) | Infrastructure | Conversion/FormatConverterAdapter.cs | 3.1, 3.2 |
| 3.4 | WinnerConversionPipelinePhase anpassen (nutzt neue Phasen) | Infrastructure | Orchestration/ | 3.1, 3.2 |
| 3.5 | ConvertOnlyPipelinePhase anpassen | Infrastructure | Orchestration/ | 3.1, 3.2 |
| 3.6 | RunProjection erweitern (BlockedCount, ReviewCount, SavedBytes) | Infrastructure | Orchestration/RunProjection.cs | 1.6 |
| 3.7 | ARCADE/NEOGEO aus DefaultBestFormats entfernen (BUG-FIX) | Infrastructure | Conversion/FormatConverterAdapter.cs | – |

**Tests:** Full Pipeline-Tests. Preview/DryRun zeigt ConversionPlans. 5200+ bestehende Tests grün.

### Phase 4: Entry-Point-Anbindung

**Scope:** CLI, GUI, API nutzen neue Conversion-Transparenz

| # | Task | Layer | Dateien | Abhängigkeit |
|---|------|-------|---------|-------------|
| 4.1 | CLI: `--convert-format auto` zeigt ConversionPlan im DryRun-JSON | CLI | Program.cs, CliOutputWriter.cs | 3.6 |
| 4.2 | GUI: Conversion-Preview-Panel (DataGrid mit Plänen) | GUI | ViewModels/, Views/ | 3.6 |
| 4.3 | GUI: ManualOnly-Confirmation-Dialog | GUI | Views/ | 3.1 |
| 4.4 | API: `/runs/{id}/conversion-plan` Endpoint | API | Program.cs | 3.6 |
| 4.5 | Reports: ConversionReport in HTML/CSV integrieren | Infrastructure | Reporting/ | 3.2 |

---

## Anhang A: Systemdiagramm

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          ENTRY POINTS                                    │
│  CLI              GUI (WPF)           API (REST)                        │
│  --convert auto   [Convert Tab]       POST /runs {convertFormat:auto}   │
└──────────┬──────────────┬─────────────────┬─────────────────────────────┘
           │              │                 │
           ▼              ▼                 ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      INFRASTRUCTURE                                      │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐ │
│  │  RunOrchestrator                                                    │ │
│  │    → Scan → Enrich → Dedupe → JunkRemove → Move → Sort            │ │
│  │    → ConversionPlanningPhase ─────────┐                            │ │
│  │    → ConversionExecutionPhase ────────┤                            │ │
│  │    → Report → AuditSeal               │                            │ │
│  └───────────────────────────────────────┤────────────────────────────┘ │
│                                          │                               │
│  ┌───────────────────┐    ┌──────────────▼──────────────────┐           │
│  │ ConversionRegistry │    │ ConversionExecutor              │           │
│  │ Loader (JSON)      │    │  ├── ChdmanInvoker              │           │
│  │  ├─ consoles.json  │    │  ├── DolphinToolInvoker         │           │
│  │  ├─ conversion-    │    │  ├── SevenZipInvoker            │           │
│  │  │  registry.json  │    │  └── PsxtractInvoker            │           │
│  │  └─ tool-hashes    │    │                                 │           │
│  └────────┬───────────┘    └──────────────┬──────────────────┘           │
│           │                               │                              │
│           ▼                               ▼                              │
│  ┌────────────────────┐    ┌──────────────────────────────────┐          │
│  │  IToolRunner       │    │  IFileSystem + IAuditStore       │          │
│  │  (Hash-Verify,     │    │  (Move, Trash, Audit-CSV)        │          │
│  │   Process Exec)    │    │                                  │          │
│  └────────────────────┘    └──────────────────────────────────┘          │
└──────────────────────────────────────────────────────────────────────────┘
           │                               │
           ▼                               │
┌──────────────────────────────────────────┤───────────────────────────────┐
│                       CORE                │                              │
│                                          │                              │
│  ┌────────────────────────────────────┐  │                              │
│  │  ConversionPlanner                 │  │                              │
│  │    → ConversionGraph (Dijkstra)    │  │                              │
│  │    → SourceIntegrityClassifier     │◄─┘                              │
│  │    → ConversionPolicyEvaluator     │                                 │
│  └────────────────────────────────────┘                                 │
│                                                                          │
│  FormatScorer, VersionScorer, GameKeyNormalizer, RegionDetector, ...    │
└──────────────────────────────────────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      CONTRACTS                                           │
│                                                                          │
│  ConversionPolicy, SourceIntegrity, ConversionSafety                    │
│  ConversionCapability, ConversionStep, ConversionPlan                   │
│  ConversionResult, ConversionReport, ConversionCondition                │
│  ToolRequirement, VerificationMethod, VerificationStatus                │
│  IConversionRegistry, IConversionPlanner, IConversionExecutor           │
│  IFormatConverter (bestehend, Facade)                                   │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Anhang B: ADR-Referenz

Diese Architektur ist kompatibel mit:

| ADR | Relevanz |
|-----|----------|
| ADR-0002 (Ports/Services) | Alle neuen Interfaces = Ports in Contracts |
| ADR-0004 (Clean Architecture) | Dependency-Richtung: Entry Points → Infrastructure → Core → Contracts |
| ADR-0007 (Final Core Functions) | FormatConverterAdapter wird weiterhin als IFormatConverter exposed |
| **NEU: ADR-CONV-001** | Conversion-Graph-Architektur (dieses Dokument) |

---

## Anhang C: Migrations-Checkliste

| # | Schritt | Test-Kriterium | Breaking? |
|---|---------|---------------|-----------|
| 1 | Neue Enums + Records in Contracts | Kompiliert, keine bestehenden Tests betroffen | Nein |
| 2 | Core-Services (Graph, Planner, Evaluator) | Unit-Tests für alle Pfade grün | Nein |
| 3 | conversion-registry.json + consoles.json Erweiterung | Schema-Validierungstest + 65 Systeme geprüft | Nein (optionale Felder) |
| 4 | Tool-Invoker extrahiert | Alle FormatConverterAdapter-Tests grün (gleiche Ergebnisse) | Nein (internes Refactoring) |
| 5 | ConversionExecutor implementiert | Neue Tests + alte Tests grün via Facade | Nein |
| 6 | Neue Pipeline-Phasen + Orchestrator-Integration | Full-Pipeline-Tests, 5200+ bestehende Tests grün | Nein (alter Pfad per Facade) |
| 7 | ARCADE/NEOGEO Bug-Fix | Regressionstests: Arcade-ZIP wird NICHT konvertiert | Verhaltens-Änderung (Bug-Fix) |
| 8 | RunProjection-Erweiterung | CLI/API/GUI zeigen neue Metriken | Additive Erweiterung |
