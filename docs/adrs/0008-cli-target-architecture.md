# ADR-008: Zielarchitektur CLI

> **Status:** Proposed  
> **Datum:** 2026-03-18  
> **Kontext:** FULL_TOOL_AUDIT_2026-03-18.md, CliRedPhaseTests.cs (11 Failing Tests), ADR-005 (Core-Zielarchitektur)  
> **Scope:** CLI Entry Point — Program, Commands, Parser, Validation, Output, Exit Codes, Projection-Nutzung  
> **Betroffene Datei (IST):** `src/RomCleanup.CLI/Program.cs` (~850 Zeilen, 14 Verantwortlichkeiten)

---

## 1. Executive Design

### Hauptidee

**Die CLI ist ein dünner Adapter, der exakt drei Dinge tut: Argumente parsen, den Orchestrator delegieren, das Ergebnis formatiert ausgeben.**

Alles, was zwischen Parsing und Output liegt — Settings-Merging, Tool-Setup, DAT-Initialisierung, Pfad-Auflösung, Console-Mapping — gehört nicht in die CLI, sondern in Infrastructure-Services, die alle drei Entry Points (CLI, API, WPF) identisch nutzen.

### Ist-Zustand: 14 Verantwortlichkeiten in einer Datei

```
Program.cs (850 Zeilen)
├── Argument Parsing         ← gehört hierher
├── Settings Loading         ← NICHT CLI-Zuständigkeit
├── Data Directory Resolution← NICHT CLI-Zuständigkeit
├── Logging Initialization   ← NICHT CLI-Zuständigkeit
├── Tool Hash Verification   ← NICHT CLI-Zuständigkeit
├── DAT Setup                ← NICHT CLI-Zuständigkeit
├── Format Converter Setup   ← NICHT CLI-Zuständigkeit
├── Console Detection        ← NICHT CLI-Zuständigkeit
├── Console-to-DAT Mapping   ← NICHT CLI-Zuständigkeit
├── RunOptions Construction  ← teilweise CLI (Mapping CliOptions→RunOptions)
├── Orchestration Delegation ← gehört hierher (1 Zeile)
├── Result Projection        ← gehört hierher (1 Zeile)
├── Output Formatting        ← gehört hierher
└── Exit Code Resolution     ← gehört hierher
```

### Soll-Zustand: 5 Verantwortlichkeiten, 4 Dateien

```
src/RomCleanup.CLI/
├── Program.cs              ← Entry Point: Main + Ctrl+C + top-level error handling
├── CliArgsParser.cs        ← Pure Parsing: string[] → CliParseResult (value object)
├── CliOptionsMapper.cs     ← CliParseResult + Settings → RunOptions
└── CliOutputWriter.cs      ← RunProjection + RunResult → stdout/stderr
```

### Wichtigste Architekturentscheidungen

| # | Entscheidung | Begründung |
|---|-------------|------------|
| C-01 | **Parser ist pure Funktion, keine Seiteneffekte** | `Parse(string[]) → CliParseResult` — kein Console.Write, kein File.Exists. Eliminiert alle 11 Red-Phase-Bugs, da Validation-Regeln isoliert testbar werden. |
| C-02 | **Exit Codes werden aus `RunProjection.ExitCode` + `CliParseResult.ExitCode` abgeleitet, nie selbst berechnet** | CLI erfindet keine eigenen Codes. Mapping ist ein einziger `switch`-Ausdruck. |
| C-03 | **CLI liest nur `RunProjection`, nie `RunResult`-Interna** | DryRun JSON wird aus `RunProjection` serialisiert (nicht aus anonymem Objekt mit manuellen Feldzuweisungen). Eliminiert stille Feldverluste bei Projection-Erweiterungen. |
| C-04 | **`RunEnvironmentBuilder` in Infrastructure** | Settings-Laden, Tool-Setup, DAT-Index, ConsoleDetector — alles in einem shared Service, den CLI/API/WPF identisch nutzen. Program.cs enthält null Setup-Logik. |
| C-05 | **Wert-tragende Flags validieren strict** | Jedes Flag mit erwartetem Wert (`--prefer`, `--extensions`, `--report` etc.) MUSS bei fehlendem Wert mit Exit Code 3 abbrechen. Flags als Werte (`--prefer --mode`) werden explizit zurückgewiesen. |
| C-06 | **DryRun JSON = `JsonSerializer.Serialize(projection)`, nicht anonymes Objekt** | Wenn `RunProjection` ein neues Feld bekommt, ist es automatisch im CLI JSON. Kein manuelles Field-Mapping mehr. |

### Verantwortungsgrenzen (nicht verhandelbar)

| Grenze | CLI darf | CLI darf NICHT |
|--------|----------|---------------|
| Parsing → Validation | Syntaktische Prüfung (Flag-Format, Typ-Konvertierung) | Semantische Prüfung (existiert Root? Ist Hash-Typ gültig?) |
| Validation → Execution | Root-Existenz prüfen, System-Pfade blocken | DAT-Index laden, ConsoleDetector initialisieren |
| Execution → Output | `RunProjection` lesen, formatiert ausgeben | `RunResult.AllCandidates` direkt traversieren |
| Output → Sidecar | stderr-Pfade zu Audit/Report ausgeben | Audit-/Report-Dateien selbst schreiben |

---

## 2. Zielobjekte und Services

### 2.1 `CliParseResult` — Value Object (in CLI-Projekt)

```
CliParseResult
├── Command: CliCommand        // Run | Help | Version
├── ExitCode: int              // 0=ok, 3=validation error
├── Errors: string[]           // Fehlermeldungen für stderr
├── Options: CliRunOptions?    // nur bei Command=Run
```

```
CliRunOptions                  // immutable record
├── Roots: string[]
├── Mode: string               // "DryRun" | "Move"
├── PreferRegions: string[]
├── Extensions: string[]?      // null = nicht explizit gesetzt
├── ExtensionsExplicit: bool
├── TrashRoot: string?
├── RemoveJunk: bool
├── OnlyGames: bool
├── KeepUnknownWhenOnlyGames: bool
├── AggressiveJunk: bool
├── SortConsole: bool
├── EnableDat: bool
├── DatRoot: string?
├── HashType: string?
├── ConvertFormat: bool
├── ReportPath: string?
├── AuditPath: string?
├── LogPath: string?
├── LogLevel: string
```

**Schlüsselregel:** `CliRunOptions` enthält die *rohen* geparsten Werte. Kein Settings-Merge, kein Default-Fallback aus `defaults.json`, keine Pfad-Auflösung. Das ist Aufgabe des Mappers.

### 2.2 `CliArgsParser` — Pure Parser (in CLI-Projekt)

```csharp
internal static class CliArgsParser
{
    /// <summary>
    /// Pure function: string[] → CliParseResult.
    /// Keine Seiteneffekte. Kein Console.Write. Kein File.Exists.
    /// </summary>
    internal static CliParseResult Parse(string[] args);
}
```

**Validation-Regeln im Parser (rein syntaktisch):**

| Regel | Aktuell | Ziel |
|-------|---------|------|
| Flag ohne Wert (`--prefer` am Ende) | Stillschweigend ignoriert | `ExitCode=3`, Fehler in `Errors[]` |
| Flag als Wert (`--prefer --mode`) | Wert wird verschluckt | Wert mit `-`-Prefix wird zurückgewiesen |
| Unbekanntes Flag (`--foobar`) | Exit Code 3 | Exit Code 3 (korrekt, beibehalten) |
| `--dropunknown` ohne `--gamesonly` | Exit Code 3 | Exit Code 3 (korrekt, beibehalten) |
| Mode weder DryRun noch Move | Exit Code 3 | Exit Code 3 (korrekt, beibehalten) |
| Leere Roots nach `--roots " ; "` | Exit Code 3 | Exit Code 3 (korrekt, beibehalten) |

**Semantische Validierung (NICHT im Parser, im Mapper oder Preflight):**

| Regel | Zuständig |
|-------|-----------|
| Root existiert als Verzeichnis | `CliOptionsMapper` → Preflight-Check |
| Root ist kein System-Pfad | `CliOptionsMapper` → Preflight-Check |
| Extension hat `.`-Prefix | `CliOptionsMapper` (Normalisierung) |
| DAT-Root existiert | `RunEnvironmentBuilder` |
| Tool-Hashes verifiziert | `RunEnvironmentBuilder` |

### 2.3 `CliOptionsMapper` — Mapping + Root-Validierung (in CLI-Projekt)

```csharp
internal static class CliOptionsMapper
{
    /// <summary>
    /// Vereint CliRunOptions + RomCleanupSettings zu einem fertigen RunOptions.
    /// Prüft Root-Existenz und System-Pfad-Blockade.
    /// Gibt (RunOptions?, CliParseResult) zurück — CliParseResult nur bei Fehler.
    /// </summary>
    internal static (RunOptions? Options, CliParseResult? Error)
        Map(CliRunOptions raw, RomCleanupSettings settings);
}
```

**Verantwortlichkeiten:**
- Settings-Merge: CLI-Args überschreiben Settings, Settings überschreiben Defaults
- Extensions-Merge: Explizit → CLI-Wert; nicht explizit → Settings → `RunOptions.DefaultExtensions`
- Root-Existenz-Prüfung (`Directory.Exists`)
- System-Pfad-Blockade (Windows, ProgramFiles, System32)
- Audit-Pfad-Default für Move (`ArtifactPathResolver`)

### 2.4 `CliOutputWriter` — Formatierte Ausgabe (in CLI-Projekt)

```csharp
internal static class CliOutputWriter
{
    /// <summary>DryRun: Serialisiert RunProjection + DedupeGroups als JSON nach stdout.</summary>
    internal static void WriteDryRunJson(TextWriter stdout, RunProjection projection,
                                          IReadOnlyList<DedupeResult> groups);

    /// <summary>Move: Schreibt Zusammenfassung + Sidecar-Pfade nach stderr.</summary>
    internal static void WriteMoveSummary(TextWriter stderr, RunProjection projection,
                                           string? auditPath, string? reportPath);

    /// <summary>Help: Gibt Usage-Text nach stdout aus.</summary>
    internal static void WriteUsage(TextWriter stdout);

    /// <summary>Fehler: Gibt Fehlermeldungen nach stderr aus.</summary>
    internal static void WriteErrors(TextWriter stderr, IReadOnlyList<string> errors);
}
```

**Schlüsselregel für DryRun JSON (C-06):**

```csharp
// IST (anonymes Objekt, manuelles Feld-Mapping — bricht bei neuen Feldern):
var summary = new { Status = projection.Status, ExitCode = projection.ExitCode, ... };

// SOLL (direkte Serialisierung — neue Felder automatisch enthalten):
var output = new CliDryRunOutput(projection, groups);  // typed record
var json = JsonSerializer.Serialize(output, jsonOptions);
```

```
CliDryRunOutput                     // sealed record
├── [alle RunProjection-Felder]     // via Spread/Deconstruction
├── Mode: string                    // "DryRun"
├── Results: CliDedupeGroup[]       // GameKey, Winner, WinnerDatMatch, Losers
```

### 2.5 `RunEnvironmentBuilder` — Shared Setup (in Infrastructure)

```csharp
public sealed class RunEnvironmentBuilder
{
    /// <summary>
    /// Baut aus RunOptions die vollständige Ausführungsumgebung:
    /// ConsoleDetector, DatIndex, HashService, Converter, Logger.
    /// Identisch genutzt von CLI, API und WPF.
    /// </summary>
    public RunEnvironment Build(RunOptions options, RomCleanupSettings settings,
                                 Action<string>? onProgress = null);
}
```

```
RunEnvironment
├── Orchestrator: RunOrchestrator
├── Options: RunOptions
├── AuditPath: string?
├── ReportPath: string?
├── Logger: JsonlLogWriter?
```

**Das eliminiert:** 120 Zeilen Setup-Code in `Program.Run()`, die nahezu identisch in `RunManager.TryCreate()` und `RunService.BuildOrchestrator()` existieren.

### 2.6 Exit Code Mapping

```
┌────────────────────────┬──────────┬────────────────────────────┐
│ Quelle                 │ ExitCode │ Semantik                   │
├────────────────────────┼──────────┼────────────────────────────┤
│ CliParseResult         │ 0        │ Help / Version angezeigt   │
│ CliParseResult         │ 3        │ Syntaktischer Parse-Fehler │
│ CliOptionsMapper       │ 3        │ Semantischer Validierungs- │
│                        │          │ fehler (Root existiert     │
│                        │          │ nicht, System-Pfad)        │
│ RunProjection.ExitCode │ 0        │ Pipeline erfolgreich       │
│ RunProjection.ExitCode │ 1        │ Pipeline mit Fehlern       │
│ OperationCanceledException│ 2     │ Ctrl+C / Abbruch           │
│ Exception → ErrorClassifier│ 1    │ Unbehandelter Fehler       │
└────────────────────────┴──────────┴────────────────────────────┘
```

**Regel:** CLI erfindet keinen Exit Code. Jeder Code kommt entweder vom Parser (0/3), vom Mapper (3), von der Projection (0/1) oder vom Exception-Handler (1/2).

---

## 3. CLI-Datenfluss

```
                                ┌─────────────┐
                     string[]   │             │  CliParseResult
              ─────────────────>│ CliArgsParser│──────────────────┐
                                │   (pure)    │                  │
                                └─────────────┘                  │
                                                                 ▼
                                                        ┌─ Command=Help ──> CliOutputWriter.WriteUsage → exit 0
                                                        │
                                                        ├─ Command=Version ──> assembly version → exit 0
                                                        │
                                                        ├─ ExitCode=3 ──> CliOutputWriter.WriteErrors → exit 3
                                                        │
                                                        └─ Command=Run ──┐
                                                                         ▼
                                ┌──────────────┐        CliRunOptions + Settings
                                │CliOptions    │                         │
                                │   Mapper     │<────────────────────────┘
                                │              │
                                └──────┬───────┘
                                       │  RunOptions (oder Error → exit 3)
                                       ▼
                                ┌──────────────┐
                                │RunEnvironment│   (Infrastructure, shared)
                                │   Builder    │
                                └──────┬───────┘
                                       │  RunEnvironment
                                       ▼
                                ┌──────────────┐
                                │RunOrchestrator│
                                │  .Execute()  │
                                └──────┬───────┘
                                       │  RunResult
                                       ▼
                                ┌──────────────┐
                                │RunProjection │
                                │  Factory     │
                                └──────┬───────┘
                                       │  RunProjection
                                       ▼
                        ┌──────────────────────────┐
                        │    CliOutputWriter        │
                        │  DryRun → JSON (stdout)   │
                        │  Move   → Summary (stderr) │
                        └──────────┬───────────────┘
                                   │
                                   ▼
                            exit(projection.ExitCode)
```

### Datenfluss-Invarianten

1. **Keine Rückwärts-Abhängigkeiten:** Jede Stufe gibt ihr Ergebnis nach rechts weiter. Keine Stufe liest von einer späteren Stufe.
2. **Keine Stufe überspringt eine andere:** Parser → Mapper → Builder → Orchestrator → Projection → Output. Keine Abkürzungen.
3. **stdout wird exakt einmal beschrieben:** Nur `CliOutputWriter.WriteDryRunJson` (bei DryRun) oder `CliOutputWriter.WriteUsage` (bei Help). Nie beides. Nie vermischt mit stderr.
4. **stderr ist der einzige Kanal für Progress/Errors/Sidecar-Pfade.** Alles, was nicht die primäre Antwort ist, geht nach stderr.

---

## 4. Zu entfernende Altlogik

### 4.1 Aus `Program.cs` zu eliminieren

| Zeilen (circa) | Was | Wohin |
|----------------|-----|-------|
| 55–98 | Settings-Laden + Merge | `CliOptionsMapper` + `SettingsLoader` |
| 98–110 | Extensions-Merge aus Settings | `CliOptionsMapper` |
| 113–118 | ToolRunnerAdapter-Init | `RunEnvironmentBuilder` |
| 120–155 | DAT-Setup (DatRepositoryAdapter, FileHashService, ConsoleMap) | `RunEnvironmentBuilder` |
| 126–145 | ConsoleDetector-Init | `RunEnvironmentBuilder` |
| 147–165 | DatIndex-Aufbau | `RunEnvironmentBuilder` |
| 173–195 | AuditPath-Default-Logik | `CliOptionsMapper` |
| 197–230 | RunOptions-Konstruktion | `CliOptionsMapper` |
| 232–275 | DryRun JSON (anonymes Objekt mit 27+ manuellen Feldzuweisungen) | `CliOutputWriter.WriteDryRunJson` mit typisiertem Record |
| 277–284 | Move-Zusammenfassung | `CliOutputWriter.WriteMoveSummary` |
| 300–431 | ParseArgs (monolithische switch-Anweisung) | `CliArgsParser.Parse` |
| 457–482 | PrintUsage | `CliOutputWriter.WriteUsage` |
| 656–720 | BuildConsoleMap | `RunEnvironmentBuilder` |
| 722–755 | ResolveDataDir | Infrastructure-Service (bereits als Kandidat in SettingsLoader) |

### 4.2 Anonymes DryRun-JSON-Objekt

**Problem:** `Program.Run()` baut ein anonymes Objekt mit 27 manuell kopierten Feldern aus `RunProjection`. Wenn `RunProjection` ein Feld bekommt, vergisst man es hier — stilles Feld-Dropping. Der Red-Phase-Test `CliDryRunJson_ContainsAllRunProjectionFields` sichert das aktuell als Regression ab.

**Lösung:** Typisiertes `CliDryRunOutput`-Record, das `RunProjection` per Komposition enthält. Serialisierung via `JsonSerializer.Serialize()` statt manuelles `new { }`.

### 4.3 Dreifache DefaultExtensions-Quellen

| Stelle | Aktuell |
|--------|---------|
| `CliOptions.Extensions` | `new HashSet<string>(RunOptions.DefaultExtensions, ...)` |
| `RunManager` | `RunOptions.DefaultExtensions` |
| `RunService` (WPF) | `RunOptions.DefaultExtensions` |

**Lösung:** Alle drei Entry Points lesen `RunOptions.DefaultExtensions` als Single Source of Truth. Kein Kopieren in lokale Defaults. (Aktuell korrekt bei API/WPF, aber CLI kopiert in `HashSet`-Konstruktor — d.h. bei Extensions-Merge muss geprüft werden, ob das funktional identisch bleibt.)

---

## 5. Migrationshinweise

### Phase 1: Parser-Extraktion (behebt 11 Red-Phase-Tests)

1. Neue Datei `CliArgsParser.cs` mit `Parse(string[])` → `CliParseResult`.
2. Parser implementiert strenge Wert-Validierung (C-05): Jedes wert-tragende Flag prüft `++i >= args.Length` und lehnt Werte mit `-`-Prefix ab.
3. `Program.ParseArgs` wird auf einzeiligen Aufruf `CliArgsParser.Parse(args)` reduziert.
4. **Erwartetes Ergebnis:** Alle 11 Red-Phase-Tests werden grün, alle bestehenden `CliProgramTests` bleiben grün.

### Phase 2: Output-Extraktion (behebt Feld-Drift-Risiko)

1. Neue Datei `CliOutputWriter.cs` mit typisierten Methoden.
2. `CliDryRunOutput`-Record statt anonymes Objekt.
3. `PrintUsage` wird zu `CliOutputWriter.WriteUsage`.
4. **Erwartetes Ergebnis:** Red-Phase-Test `CliDryRunJson_ContainsAllRunProjectionFields` bleibt grün und wird auch bei Projection-Erweiterungen automatisch bestehen.

### Phase 3: Mapper-Extraktion (trennt Parsing von Semantik)

1. Neue Datei `CliOptionsMapper.cs` mit `Map(CliRunOptions, Settings)` → `RunOptions`.
2. Settings-Merge, Extensions-Merge, Root-Existenz-Prüfung, System-Pfad-Blockade wandern hierher.
3. `Program.Run()` verliert ~80 Zeilen.
4. **Erwartetes Ergebnis:** Mapper ist isoliert testbar ohne Filesystem-Mocking (Root-Prüfung mit TempDirs).

### Phase 4: `RunEnvironmentBuilder` (eliminiert Entry-Point-Code-Duplizierung)

1. Neue Datei `src/RomCleanup.Infrastructure/Orchestration/RunEnvironmentBuilder.cs`.
2. DAT-Setup, ConsoleDetector-Init, Converter-Init, Logger-Init wandern hierher.
3. Rückbau in `RunManager.TryCreate()` (API) und `RunService.BuildOrchestrator()` (WPF) auf `RunEnvironmentBuilder.Build()`.
4. **Erwartetes Ergebnis:** Drei Entry Points teilen exakt denselben Setup-Code. Änderungen an DAT-Konfiguration oder Tool-Initialisierung müssen nur noch an einer Stelle erfolgen.

### Phase 5: Program.cs Slim-Down

End-Zustand von `Program.cs`:

```csharp
internal static class Program
{
    private static int Main(string[] args)
    {
        var parseResult = CliArgsParser.Parse(args);

        switch (parseResult.Command)
        {
            case CliCommand.Help:
                CliOutputWriter.WriteUsage(Console.Out);
                return 0;

            case CliCommand.Version:
                Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
                return 0;
        }

        if (parseResult.ExitCode != 0)
        {
            CliOutputWriter.WriteErrors(Console.Error, parseResult.Errors);
            return parseResult.ExitCode;
        }

        var settings = SettingsLoader.Load();
        var (runOptions, mapError) = CliOptionsMapper.Map(parseResult.Options!, settings);
        if (mapError is not null)
        {
            CliOutputWriter.WriteErrors(Console.Error, mapError.Errors);
            return mapError.ExitCode;
        }

        using var cts = WireCancellation();
        var env = new RunEnvironmentBuilder().Build(runOptions!, settings, Console.Error.WriteLine);
        var result = env.Orchestrator.Execute(runOptions!, cts.Token);
        var projection = RunProjectionFactory.Create(result);

        if (runOptions!.Mode == "DryRun")
            CliOutputWriter.WriteDryRunJson(Console.Out, projection, result.DedupeGroups);
        else
            CliOutputWriter.WriteMoveSummary(Console.Error, projection, runOptions.AuditPath, result.ReportPath);

        env.Logger?.Dispose();
        return projection.ExitCode;
    }
}
```

**~40 Zeilen** statt aktuell ~850. Keine Fachlogik, keine Setup-Logik, keine JSON-Konstruktion.

### Nicht-Ziele (explizit ausgenommen)

| Was | Warum nicht |
|-----|-------------|
| Command-Pattern (`romcleanup scan`, `romcleanup convert`) | Aktuell nur ein Command. Einführen, wenn zweiter Command kommt. YAGNI. |
| Third-Party-Arg-Parser (System.CommandLine, Spectre.Console) | Aktuelle Flag-Menge (~20) ist mit hand-rolled Parser beherrschbar. Externe Abhängigkeit nicht gerechtfertigt. |
| Streaming/Progress-Bar auf stderr | CLI ist headless/CI-optimiert. Progress-Zeilen reichen. |
| JSON-Schema für CLI-Output | Erst relevant, wenn CI-Consumer das CLI-JSON maschinell verarbeiten. Dann als OpenAPI-Sidecar. |
