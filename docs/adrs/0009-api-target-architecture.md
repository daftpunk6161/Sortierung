# ADR-0009: API – Zielarchitektur

**Status:** Proposed  
**Datum:** 2025-07-15  
**Betrifft:** `src/RomCleanup.Api/` (Program.cs, RunManager.cs, neue Dateien)  
**Spezifikation:** 18 RED-Tests in `src/RomCleanup.Tests/ApiRedPhaseTests.cs`  
**Vorgänger:** ADR-0007 (Final Core Functions), ADR-0008 (CLI Target Architecture)

---

## 1. Executive Design

### Problem

Die API hat drei strukturelle Defizite:

1. **Eigene Wahrheit statt Shared Projection:** `ApiRunResult` dupliziert 30+ Felder manuell aus `RunProjection`, transportiert aber weder `PreflightWarnings`, `PhaseMetrics` noch `DedupeGroups`. CLI und GUI nutzen `RunProjectionFactory.Create()` korrekt — die API baut stattdessen ein eigenes DTO manuell zusammen.

2. **RunManager als God-Class:** `RunManager.cs` enthält 6 Verantwortlichkeiten in einer Datei (Lifecycle, Execution, Models, Enums, Fingerprinting, Recovery). Die Klasse ist 600+ Zeilen, davon >200 Zeilen Model-Definitionen.

3. **Middleware-Reihenfolge:** Correlation-ID wird erst NACH Auth/Rate-Limit gesetzt → 401/429-Responses haben keinen Correlation-Header. SSE mappt `completed_with_errors` auf generisches `completed`.

### Zielzustand

```
HTTP Request
  │
  ├── Middleware Pipeline (korrigierte Reihenfolge)
  │     ① Security Headers + CORS + X-Api-Version
  │     ② Correlation-ID  ← VOR Auth (Fix!)
  │     ③ Rate Limiting    ← mit Retry-After Header
  │     ④ Auth (X-Api-Key)
  │     ⑤ Request Logging
  │
  ├── Endpoint (Program.cs — nur Routing + Validation)
  │     │
  │     └── RunManager (nur Lifecycle: Create/Get/Cancel/Wait)
  │           │
  │           └── ExecuteWithOrchestrator
  │                 │
  │                 ├── RunOptions  ← Shared mit CLI/GUI
  │                 ├── RunEnvironmentBuilder.Build()
  │                 ├── RunOrchestrator.Execute()
  │                 └── RunResult → ApiResponseMapper.Map()
  │                       │
  │                       ├── RunProjection (Shared, 26 Felder)
  │                       ├── + PreflightWarnings
  │                       ├── + PhaseMetrics
  │                       └── + DedupeGroups (wenn DryRun)
  │
  └── API Response (OperationErrorResponse mit utc + correlationId)
```

### Design-Prinzipien

| Prinzip | Regel |
|---------|-------|
| **Single Source of Truth** | API-Result basiert auf `RunProjection` — keine manuelle Feld-Kopie |
| **Parity by Construction** | RunRequest → RunOptions-Mapping nutzt dieselben Felder wie CLI/GUI |
| **Error Enrichment** | Jede Fehlerantwort enthält `utc` + Correlation-ID, auch bei 401/429 |
| **Status Fidelity** | Jeder `RunOutcome`-Wert hat einen eigenen SSE-Event-Namen |

---

## 2. Zielobjekte und Services

### 2.1 Zu ändernde Dateien

| Datei | Änderung | Tests (RED → GREEN) |
|-------|----------|---------------------|
| **Program.cs** | Middleware-Reihenfolge, Health+version, Location-Header, SSE-Switch, Retry-After, Error-Shape | 6 Tests |
| **RunManager.cs** | RunRequest + ConflictPolicy/ConvertOnly, RunRecord + elapsedMs/progressPercent/cancelledAtUtc, ApiRunResult → RunProjection-basiert, DurationMs bei Cancel | 8 Tests |
| **OperationErrorResponse.cs** | + `Utc` Feld | 1 Test |

### 2.2 Neue Dateien (Extraktion aus RunManager God-Class)

| Neue Datei | Inhalt | Begründung |
|------------|--------|------------|
| `ApiModels.cs` | `RunRequest`, `RunRecord`, `ApiRunResult`, Disposition-Enums, `RunExecutionOutcome` | Model-Definitionen raus aus RunManager |
| `ApiResponseMapper.cs` | `MapResult(RunResult, RunRecord)` → API-Response-Shape | Projection-Logik zentralisiert, DRY mit RunProjectionFactory |

### 2.3 RunRequest (Zielzustand)

```csharp
public sealed class RunRequest
{
    // Bestehende Felder (unverändert)
    public string[]? Roots { get; set; }
    public string? Mode { get; set; }
    public string[]? PreferRegions { get; set; }
    public bool RemoveJunk { get; set; } = true;
    public bool AggressiveJunk { get; set; }
    public bool SortConsole { get; set; }
    public bool EnableDat { get; set; }
    public string? DatRoot { get; set; }
    public bool OnlyGames { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public string? HashType { get; set; }
    public string? ConvertFormat { get; set; }
    public string? TrashRoot { get; set; }
    public string[]? Extensions { get; set; }

    // NEU — Parity mit CLI/GUI (Tests E1, E2, E4)
    public string? ConflictPolicy { get; set; }   // "Rename" | "Skip" | "Overwrite"
    public bool ConvertOnly { get; set; }          // Skip dedupe, nur konvertieren
}
```

### 2.4 RunRecord (Zielzustand — Deltas)

```csharp
public sealed class RunRecord
{
    // ... bestehende Felder ...

    // NEU — Parity (Tests E1, E2)
    public string ConflictPolicy { get; init; } = "Rename";
    public bool ConvertOnly { get; init; }

    // NEU — Computed (Tests A4, A5, C1)
    public long ElapsedMs => Status == "running"
        ? (long)(DateTime.UtcNow - StartedUtc).TotalMilliseconds
        : CompletedUtc.HasValue
            ? (long)(CompletedUtc.Value - StartedUtc).TotalMilliseconds
            : 0;

    public int ProgressPercent { get; set; }       // Set by orchestrator progress callback
    public DateTime? CancelledAtUtc { get; set; }  // Set when cancel is accepted
}
```

### 2.5 ApiRunResult (Zielzustand — Projection-basiert)

```csharp
public sealed class ApiRunResult
{
    // Aus RunProjection (26 Felder — direkt gemappt, keine Handkopie)
    public string OrchestratorStatus { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    // ... alle 26 RunProjection-Felder ...
    public long DurationMs { get; init; }

    // NEU — erweiterte Felder (Tests A2, A3, B2, E3)
    public string[]? PreflightWarnings { get; init; }              // aus RunResult.Preflight.Warnings
    public Dictionary<string, long>? PhaseMetrics { get; init; }   // aus RunResult.PhaseMetrics
    public object[]? DedupeGroups { get; init; }                   // aus RunResult.DedupeGroups (nur DryRun)

    // NEU — strukturierter Fehler statt string (Test B2)
    public OperationError? Error { get; init; }                    // war: string? Error
}
```

### 2.6 OperationErrorResponse (Zielzustand)

```csharp
public sealed record OperationErrorResponse(
    OperationError Error,
    string? RunId = null,
    IDictionary<string, object>? Meta = null)
{
    // NEU (Test D1)
    public string Utc { get; init; } = DateTime.UtcNow.ToString("o");

    public bool Retryable => Error.Kind == ErrorKind.Transient;
}
```

---

## 3. Request → Domain → Response Flow

### 3.1 Korrigierte Middleware-Reihenfolge

```
VORHER (Bug):                      NACHHER (Ziel):
┌────────────────────────┐         ┌────────────────────────┐
│ ① Security Headers     │         │ ① Security Headers     │
│ ② CORS + OPTIONS       │         │ ② CORS + OPTIONS       │
│ ③ Rate Limiting        │  ←BUG   │ ③ Correlation-ID       │ ← FIX
│ ④ Auth (X-Api-Key)     │  ←BUG   │ ④ Rate Limiting        │ ← + Retry-After
│ ⑤ Correlation-ID       │  ←BUG   │ ⑤ Auth (X-Api-Key)     │
│ ⑥ Request Logging      │         │ ⑥ Request Logging      │
└────────────────────────┘         └────────────────────────┘

Tests: D2 (AuthError CorrelationId), D3 (RateLimit Retry-After), D4 (RateLimit CorrelationId)
```

**Umsetzung:** Die bestehende Single-Middleware muss aufgeteilt werden:
- Middleware 1: Security Headers + CORS + OPTIONS preflight
- Middleware 2: Correlation-ID (eigene `app.Use(...)`)
- Middleware 3: Rate Limiting + Retry-After Header
- Middleware 4: Auth (X-Api-Key)
- Middleware 5: Request Logging (bestehend)

### 3.2 POST /runs → RunOrchestrator Flow

```
POST /runs { roots, mode, conflictPolicy, convertOnly, ... }
  │
  ├── Validation (Program.cs)
  │   ├── Content-Type, Body-Size, JSON-Parse
  │   ├── Roots: exist, no reparse, no system-dir, no drive-root
  │   ├── Mode: DryRun | Move
  │   ├── ConflictPolicy: Rename | Skip | Overwrite (NEU, Test E4)
  │   ├── ConvertOnly: bool (NEU, kein Validierungs-Delta)
  │   ├── Regions, HashType, Extensions (bestehend)
  │   └── IdempotencyKey, OnlyGames-Policy (bestehend)
  │
  ├── RunManager.TryCreateOrReuse()
  │   ├── RunRequest → RunRecord (inkl. ConflictPolicy, ConvertOnly)
  │   ├── Fingerprint inkl. ConflictPolicy + ConvertOnly
  │   └── Active/Idempotency-Conflict-Prüfung
  │
  ├── ExecuteWithOrchestrator(RunRecord, ...)
  │   ├── RunRecord → RunOptions (NEU: ConflictPolicy, ConvertOnly gemappt)
  │   ├── RunEnvironmentBuilder.Build(options, settings, dataDir)
  │   ├── RunOrchestrator.Execute(options, ct)
  │   └── RunResult → ApiResponseMapper.Map(result, record)
  │         ├── RunProjection = RunProjectionFactory.Create(result)
  │         ├── PreflightWarnings = result.Preflight?.Warnings
  │         ├── PhaseMetrics = result.PhaseMetrics?.Phases → Dict
  │         ├── DedupeGroups = mode=="DryRun" ? result.DedupeGroups : null
  │         └── Error = (strukturiert, nicht string)
  │
  └── Response
      ├── wait=false → 202 Accepted { run, reused }
      ├── wait=true, completed → 200 OK { run, result } + Location Header (Test C2)
      └── wait=true, timeout → 202 Accepted { run, reused, waitTimedOut }
```

### 3.3 SSE Event-Mapping (Zielzustand)

```csharp
// VORHER (Bug):
var terminalEvent = current.Status switch
{
    "cancelled" => "cancelled",
    "failed"    => "failed",
    _           => "completed"       // ← BUG: completed_with_errors → "completed"
};

// NACHHER (Fix für Test B1):
var terminalEvent = current.Status switch
{
    "cancelled"             => "cancelled",
    "failed"                => "failed",
    "completed_with_errors" => "completed_with_errors",  // ← NEU: eigener Event-Name
    _                       => "completed"
};
```

### 3.4 Health Endpoint (Zielzustand)

```csharp
// VORHER:
return Results.Ok(new { status, serverRunning, hasActiveRun, utc });

// NACHHER (Test A1):
return Results.Ok(new { status, serverRunning, hasActiveRun, utc, version = ApiVersion });
```

### 3.5 Cancel Endpoint (Zielzustand)

```csharp
// NACHHER (Test A5):
run.CancelledAtUtc = DateTime.UtcNow;        // Timestamp setzen bei Cancel
// Response:
return Results.Ok(new {
    run = updated,
    cancelAccepted = ...,
    idempotent = ...,
    cancelledAtUtc = updated?.CancelledAtUtc?.ToString("o")   // NEU
});
```

### 3.6 Error Response Shape

```csharp
// VORHER:
static OperationErrorResponse CreateErrorResponse(code, message, kind, runId, meta)
    => new OperationErrorResponse(new OperationError(code, message, kind, "API"), runId, meta);

// NACHHER (Test D1 — Utc ist auto-gesetzt via Record-Default):
// Keine Code-Änderung in CreateErrorResponse nötig — Utc wird durch
// das neue Property-Default auf OperationErrorResponse automatisch befüllt.
// Beispiel-Serialisierung:
// { "error": { "code": "...", "message": "...", "kind": "..." },
//   "runId": "...", "utc": "2025-07-15T12:00:00.000Z", "retryable": false }
```

---

## 4. Zu entfernende Altlogik

### 4.1 Manuelle Feld-Kopie in ExecuteWithOrchestrator

```csharp
// ZU ENTFERNEN — ca. 40 Zeilen manuelle Zuweisung in ExecuteWithOrchestrator():
new ApiRunResult
{
    OrchestratorStatus = projection.Status,
    ExitCode = projection.ExitCode,
    TotalFiles = projection.TotalFiles,
    Candidates = projection.Candidates,
    // ... 30+ weitere Zeilen ...
}

// ERSETZEN DURCH:
ApiResponseMapper.MapResult(result, record)
```

`ApiResponseMapper.MapResult` ruft intern `RunProjectionFactory.Create(result)` auf und projiziert alle 26 Felder automatisch, plus die 3 neuen Felder (`PreflightWarnings`, `PhaseMetrics`, `DedupeGroups`).

### 4.2 ApiRunResult.Error als string

```csharp
// ZU ENTFERNEN:
public string? Error { get; init; }

// catch (Exception):
Error = "Internal error during run execution."   // ← flacher String

// ERSETZEN DURCH:
public OperationError? Error { get; init; }

// catch (Exception ex):
Error = new OperationError("RUN-INTERNAL-ERROR", ex.Message, ErrorKind.Critical, "API")
```

### 4.3 Redundante Alias-Felder auf ApiRunResult

```csharp
// ZU ENTFERNEN (nach Deprecation-Periode):
public int Winners { get; init; }     // = Keep
public int Losers { get; init; }      // = Dupes
public int Duplicates { get; init; }  // = Dupes
```

Diese drei Felder existieren nur für Rückwärtskompatibilität. In v2 können sie depreciert werden (mit `[Obsolete]` Attribut), in v3 entfernt.

### 4.4 DurationMs = 0 bei Cancel

```csharp
// ZU ENTFERNEN im catch (OperationCanceledException):
run.Result = new ApiRunResult { DurationMs = 0, ... };  // ← implizit 0

// ERSETZEN DURCH:
var elapsed = (long)(DateTime.UtcNow - run.StartedUtc).TotalMilliseconds;
run.Result = new ApiRunResult { ..., DurationMs = elapsed };
```

---

## 5. Migrationshinweise

### 5.1 Reihenfolge der Umsetzung

Die 18 RED-Tests bilden die Spezifikation. Empfohlene Implementierungsreihenfolge nach Risiko und Abhängigkeitsgraph:

| Phase | Scope | Tests | Risiko | Begründung |
|-------|-------|-------|--------|------------|
| **Phase 1** | Middleware-Split + Correlation-ID vor Auth | D2, D4 | Niedrig | Reine Umordnung, kein neues Verhalten |
| **Phase 2** | Error-Shape: OperationErrorResponse.Utc + Retry-After | D1, D3 | Niedrig | Additive Felder, keine Breaking Changes |
| **Phase 3** | Health + version, Location-Header | A1, C2 | Niedrig | Triviale Ergänzungen |
| **Phase 4** | SSE completed_with_errors Event | B1 | Niedrig | Ein Switch-Case hinzufügen |
| **Phase 5** | RunRequest + ConflictPolicy/ConvertOnly Parity | E1, E2, E4 | Mittel | Neues Request-Feld + Validation + Fingerprint |
| **Phase 6** | RunRecord ElapsedMs/ProgressPercent/CancelledAtUtc | A4, A5, C1 | Mittel | Computed Properties + Cancel-Timestamp |
| **Phase 7** | ApiRunResult → Projection-basiert + strukturierter Error | A2, A3, B2, E3 | Hoch | Refactoring von ExecuteWithOrchestrator, Breaking Change für Error-Feld |
| **Phase 8** | DurationMs bei Cancel | B3 | Niedrig | Einzeiler im catch-Block |

### 5.2 Breaking Changes

| Feld | Vorher | Nachher | Migration |
|------|--------|---------|-----------|
| `ApiRunResult.Error` | `string?` | `OperationError?` | **Breaking.** Clients die `error` als String parsen, müssen auf Objekt umstellen. API-Version-Header (`X-Api-Version`) erhöhen. |
| `OperationErrorResponse` | kein `utc` Feld | `utc` Property | **Additiv.** Kein Breaking Change — neues Feld erscheint in der Serialisierung. |
| `RunRecord` Serialisierung | kein `elapsedMs` | computed `elapsedMs` | **Additiv.** Neues Feld in GET /runs/{id} Response. |

### 5.3 Fingerprint-Update

`BuildRequestFingerprint` muss um `ConflictPolicy` und `ConvertOnly` erweitert werden, damit Idempotency-Keys korrekt funktionieren:

```csharp
// Payload-Zeile hinzufügen:
request.ConflictPolicy ?? "Rename",
request.ConvertOnly ? "1" : "0",
```

### 5.4 Test-Abdeckung nach Migration

Nach Umsetzung aller 8 Phasen:
- **18/18 ApiRedPhaseTests → GREEN**
- **25/25 ApiIntegrationTests → GREEN** (keine Regression)
- Neue Tests empfohlen:
  - ConflictPolicy Validation Edge Cases (leerer String, Groß-/Kleinschreibung)
  - ProgressPercent-Aktualisierung bei mehrstufigem Executor
  - SSE-Stream mit `completed_with_errors` + strukturiertem Error
  - Idempotency mit ConflictPolicy-Differenz

### 5.5 Nicht im Scope dieser ADR

- **Rollback-API** (`POST /runs/{id}/rollback`) — separate ADR
- **Batch-Runs** (`POST /runs/batch`) — nicht geplant
- **WebSocket-Upgrade** als SSE-Alternative — nicht geplant
- **OpenAPI-Spec-Generierung** aus Code — wünschenswert, aber eigenes Issue
- **Alias-Feld-Entfernung** (Winners/Losers/Duplicates) — erst v3

---

## Entscheidungstreiber

1. **RED-Tests als Spezifikation:** Die 18 Tests in `ApiRedPhaseTests.cs` definieren exakt, welche Felder, Header und Verhaltensweisen fehlen. Die Architektur löst jeden einzelnen Test.
2. **CLI/GUI-Parity:** `RunOptions` hat bereits `ConflictPolicy` und `ConvertOnly` — die API muss sie nur durchreichen.
3. **RunProjection als Single Source of Truth:** CLI nutzt `RunProjectionFactory.Create()` für Output, GUI für Dashboard — die API muss denselben Pfad nehmen statt manuell zu kopieren.
4. **RFC 6585 §4 Compliance:** 429-Responses brauchen `Retry-After` Header.
5. **Correlation-ID Observability:** Ohne Correlation in Auth/Rate-Limit-Fehlern sind diese Requests unsichtbar im Monitoring.

## Alternativen

### Option A: ApiRunResult durch RunProjection ersetzen (verworfen)
RunProjection hat kein `Error`, kein `AuditPath`, kein `ReportPath` — es bräuchte eine Breaking Extension des shared Records oder ein Wrapper. Zu invasiv für CLI/GUI.

### Option B: Middleware-Reihenfolge beibehalten, Correlation-ID in Helpers setzen (verworfen)
Führt zu duplizierten Correlation-ID-Logik in `WriteApiError`, `ApiError`, und allen early-return Pfaden. Fehleranfällig.

### Option C: Gewählte Lösung — Middleware-Split + ApiResponseMapper + additive Felder
Minimale Änderungen an shared Contracts (nur `Utc` auf OperationErrorResponse). Alle anderen Änderungen sind API-lokal. Keine Regression-Gefahr für CLI/GUI.
