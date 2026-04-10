# ADR-0010: Safety / IO / Security – Zielarchitektur

**Status:** Proposed  
**Datum:** 2026-03-19  
**Betrifft:** `src/Romulus.Infrastructure/FileSystem/`, `src/Romulus.Infrastructure/Safety/`, `src/Romulus.Infrastructure/Audit/`, `src/Romulus.Infrastructure/Conversion/`, `src/Romulus.Infrastructure/Dat/`, `src/Romulus.Contracts/Ports/IFileSystem.cs`  
**Spezifikation:** 11 RED-Tests in `src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs`  
**Vorgänger:** ADR-0007 (Final Core Functions), ADR-0009 (API Target Architecture)

---

## 1. Executive Design

### Problem

Die aktuelle Safety-Implementierung deckt die häufigsten Angriffsvektoren ab (Path Traversal via `..`, Zip-Slip, Reparse-Point-Blocking, CSV-Injection, HTML-Escaping), hat aber **11 identifizierte Lücken**, die jeweils durch einen roten TDD-Test spezifiziert sind:

| # | Lücke | Schweregrad | Ist-Zustand |
|---|-------|-------------|-------------|
| 1 | NTFS Alternate Data Streams in Pfadauflösung | P1 | `ResolveChildPathWithinRoot` prüft nicht auf `:` |
| 2 | Trailing-Dot Windows-Normalisierung | P2 | Pfade wie `sub...\` passieren Guards |
| 3 | Destination Escape bei Move-Operationen | P0 | `MoveItemSafely` validiert kein Root-Containment auf dem Ziel |
| 4 | Locked-File Handling | P2 | `MoveItemSafely` wirft unbehandelte `IOException` |
| 5 | ReadOnly-Attribute bei Delete | P2 | `DeleteFile` scheitert an ReadOnly-Dateien |
| 6 | Extended-Length-Prefix (`\\?\`) als Bypass | P1 | `NormalizePath` akzeptiert den Prefix |
| 7 | Zip-Bomb Compression Ratio | P2 | Nur Entry-Count + Gesamtgröße, keine Ratio-Prüfung |
| 8 | DTD Processing in DAT-Parsing | P2 | `DtdProcessing.Ignore` statt `Prohibit` |
| 9 | Rollback ohne Sidecar-Verifikation | P1 | Rollback läuft auch ohne `.meta.json` |
| 10 | Rollback über Reparse-Points | P1 | Keine `IsReparsePoint`-Prüfung vor Rollback-Move |
| 11 | Compression-Ratio-Konstante fehlt | P2 | Kein `MaxCompressionRatio`-Feld in FormatConverterAdapter |

### Designprinzipien

Alle Fixes folgen dem **Defense-in-Depth-Prinzip**:

1. **Fail-Closed:** Im Zweifelsfall ablehnen, nicht durchlassen.
2. **Single Responsibility:** Jeder Guard prüft exakt eine Invariante.
3. **Zentralisierung:** Alle Pfad-Guards gehören in `IFileSystem` / `FileSystemAdapter`. Kein Aufrufpunkt baut eigene Validierung.
4. **Test-First:** Jeder Fix wird durch einen existierenden roten Test verifiziert.
5. **Kein Breaking Change:** Bestehende grüne Tests bleiben grün.

### Zielzustand

```
User Input (Pfad / Archiv / Audit-CSV)
  │
  ├─ PathGuard (zentral in FileSystemAdapter)
  │    ├── NFC-Normalisierung
  │    ├── ADS-Rejection  ← NEU: Prüfung auf ':'
  │    ├── Trailing-Dot-Rejection  ← NEU
  │    ├── Extended-Length-Prefix-Rejection  ← NEU (SafetyValidator)
  │    ├── Root-Containment  (bestehend)
  │    └── Reparse-Point-Ancestry-Check  (bestehend)
  │
  ├─ MoveGuard (in MoveItemSafely / MoveDirectorySafely)
  │    ├── Source Reparse-Point Block  (bestehend)
  │    ├── Dest-Parent Reparse-Point Block  (bestehend)
  │    ├── Destination-Root-Containment  ← NEU
  │    ├── Locked-File Graceful Handling  ← NEU: IOException → null
  │    └── Collision-Safe DUP-Suffix  (bestehend)
  │
  ├─ DeleteGuard (in DeleteFile)
  │    ├── Reparse-Point Block  (bestehend)
  │    └── ReadOnly-Attribute-Clear  ← NEU
  │
  ├─ ArchiveGuard (in FormatConverterAdapter + DatSourceService)
  │    ├── Zip-Slip per Entry  (bestehend)
  │    ├── Entry-Count-Limit  (bestehend)
  │    ├── Total-Size-Limit  (bestehend)
  │    ├── Compression-Ratio-Limit  ← NEU
  │    └── Post-Extraction Reparse-Check  (bestehend)
  │
  ├─ XmlGuard (in DatRepositoryAdapter)
  │    ├── DtdProcessing.Prohibit  ← UPGRADE von Ignore
  │    ├── XmlResolver = null  (bestehend)
  │    └── File-Size-Limit  (bestehend)
  │
  └─ RollbackGuard (in AuditSigningService.Rollback)
       ├── Root-Containment oldPath/newPath  (bestehend)
       ├── Reverse-Order Processing  (bestehend)
       ├── Sidecar-Pflicht  ← NEU: kein Rollback ohne .meta.json
       ├── Reparse-Point Pre-Check  ← NEU: IsReparsePoint vor Move
       └── HMAC-Verifikation  (bestehend)
```

---

## 2. Zielobjekte / Guard Rails / Validatoren

### 2.1 PathGuard – Zentrale Pfadvalidierung

**Datei:** `FileSystemAdapter.cs` → `ResolveChildPathWithinRoot()`

**Neue Prüfungen (vor Root-Containment-Check):**

```
① ADS-Rejection
   IF relativePath enthält ':'  → return null
   Grund: NTFS Alternate Data Streams erlauben versteckte Daten-Exfiltration
   und umgehen Content-basierte Prüfungen.

② Trailing-Dot / Trailing-Space Rejection
   IF relativePath enthält Segmente die auf '.' oder ' ' enden → return null
   Grund: Windows strippt trailing dots/spaces bei CreateFile,
   was zu Pfad-Mismatch zwischen Prüfung und tatsächlichem Zugriff führt.
```

**Datei:** `SafetyValidator.cs` → `NormalizePath()`

```
③ Extended-Length-Prefix Rejection
   IF path beginnt mit '\\?\'  → return null
   Grund: Extended-Length-Prefix umgeht Windows-MAX_PATH-Prüfungen
   und kann Blocklist-Checks invalidieren.
```

### 2.2 MoveGuard – Sichere Dateioperationen

**Datei:** `FileSystemAdapter.cs` → `MoveItemSafely()`

```
④ Locked-File Graceful Handling
   File.Move-Aufrufe, die IOException werfen (nicht wegen Collision),
   sollen null zurückgeben statt zu crashen.
   Betroffene catch-Blöcke: primärer Move + DUP-Fallback.

   ACHTUNG: Nur IOException mit SharingViolation-HResult abfangen,
   nicht pauschal alle IOExceptions verschlucken.
```

**Datei:** `FileSystemAdapter.cs` → `MoveItemSafely()` (neuer Guard)

```
⑤ Destination-Root-Containment (OPTIONAL / DISKUSSION)
   Aktuell wird MoveItemSafely von übergeordneten Phasen aufgerufen,
   die bereits Root-Containment sicherstellen (JunkRemoval, Move, ConsoleSort).
   Ein eigenständiger Guard hätte das Problem, dass der erlaubte Root
   nicht zur Signatur von MoveItemSafely gehört.

   ENTSCHEIDUNG: Kein Root-Parameter in MoveItemSafely hinzufügen.
   Stattdessen: Klarer Vertrag, dass MoveItemSafely nur mit
   vorvalidierten Pfaden aufgerufen werden darf.
   Red-Test bleibt als Dokumentation der Einschränkung bestehen,
   wird zu Assert.True (Move geht durch) umgewandelt ODER
   MoveItemSafely bekommt optionalen allowedRoot-Parameter.

   EMPFEHLUNG: Optionaler allowedRoot-Parameter:
     string? MoveItemSafely(string src, string dest, string? allowedRoot = null)
   Wenn allowedRoot gesetzt: dest muss innerhalb von allowedRoot liegen.
```

### 2.3 DeleteGuard – ReadOnly-Handling

**Datei:** `FileSystemAdapter.cs` → `DeleteFile()`

```
⑥ ReadOnly-Attribute-Clear
   VOR File.Delete():
     IF (attrs & FileAttributes.ReadOnly) != 0:
       File.SetAttributes(fullPath, attrs & ~FileAttributes.ReadOnly)
   Dann erst File.Delete().
```

### 2.4 ArchiveGuard – Zip-Bomb und Ratio

**Datei:** `FormatConverterAdapter.cs` → `ExtractZipSafe()`

```
⑦ Compression-Ratio-Limit
   NEUE Konstante: private const double MaxCompressionRatio = 100.0;

   NACH Entry-Count und Total-Size-Check:
     foreach entry:
       IF entry.CompressedLength > 0
          AND entry.Length / entry.CompressedLength > MaxCompressionRatio:
         return "archive-compression-ratio-exceeded"

   100:1 ist konservativ genug für DVD-ISOs (typisch 1:1 bis 3:1)
   und CUE/BIN-Sets (typisch 2:1 bis 20:1), aber fängt
   Null-Byte-Zip-Bombs ab (1000:1+).
```

### 2.5 XmlGuard – DTD-Policy

**Datei:** `DatRepositoryAdapter.cs` → `CreateSecureXmlSettings()`

```
⑧ DtdProcessing-Upgrade: Ignore → Prohibit
   ABER: Reale No-Intro/Redump DATs enthalten DOCTYPE-Deklarationen.
   
   ENTSCHEIDUNG nach Abwägung:
   OPTION A (empfohlen): DtdProcessing.Prohibit mit Fallback
     – Erst Prohibit versuchen
     – Bei XmlException (wegen DOCTYPE): Retry mit Ignore + Warning loggen
   
   OPTION B: Bei Ignore bleiben + Red-Test anpassen
     – Rationale: Ignore expandiert keine Entities, XmlResolver=null
       verhindert SSRF. Das reale Risiko ist minimal.
     – Nachteil: Red-Test müsste relaxt werden.

   EMPFEHLUNG: Option A. Prohibit als Default, Fallback für Legacy-DATs.
```

### 2.6 RollbackGuard – Integritätspflicht

**Datei:** `AuditSigningService.cs` → `Rollback()`

```
⑨ Sidecar-Pflicht
   Aktuell: Wenn kein .meta.json existiert, wird Rollback ohne Verifikation
   durchgeführt.

   NEU: Wenn kein .meta.json existiert:
     – DryRun: return Result mit Failed=1, Reason="missing-sidecar"
     – Execute: return Result mit Failed=1, Reason="missing-sidecar"
   
   Es sei denn, ein optionaler Parameter force=true wird übergeben.
   Signatur-Erweiterung:
     Rollback(..., bool dryRun = true, bool force = false)

⑩ Reparse-Point Pre-Check vor Rollback-Move
   VOR _fs.MoveItemSafely(newPath, oldPath):
     IF _fs.IsReparsePoint(newPath):
       skippedUnsafe++
       continue
```

---

## 3. Sicherheitsdatenfluss

### 3.1 Pfad-Lifecycle (Input → Validierung → Operation → Audit)

```
┌────────────────────────────────────────────────────────────────┐
│  User Input (GUI / CLI / API)                                  │
│    ↓                                                           │
│  SafetyValidator.NormalizePath()                               │
│    ├── Whitespace/Null → null                                  │
│    ├── Extended-Length-Prefix → null  ← NEU                    │
│    └── Path.GetFullPath() → normalized                         │
│    ↓                                                           │
│  SafetyValidator.ValidateSandbox()                             │
│    ├── Root existiert?                                         │
│    ├── Drive Root? → blocked                                   │
│    ├── Protected Path? → blocked                               │
│    ├── Reparse Point? → blocked  ← NEU (Root-Level)           │
│    └── → ok / blocked                                          │
│    ↓                                                           │
│  RunOrchestrator (Preflight → Scan → Dedupe → Sort → Move)    │
│    ↓                                                           │
│  FileSystemAdapter.ResolveChildPathWithinRoot()                │
│    ├── ADS → null  ← NEU                                      │
│    ├── Trailing Dot → null  ← NEU                              │
│    ├── Root-Containment → null if escape                       │
│    ├── Reparse-Ancestry → null if reparse in chain             │
│    └── → sanitized absolute path                               │
│    ↓                                                           │
│  FileSystemAdapter.MoveItemSafely()                            │
│    ├── NFC normalize src + dest                                │
│    ├── Source reparse → InvalidOperationException              │
│    ├── Dest parent reparse → InvalidOperationException         │
│    ├── Locked file → null  ← NEU                               │
│    ├── Collision → __DUP suffix                                │
│    └── → final destination path                                │
│    ↓                                                           │
│  AuditCsvStore.AppendAuditRow()                                │
│    ├── CSV-Injection-Sanitization                              │
│    └── → audit.csv + .meta.json sidecar                        │
└────────────────────────────────────────────────────────────────┘
```

### 3.2 Archiv-Lifecycle (ZIP → Extract → Convert → Verify → Cleanup)

```
┌────────────────────────────────────────────────────────────────┐
│  ZIP/7Z-Archiv als Quell-ROM                                   │
│    ↓                                                           │
│  FormatConverterAdapter.ExtractZipSafe()                       │
│    ├── Entry Count ≤ 10,000                                    │
│    ├── Total Size ≤ MaxExtractedTotalBytes                     │
│    ├── Compression Ratio ≤ MaxCompressionRatio  ← NEU          │
│    ├── Per-Entry: dest.StartsWith(extractDir)  (Zip-Slip)      │
│    └── → extracted to temp dir                                 │
│    ↓                                                           │
│  ValidateExtractedContents()                                   │
│    ├── All paths within extractDir                             │
│    ├── No reparse points                                       │
│    └── → true / false                                          │
│    ↓                                                           │
│  Tool Invocation (chdman / dolphintool / 7z)                   │
│    ├── Hash-verifizierter Tool-Pfad                            │
│    ├── Argument-Injection-Schutz (ArgumentList, kein Concat)   │
│    ├── Exit-Code-Prüfung                                       │
│    └── Timeout                                                 │
│    ↓                                                           │
│  Verify Output (chdman verify / magic bytes / 7z t)            │
│    ├── Success → CleanupPartialOutput(source) if convertOnly   │
│    └── Failure → CleanupPartialOutput(target), Error-Result    │
└────────────────────────────────────────────────────────────────┘
```

### 3.3 Rollback-Lifecycle (Audit-CSV → Verify → DryRun → Execute)

```
┌────────────────────────────────────────────────────────────────┐
│  Audit-CSV + .meta.json Sidecar                                │
│    ↓                                                           │
│  AuditSigningService.VerifyMetadataSidecar()                   │
│    ├── CSV SHA256 Match                                        │
│    ├── HMAC-SHA256 Match (constant-time)                       │
│    └── → verified / tampered                                   │
│    ↓                                                           │
│  Sidecar-Pflicht-Guard  ← NEU                                  │
│    ├── .meta.json existiert? → weiter                          │
│    ├── .meta.json fehlt UND force=false? → Failed, abort       │
│    └── .meta.json fehlt UND force=true? → Warning, weiter      │
│    ↓                                                           │
│  Rollback Row Processing (reverse order)                       │
│    ├── Root-Containment: oldPath in allowedRestore?            │
│    ├── Root-Containment: newPath in allowedCurrent?            │
│    ├── Reparse-Point-Check(newPath)  ← NEU                     │
│    ├── Existence-Check: newPath exists?                         │
│    ├── Collision-Check: oldPath free?                           │
│    └── _fs.MoveItemSafely(newPath, oldPath)                    │
│    ↓                                                           │
│  Rollback-Audit-Trail                                          │
│    ├── .rollback-audit.csv                                     │
│    └── .rollback-trail.csv                                     │
└────────────────────────────────────────────────────────────────┘
```

---

## 4. Zu entfernende Altlogik

| Datei | Was | Grund |
|-------|-----|-------|
| – | Keine Löschungen vorgesehen | Alle Änderungen sind additive Guards oder Upgrades bestehender Prüfungen |

**Hinweis:** Es gibt keine zu entfernende Altlogik. Alle 11 Fixes sind additive Härtungen. Es wird kein bestehender Pfad entfernt oder refactored, sondern nur erweitert.

---

## 5. Migrationshinweise

### 5.1 Reihenfolge der Implementierung

Die empfohlene Reihenfolge orientiert sich am **Schweregrad** der Lücke:

| Phase | Tests | Fix | Risiko |
|-------|-------|-----|--------|
| **1** | `MoveItemSafely_DestinationEscapeViaDotDot_IsBlocked` | Optionaler `allowedRoot`-Parameter oder Vertragsdoku | P0 |
| **2** | `ResolveChildPathWithinRoot_NtfsAlternateDataStream_RejectsPath` | ADS-Check in `ResolveChildPathWithinRoot` | P1 |
| **2** | `NormalizePath_ExtendedLengthPrefix_IsRejected` | Prefix-Guard in `NormalizePath` | P1 |
| **2** | `Rollback_WithoutMetadataSidecar_IsBlockedUntilVerified` | Sidecar-Pflicht + `force`-Parameter | P1 |
| **2** | `Rollback_WhenCurrentPathIsReparsePoint_SkipsMove` | `IsReparsePoint`-Check in Rollback-Loop | P1 |
| **3** | `ResolveChildPathWithinRoot_TrailingDotPath_RejectsPath` | Segment-Validierung | P2 |
| **3** | `DeleteFile_ReadOnlyFile_DeletesAfterAttributeClear` | Attribut-Clear vor Delete | P2 |
| **3** | `MoveItemSafely_LockedSource_ReturnsNullWithoutThrowing` | IOException → null | P2 |
| **3** | `ConvertArchive_HighCompressionRatio_IsRejectedAsZipBomb` | `MaxCompressionRatio` + Ratio-Check | P2 |
| **3** | `FormatConverter_HasCompressionRatioLimitConstant` | Konstante hinzufügen | P2 |
| **4** | `DatRepository_SecureXmlSettings_UsesDtdProcessingProhibit` | Prohibit + Fallback | P2 |

### 5.2 Entscheidungspunkte (erfordern Team-Input)

| ID | Frage | Optionen | Empfehlung |
|----|-------|----------|------------|
| D1 | `MoveItemSafely` Root-Containment | (a) Optionaler Parameter (b) Vertragsdokumentation (c) Separate `MoveItemWithinRoot()` Methode | **(a)** – minimalster Breaking-Change, abwärtskompatibel durch Default `null` |
| D2 | DtdProcessing Prohibit vs Ignore | (a) Prohibit mit Fallback (b) Ignore beibehalten | **(a)** – Defense in Depth, Fallback fängt Legacy-DATs ab |
| D3 | Locked-File: null vs Exception | (a) null (graceful) (b) spezifische `FileLockedException` | **(a)** – konsistent mit bestehendem null-Return-Muster bei Move-Fehlern |

### 5.3 IFileSystem-Interface-Erweiterung

Wenn Entscheidungspunkt D1 mit Option (a) gelöst wird, ändert sich die Interface-Signatur:

```csharp
// Bestehend:
string? MoveItemSafely(string sourcePath, string destinationPath);

// Neu (abwärtskompatibel via default-Implementierung):
string? MoveItemSafely(string sourcePath, string destinationPath, string? allowedRoot = null)
    => MoveItemSafely(sourcePath, destinationPath); // default: kein Root-Check
```

### 5.4 Testmatrix nach Green-Phase

Nach Implementierung müssen **alle 11 roten Tests grün** werden, plus:

| Bestehende Testsuite | Erwartung |
|----------------------|----------|
| `SecurityTests` (10 Tests) | Unverändert grün |
| `SafetyIoRecoveryTests` (12 Tests) | Unverändert grün |
| `SafetyValidatorTests` (8 Tests) | Unverändert grün |
| `UncPathTests` (8 Tests) | Unverändert grün |
| `AuditSigningServiceTests` | Unverändert grün |
| `AuditComplianceTests` (XXE) | Ggf. anpassen wenn DtdProcessing auf Prohibit wechselt |
| `FormatConverterAdapterTests` | Unverändert grün |
| `DatSourceServiceTests` | Unverändert grün |
| Gesamtsuite (3300+ Tests) | Keine Regressionen |

### 5.5 Invarianten für CI-Gate

Diese Bedingungen müssen nach jeder Änderung in der Safety-Schicht gelten:

1. **Kein Move außerhalb erlaubter Roots möglich** (wenn `allowedRoot` gesetzt)
2. **Kein Zugriff auf NTFS Alternate Data Streams über Pfadauflösung**
3. **Kein Rollback ohne intakte Sidecar-Verifikation** (ohne `force`)
4. **Keine Reparse-Point-Durchquerung in Move/Copy/Delete/Rollback**
5. **Keine ZIP-Extraktion mit Ratio > MaxCompressionRatio**
6. **Kein DTD-Entity-Expansion in DAT-Parsing** (Prohibit oder Ignore mit XmlResolver=null)
7. **ReadOnly-Dateien sind löschbar** (DeleteFile)
8. **Locked Files erzeugen keinen unbehandelten Crash** (MoveItemSafely)
