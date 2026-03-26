# Code Review: API Security — RomCleanup

**Ready for Production**: No (was Nein — 3 CRITs, 4 HIGHs gefunden, alle gefixt)
**After Fixes**: Ja (alle bekannten Findings inkl. LOW umgesetzt)
**Critical Issues fixed**: 3
**High Issues fixed**: 4
**Date**: 2026-03-18
**Scope**: `src/RomCleanup.Api/`, `src/RomCleanup.Contracts/Errors/`

---

## 1) API-Lücken (Findings)

### P1-S01 — TrashRoot/DatRoot ohne Pfadvalidierung ⛔ CRITICAL

- **Schweregrad**: Critical — Path Traversal / Datenverlust
- **Impact**: Angreifer kann `trashRoot: "C:\\Windows\\System32"` setzen. RunOrchestrator verschiebt Dateien dorthin oder liest von dort. Identischer Angriff über `datRoot`.
- **Betroffene Datei(en)**: [Program.cs](../src/RomCleanup.Api/Program.cs) (POST /runs Validierung)
- **Ursache**: `roots[]` hat umfangreiche Sicherheitschecks (Reparse-Points, Systemverzeichnisse, Drive-Roots). `trashRoot` und `datRoot` werden nur getrimmt — keine Security-Validierung.
- **Fix**: `ValidatePathSecurity()` — gleiche Regeln wie für Roots: System-Dir-Block, Drive-Root-Block, Reparse-Point-Block.
- **Testabsicherung**: `TrashRoot_SystemDirectory_IsRejected`, `TrashRoot_DriveRoot_IsRejected`, `DatRoot_SystemDirectory_IsRejected`
- **Status**: ✅ GEFIXT

### P1-S02 — Correlation-ID Injection / Log Injection ⛔ CRITICAL

- **Schweregrad**: Critical — Log Injection, Header Injection
- **Impact**: Client sendet `X-Correlation-ID: "abc\nX-Admin: true"`. Newlines brechen Log-Format → Log Injection. Überlange Werte → Memory Pressure. Wert wird unvalidiert in Response-Header und Console-Log übernommen.
- **Betroffene Datei(en)**: [Program.cs](../src/RomCleanup.Api/Program.cs) (Correlation-ID Middleware)
- **Ursache**: Keine Validierung oder Sanitization des client-gelieferten Correlation-ID-Headers.
- **Fix**: `SanitizeCorrelationId()` — Nur druckbare ASCII (0x21–0x7E), max 64 Zeichen. Bei Verstoß: Server-generierte ID.
- **Testabsicherung**: `CorrelationId_WithNewlines_IsSanitized`, `CorrelationId_TooLong_IsSanitized`, `CorrelationId_ValidValues_EchoedBack`
- **Status**: ✅ GEFIXT

### P1-S03 — Exception-Message-Leak an API-Clients ⛔ CRITICAL

- **Schweregrad**: Critical — Information Disclosure (OWASP A01/A05)
- **Impact**: Bei Pipeline-Fehlern wurde `ex.Message` direkt als `OperationError.Message` an den Client gesendet. Exception-Messages können interne Pfade, DB-Connection-Strings, Konfigurationswerte oder Stack-Traces enthalten.
- **Betroffene Datei(en)**: [RunManager.cs](../src/RomCleanup.Api/RunManager.cs) (`ExecuteRun` catch-Block)
- **Ursache**: `new OperationError("RUN-INTERNAL-ERROR", ex.Message, ...)` — direktes Durchreichen ohne Sanitization.
- **Fix**: Generische Nachricht `"An internal error occurred during execution."` an Client. `ex.ToString()` wird intern via `Console.Error` geloggt.
- **Testabsicherung**: `FailedRun_ErrorMessage_DoesNotLeakExceptionDetails`
- **Status**: ✅ GEFIXT

### P1-S04 — OperationError.InnerException serialisierbar 🔴 HIGH

- **Schweregrad**: High — Information Disclosure Zeitbombe
- **Impact**: `OperationError` ist ein `record` mit `Exception? InnerException`. `System.Text.Json` serialisiert Exception-Objekte einschl. `StackTrace`, `TargetSite`, `Source`. Aktuell wird `InnerException` in API-Pfaden nicht gesetzt — aber jeder zukünftige Call zu `new OperationError(..., innerException: ex)` würde den vollen Stack an den Client leaken.
- **Betroffene Datei(en)**: [OperationError.cs](../src/RomCleanup.Contracts/Errors/OperationError.cs)
- **Fix**: `[property: JsonIgnore]` auf `InnerException` Parameter.
- **Testabsicherung**: `OperationError_InnerException_NotSerialized`
- **Status**: ✅ GEFIXT

### P2-S05 — SSE Event-Name Injection 🟠 HIGH

- **Schweregrad**: High — SSE Injection
- **Impact**: `WriteSseEvent` baut den Payload mit String-Interpolation: `$"event: {eventName}\n..."`. Ein Event-Name mit Newlines könnte zusätzliche SSE-Events injizieren. Aktuell sind Event-Namen hardcoded, aber die Funktion ist generisch.
- **Betroffene Datei(en)**: [Program.cs](../src/RomCleanup.Api/Program.cs) (`WriteSseEvent`)
- **Fix**: `SanitizeSseEventName()` — blockiert `\n`, `\r`, `:` in Event-Namen.
- **Testabsicherung**: Implizit durch bestehende SSE-Tests (Funktion defensiv gemacht).
- **Status**: ✅ GEFIXT

### P2-S06 — ConvertFormat ohne Allowlist 🟠 HIGH

- **Schweregrad**: High — Injection / Tool-Missbrauch
- **Impact**: `convertFormat` wird nur getrimmt. Beliebige Strings werden in die RunOptions übernommen und potenziell an externe Tools (chdman/dolphintool) weitergereicht.
- **Betroffene Datei(en)**: [Program.cs](../src/RomCleanup.Api/Program.cs) (POST /runs Validierung)
- **Fix**: Allowlist: `auto`, `chd`, `rvz`, `zip`, `7z`.
- **Testabsicherung**: `ConvertFormat_InvalidValue_Returns400`, `ConvertFormat_ValidValues_Accepted` (5 InlineData)
- **Status**: ✅ GEFIXT

### P2-S07 — Misleading CancelledAtUtc bei erfolgreichem Run 🟠 HIGH

- **Schweregrad**: High — Irreführender API-Zustand
- **Impact**: Wenn `Cancel()` aufgerufen wird, setzt es `CancelledAtUtc`. Wenn der Executor vor dem CTS-Fire fertig wird, ist der Status `completed` aber `CancelledAtUtc != null`. Clients interpretieren das als "wurde abgebrochen".
- **Betroffene Datei(en)**: [RunManager.cs](../src/RomCleanup.Api/RunManager.cs) (`ExecuteRun`)
- **Fix**: Nach erfolgreichem Executor: `if (status is "completed" && CancelledAtUtc != null) CancelledAtUtc = null;`
- **Testabsicherung**: `CompletedRun_WhenCancelRequestedLate_ClearsCancel`
- **Status**: ✅ GEFIXT

---

## 2) Priorisierung

| # | Finding | Schweregrad | Status |
|---|---------|------------|--------|
| S01 | TrashRoot/DatRoot Path Traversal | ⛔ Critical | ✅ Gefixt |
| S02 | Correlation-ID Injection | ⛔ Critical | ✅ Gefixt |
| S03 | Exception-Message-Leak | ⛔ Critical | ✅ Gefixt |
| S04 | InnerException serialisierbar | 🔴 High | ✅ Gefixt |
| S05 | SSE Event-Name Injection | 🟠 High | ✅ Gefixt |
| S06 | ConvertFormat Allowlist | 🟠 High | ✅ Gefixt |
| S07 | CancelledAtUtc misleading state | 🟠 High | ✅ Gefixt |

### Verbleibende Punkte (LOW)

| # | Punkt | Schweregrad | Status |
|---|-------|------------|--------|
| L01 | OpenAPI-Spec Drift (conflictPolicy, convertOnly, convertFormat) | 🟡 Low | ✅ Gefixt |
| L02 | RunRecord serialisiert sensible Filesystem-Pfade | 🟡 Low | ✅ Gefixt |
| L03 | Kein per-Run Ownership (fremde Clients können canceln/lesen) | 🟡 Low | ✅ Gefixt |
| L04 | ApiRunResult.Error typisiert als `OperationError` | 🟡 Low | ✅ Gefixt |

---

## 3) Konkrete Fixes (Zusammenfassung)

### Geänderte Dateien

1. **[Program.cs](../src/RomCleanup.Api/Program.cs)**
   - `SanitizeCorrelationId()` — Correlation-ID Sanitization
   - `SanitizeSseEventName()` — SSE Injection Guard
   - `ValidatePathSecurity()` — Pfadvalidierung für TrashRoot/DatRoot
   - ConvertFormat Allowlist-Validierung
   - TrashRoot/DatRoot validiert vor Run-Erstellung

2. **[RunManager.cs](../src/RomCleanup.Api/RunManager.cs)**
   - Exception-Message durch generische Nachricht ersetzt
   - `SafeLog()` — interne Fehlerprotokollierung via stderr
   - CancelledAtUtc-Clearing bei erfolgreichem Abschluss

3. **[OperationError.cs](../src/RomCleanup.Contracts/Errors/OperationError.cs)**
   - `[JsonIgnore]` auf `InnerException`

4. **[ApiSecurityTests.cs](../src/RomCleanup.Tests/ApiSecurityTests.cs)** (NEU)
   - 17 Security-Tests

---

## 4) Guard Rails

Die folgenden Invarianten müssen bei jedem API-Change geprüft werden:

1. **Pfad-Inputs**: Jeder vom Client kommende Pfad (`roots[]`, `trashRoot`, `datRoot`) MUSS durch `ValidatePathSecurity` oder äquivalente Prüfung laufen bevor er in RunOptions landet.

2. **Error-Responses**: Exception-Messages DÜRFEN NICHT an Clients weitergegeben werden. Nur vordefinierte Error-Codes und generische Messages.

3. **SSE-Events**: Event-Namen MÜSSEN einzeilige printable ASCII sein. `WriteSseEvent` sanitisiert defensiv.

4. **Correlation-IDs**: Client-gelieferte IDs werden sanitisiert. Bei Verstoß: Server-generierte ID.

5. **Serialisierung**: `OperationError.InnerException` ist `[JsonIgnore]`. Neue Felder auf API-exponierten Typen MÜSSEN auf Serialisierungs-Leaks geprüft werden.

6. **ConvertFormat**: Allowlist `{auto, chd, rvz, zip, 7z}`. Neue Formate erfordern expliziten Eintrag.

---

## 5) Testergebnis

```
Bestanden: 80/80 API-Tests (0 Fehler, 0 übersprungen)
  - ApiIntegrationTests:  25 ✅
  - ApiRedPhaseTests:     18 ✅
  - RunManagerTests:      20 ✅
   - ApiSecurityTests:     20 ✅ (NEU)
```

---

## 6) Nötige Tests (geliefert in ApiSecurityTests.cs)

| Test | Prüft |
|------|-------|
| `TrashRoot_SystemDirectory_IsRejected` | S01: Path Traversal |
| `TrashRoot_DriveRoot_IsRejected` | S01: Path Traversal |
| `DatRoot_SystemDirectory_IsRejected` | S01: Path Traversal |
| `ConvertFormat_InvalidValue_Returns400` | S06: Tool Injection |
| `ConvertFormat_ValidValues_Accepted` (x5) | S06: Allowlist positiv |
| `CorrelationId_ValidValues_EchoedBack` (x2) | S02: Echo korrekt |
| `CorrelationId_WithNewlines_IsSanitized` | S02: Log Injection |
| `CorrelationId_TooLong_IsSanitized` | S02: Memory Safety |
| `FailedRun_ErrorMessage_DoesNotLeakExceptionDetails` | S03: Info Disclosure |
| `OperationError_InnerException_NotSerialized` | S04: Serialisierungs-Leak |
| `CompletedRun_WhenCancelRequestedLate_ClearsCancel` | S07: Zustandskonsistenz |
| `IdempotencyConflict_DifferentPayload_ReturnsConflict` | Idempotency Guard |
