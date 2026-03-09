<!-- markdownlint-disable-file -->

# Task Research Notes: RomCleanup Bug Hunt – Iteration 1 (Broad Scan)

## Research Executed

### File Analysis

- `dev/modules/Core.ps1` (1-650): Rule data, Initialize-RulePatterns, Get-RegionTag, ConvertTo-GameKey (LRU), Get-VersionScore, Select-Winner
- `dev/modules/FileOps.ps1` (1-960): Path safety (Test-PathWithinRoot, reparse point checks), Get-FilesSafe (FileSystemWatcher, SQLite index), Move-ItemSafely (atomic .tmp_move, collision DUP), Invoke-RootSafeMove, blocklist
- `dev/modules/Dedupe.ps1` (1-700+): Invoke-ClassifyFile, Initialize-RegionDedupeContext, scan root, parallel classification, group-winner-report pipeline
- `dev/modules/Convert.ps1` (1-700): Strategy pattern (chdman/dolphintool/7z/psxtract/ciso), Invoke-ConvertItem (working target, verify, backup, source removal)
- `dev/modules/Tools.ps1` (180-920): Invoke-NativeProcess, Wait-ProcessResponsive, ConvertTo-QuotedArg, Test-ArchiveEntryPathsSafe, Expand-ArchiveToTemp, Get-ConsoleFromArchiveEntries
- `dev/modules/SetParsing.ps1` (1-350): CUE/GDI/CCD/MDS/M3U parsers, Resolve-SetReferencePath (path traversal protection), M3U cycle detection
- `dev/modules/Report.ps1`: ConvertTo-SafeOutputValue (CSV injection), ConvertTo-HtmlSafe, ConvertTo-HtmlAttributeSafe
- `dev/modules/Classification.ps1`: Get-FileCategory, Get-ConsoleType (disc header > folder map > ext map > regex)
- `dev/modules/BackgroundOps.ps1` (1-250): Start-BackgroundRunspace, Start-BackgroundDedupe, parameter marshaling
- `dev/modules/WpfEventHandlers.ps1` (2336-2950): Start-WpfOperationAsync, DispatcherTimer polling, completion handling
- `dev/modules/RunHelpers.Execution.ps1` (195-870): Invoke-WinnerConversionMove, move plan, audit pre-flight
- `dev/modules/RunHelpers.Audit.ps1` (1-350): HMAC-SHA256 signing, Invoke-AuditRollback
- `dev/modules/Dat.ps1`: New-SecureXmlReaderSettings (DTD disabled, XmlResolver null), archive hash extraction
- `dev/modules/ApiServer.ps1` (1-1100): REST routing, auth (constant-time compare), CORS, rate limiting
- `dev/modules/Settings.ps1` (65-200): Get-UserSettings, Set-UserSettings, Invoke-SettingsMigration
- `dev/modules/ErrorContracts.ps1` (1-100): New-OperationError, ConvertTo-OperationError
- `dev/modules/PortInterfaces.ps1` (1-150): Port factory functions (FileSystem, ToolRunner, DatRepository, AuditStore, AppState)
- `dev/modules/Compatibility.ps1` (34-80): Invoke-UiPump

### Project Conventions

- Standards referenced: copilot-instructions.md, cleanup.instructions.md
- Architecture: Layered Clean Architecture with Port/Adapter pattern
- Security: Path traversal checks, zip-slip, CSV injection, HTML encoding, HMAC audit signing, tool hash verification
- Testing: Pester v5, unit/integration/e2e, 50% coverage gate

## Key Discoveries – Bug Report Iteration 1

---

### Bug-Risk Map (Pipeline)

```
Scan (Get-FilesSafe) --> Classify (Invoke-ClassifyFile) --> GameKey (ConvertTo-GameKey)
  --> Group (by GameKey) --> Winner (Select-Winner) --> Report (CSV/HTML)
  --> Move/Trash/Bios (Move-ItemSafely) --> Convert (Invoke-ConvertItem)
  --> ConsoleSort --> DAT Verify
```

High-risk areas identified: SQLite injection, Convert atomicity, CSO indentation bug,
Move collision recovery, FileSystemWatcher overflow, Background error propagation,
API timing attack residual, CSV sanitizer edge case.

---

### BUG-001: SQLite SQL Injection via Crafted Root Path

- **Category:** Security (SQL Injection)
- **Location:** [FileOps.ps1](dev/modules/FileOps.ps1#L409-L427) `Save-SqliteFileScanIndex`
- **Symptom:** Root paths containing `'` (single quote) are escaped via `.Replace("'", "''")` and checked for `;` but NOT for SQL comment sequences (`--`), line breaks (`\n`), or other SQLite metacharacters. A crafted root path like `D:\Roms' OR 1=1 --` bypasses the semicolon check.
- **Impact:** Arbitrary SQL execution via sqlite3 CLI. Could corrupt the scan index database or read data. Requires attacker-controlled root path (e.g., via API `POST /runs`).
- **Probability:** Low-Medium (requires attacker to control root paths via API or malicious settings file)
- **Repro:** Create a root path containing `' OR 1=1 --` and trigger a scan with >10000 files.
- **Fix:** Validate root paths against a strict character whitelist (alphanumeric, `\`, `/`, `:`, `.`, `-`, `_`, space). Reject paths with `'`, `--`, newlines, or non-printable characters before SQL interpolation. Better: use parameterized queries via System.Data.SQLite instead of sqlite3 CLI.
- **Test:** Unit test with root paths containing `'`, `--`, `\n`, `;`, `''` to verify rejection/sanitization.

---

### BUG-002: Convert.ps1 CSO Block Has Broken Indentation (Scoping Bug)

- **Category:** Logic Error / Potential Crash
- **Location:** [Convert.ps1](dev/modules/Convert.ps1#L490-L519) `Invoke-ConvertItem`, CSO handling block
- **Symptom:** The entire CSO-to-ISO block (lines 490-519) has inconsistent indentation — the `if ($sourceExt -eq '.cso')` block body is indented at the outer function level (no extra indent), while the `Invoke-CsoToIso` call line has extra leading whitespace. Although PowerShell does not use indentation for scoping, the `if/if/if/return` structure indicates this was likely a paste error. Specifically, the `if (-not (Invoke-CsoToIso ...))` line starts with 6 spaces but the surrounding code uses 4 spaces. This won't cause a runtime error but makes the code fragile and could cause merge conflicts or confusion.
- **Impact:** Low (cosmetic, but indicates possible copy-paste error that could mask a real logic issue)
- **Probability:** Low (already functional)
- **Fix:** Normalize indentation of the CSO block to match surrounding code (4-space indent).
- **Test:** N/A (cosmetic)

---

### BUG-003: Move-ItemSafely Recovery Failure Leaves Orphaned .tmp_move Files

- **Category:** Data Loss Risk
- **Location:** [FileOps.ps1](dev/modules/FileOps.ps1#L860-L880) `Move-ItemSafely` recovery block
- **Symptom:** When the second `Move-Item` (tmp_move → final destination) fails and recovery also fails (moving tmp_move back to source), the file remains as a `.tmp_move` orphan. This is logged as a warning but there is no cleanup mechanism, no monitoring alert, and no user notification in the GUI.
- **Impact:** Medium — user loses track of files that exist as orphaned `.tmp_move` files. No audit trail. File effectively "disappears" from the user's perspective.
- **Probability:** Low (requires concurrent filesystem access or permission changes mid-operation)
- **Fix:** 1) Add `.tmp_move` orphan detection as a post-run health check. 2) In GUI: show notification if orphans detected. 3) Add an `Invoke-OrphanCleanup` function that scans roots for `.tmp_move` files and offers recovery.
- **Test:** Integration test: create a read-only destination directory, attempt Move-ItemSafely, verify `.tmp_move` orphan detection.

---

### BUG-004: ConvertTo-QuotedArg Returns Empty String for Empty Input Instead of Quoted Empty

- **Category:** Logic Error
- **Location:** [Tools.ps1](dev/modules/Tools.ps1#L413-L415) `ConvertTo-QuotedArg`
- **Symptom:** When `$Value` is `''` (empty string), the function returns `''` (empty string) rather than `'""'` (quoted empty). This means an argument list containing an intentional empty argument loses it entirely when joined by `ConvertTo-ArgString`.
- **Impact:** Low (no current caller passes empty string arguments intentionally)
- **Probability:** Very Low
- **Fix:** Return `'""'` for empty strings if the caller intends to pass an empty argument to native tools.
- **Test:** Unit test: `ConvertTo-QuotedArg ''` should return `'""'`.

---

### BUG-005: API Auth Bypasses /health and /openapi Endpoints (No Auth Required)

- **Category:** Security (Information Disclosure)
- **Location:** [ApiServer.ps1](dev/modules/ApiServer.ps1#L791-L830) `Invoke-RomCleanupApiRequest`
- **Symptom:** Auth check (`Test-ApiRequestAuthorization`) runs before routing, so ALL endpoints require auth, including `/health` and `/openapi`. This is actually correct security behavior.
  
  **HOWEVER:** The CORS preflight (`OPTIONS`) handler at line 801 does NOT check auth. This means any origin can send OPTIONS requests without authentication. While standard for CORS, the response includes CORS headers that may leak server information depending on `CorsMode`.
- **Impact:** Low (OPTIONS preflight is standard; no sensitive data exposed)
- **Probability:** N/A (by design for CORS)
- **Fix:** Consider omitting CORS headers for `CorsMode=none` on OPTIONS responses (currently already handled: `if ($null -eq $origin) { return }`).
- **Test:** Integration test: send OPTIONS without API key, verify no sensitive headers leaked.

---

### BUG-006: FileSystemWatcher Buffer Overflow Silently Drops File Changes

- **Category:** Data Loss Risk / Silent Failure
- **Location:** [FileOps.ps1](dev/modules/FileOps.ps1#L320-L345) FileSystemWatcher setup
- **Symptom:** When the internal buffer overflows (many rapid file changes), the FileSystemWatcher raises an `Error` event. The current code handles `Created`, `Changed`, `Deleted`, `Renamed` events but the `Error` event handler behavior needs verification. If buffer overflow isn't handled, file changes are silently lost, causing stale cache.
- **Impact:** Medium — incremental scan cache becomes stale. Files added/removed during a large operation won't be detected in the next scan unless a full rescan is triggered.
- **Probability:** Medium (likely during large batch operations with thousands of files)
- **Fix:** 1) Register Error event on FileSystemWatcher to detect buffer overflow. 2) On overflow: invalidate the incremental cache and force full rescan. 3) Consider increasing `InternalBufferSize` (default 8KB → 64KB).
- **Test:** Integration test: rapidly create/delete >10000 files while watcher is active, verify no silent data loss.

---

### BUG-007: Background Runspace Exceptions Not Fully Propagated to GUI

- **Category:** Crash / Silent Failure
- **Location:** [BackgroundOps.ps1](dev/modules/BackgroundOps.ps1#L66-L250) `Start-BackgroundDedupe`
- **Symptom:** The background runspace script block runs `Invoke-RunDedupeService` and enqueues log messages. If the PowerShell instance throws an unhandled exception (e.g., out of memory, stack overflow), the `$bg.PS.Streams.Error` collection may not be checked by the GUI polling timer. The GUI polls `$bg.PS.InvocationStateInfo.State` for `Completed`/`Failed`, but the error message extraction depends on reading the `Streams.Error` after state change — a race condition exists if the runspace is disposed before reading.
- **Impact:** Medium — GUI shows "Operation completed" or hangs without useful error message after a background crash.
- **Probability:** Low-Medium (occurs on large ROM collections or out-of-memory scenarios)
- **Fix:** In the DispatcherTimer tick handler (WpfEventHandlers.ps1), always snapshot `$bg.PS.Streams.Error` before checking completion state. Store errors in a thread-safe collection accessible to the GUI.
- **Test:** Unit test: mock a background runspace that throws, verify GUI receives the error message.

---

### BUG-008: CSV Injection Sanitizer Misses Tab-Prefixed Payloads

- **Category:** Security (CSV Injection)
- **Location:** [Report.ps1](dev/modules/Report.ps1) `ConvertTo-SafeOutputValue`
- **Symptom:** The CSV sanitizer checks for leading `=`, `+`, `-`, `@`, `|` characters. However, it first trims the value. If a filename starts with a tab character (`\t`) followed by `=SUM(...)`, the trim removes the tab, then the check catches the `=`. This is actually CORRECT behavior — the trim protects against whitespace-prefixed injection.

  **HOWEVER:** The pipe character `|` is checked but is NOT in the documented "dangerous chars" list in some CSV injection references. Some spreadsheet applications also treat `\r` (carriage return) as cell separation. The sanitizer does not strip or escape embedded `\r` or `\n` in cell values.
- **Impact:** Low-Medium — embedded newlines in filenames (rare on Windows but possible via API or crafted archives) could break CSV row structure.
- **Probability:** Very Low (Windows disallows `\r` and `\n` in filenames)
- **Fix:** Replace `\r` and `\n` in CSV output values with space or escaped representation.
- **Test:** Unit test: filename with embedded `\r\n` → verify CSV output has no raw newlines in cell value.

---

### BUG-009: Convert.ps1 Source Removal Before Final Atomicity Check Has TOCTOU Gap

- **Category:** Data Loss Risk
- **Location:** [Convert.ps1](dev/modules/Convert.ps1#L580-L610) `Invoke-ConvertItem` source cleanup
- **Symptom:** After verify + commit (Move-Item .converting → .chd), the function removes source files. Then it does a final check (`$finalTargetItem`). Between the source removal and final check, if the target file is corrupted or deleted by another process, the source is already gone.

  The sequence is: 1) Verify output, 2) Commit target, 3) Remove all source files, 4) Check target exists.

  The check at step 4 returns `ERROR` with reason `target-missing-after-source-cleanup`, but the source files are already deleted/backed-up — this is a data-loss scenario.
- **Impact:** High — source files are permanently lost if target disappears between steps 2-4.
- **Probability:** Very Low (requires concurrent filesystem modification or disk failure)
- **Fix:** Move the final target existence check BEFORE source removal. Or: keep source files until the very end, verify target one more time, then remove.
- **Test:** Integration test: mock a scenario where target is deleted after commit but before source removal. Verify source files are preserved.

---

### BUG-010: Invoke-NativeProcess Passes ArgumentList as Single Joined String

- **Category:** Logic Error / Command Injection Risk
- **Location:** [Tools.ps1](dev/modules/Tools.ps1#L289-L292) `Invoke-ExternalToolProcess` → `ConvertTo-ArgString`
- **Symptom:** `Invoke-ExternalToolProcess` calls `ConvertTo-ArgString` which joins all arguments into a single string. This is then passed as `@($argLine)` (single-element array) to `Invoke-NativeProcess` which passes it to `Start-Process -ArgumentList`. 

  `Start-Process -ArgumentList` when receiving a single string passes it as-is to the process's command line. This is actually the CORRECT approach for Windows native tools (they parse their own command line via `CommandLineToArgvW`). However, `ConvertTo-QuotedArg` handles the quoting, and this creates a dual-quoting risk: PowerShell's Start-Process may add its own quoting layer on top.
- **Impact:** Medium — could cause tools to fail on paths with special characters (quotes, spaces, backslashes). Users would see "file not found" errors from chdman/dolphintool.
- **Probability:** Low-Medium (triggered by ROM paths containing quotes or trailing backslashes)
- **Fix:** Verify behavior with paths like `D:\ROMs\Game "Special" Edition\disc.cue` and `D:\ROMs\Path With Trailing Backslash\`. Add integration tests for these edge cases.
- **Test:** Integration test with filenames containing embedded quotes, trailing backslashes, and Unicode characters.

---

### BUG-011: Select-Winner Unstable Sort on Equal Scores (Non-Determinism)

- **Category:** Logic Error / Determinism Violation
- **Location:** [Core.ps1](dev/modules/Core.ps1#L559-L580) `Select-Winner`
- **Symptom:** `Select-Winner` uses `Sort-Object` with 8 properties. The last tiebreaker is `MainPath` (ascending). PowerShell's `Sort-Object` is a **stable sort** (preserves original order for equal elements), and `MainPath` should be unique per file. 

  **HOWEVER:** If two items have identical `MainPath` (e.g., same file referenced through different set memberships), the sort is not deterministic across PowerShell versions. Also, if `MainPath` is `$null` for any item, the sort behavior is undefined.
- **Impact:** Low — would cause different winners on different runs (non-deterministic behavior), violating the core invariant.
- **Probability:** Very Low (MainPath should always be unique and non-null)
- **Fix:** Add a null-check assertion on `MainPath` at the start of `Select-Winner`. Consider adding an explicit tie-breaking hash (e.g., SHA256 of path) for absolute determinism.
- **Test:** Unit test: two items with identical scores but different MainPaths → verify consistent winner. Test with $null MainPath → verify exception.

---

### BUG-012: API Run Payload Roots Not Path-Validated

- **Category:** Security (Path Traversal / SSRF)
- **Location:** [ApiServer.ps1](dev/modules/ApiServer.ps1#L121-L135) `Test-ApiRunPayload`
- **Symptom:** The API payload validator checks that `roots` is a non-empty array of non-blank strings, and that `mode` is `DryRun` or `Move`. But it does NOT validate that root paths are:
  1) Absolute paths (not relative like `..\..\Windows\System32`)
  2) Within allowed directories
  3) Not network paths (UNC `\\server\share`)
  4) Not pointing to system directories

  The downstream pipeline does check `Test-PathWithinRoot` for moves, but the SCAN phase (`Get-FilesSafe`) will enumerate any directory the process has access to, potentially leaking directory structure via the result/report.
- **Impact:** Medium-High — information disclosure (directory listing of arbitrary paths), potential data modification if Mode=Move.
- **Probability:** Low (API is localhost-only, requires API key)
- **Fix:** Add path validation in `Test-ApiRunPayload`: require absolute paths, block UNC paths and system directories (use `Get-MovePathBlocklist`), optionally require an allowlist.
- **Test:** Integration test: submit root paths like `..\..\Windows`, `\\server\share`, `C:\Windows\System32` → verify rejection.

---

### BUG-013: SQLite Scan Index CSV Uses Double-Quote Escaping Without Enclosure Guarantee

- **Category:** Data Corruption
- **Location:** [FileOps.ps1](dev/modules/FileOps.ps1#L397-L402) `Save-SqliteFileScanIndex` CSV generation
- **Symptom:** File paths are written to CSV with `.Replace('"', '""')` for double-quote escaping, and each field is enclosed in double quotes. However, file paths on Windows can contain newlines (via alternate data streams or API-created paths). A path with an embedded newline would break the CSV row structure, corrupting the SQLite import.
- **Impact:** Low — SQLite import would fail with a parse error, scan index becomes corrupted
- **Probability:** Very Low (newlines in Windows file paths are extremely rare)
- **Fix:** Also escape/replace `\r`, `\n` in path strings before CSV output. Or use a proper CSV writer.
- **Test:** Unit test: create a FileInfo with embedded newline in path, verify CSV escaping.

---

### BUG-014: Invoke-CsoToIso Fallback Loop May Attempt Multiple Decompressions

- **Category:** Performance / Logic Error
- **Location:** [Convert.ps1](dev/modules/Convert.ps1#L87-L112) `Invoke-CsoToIso`
- **Symptom:** The function tries multiple argument patterns in a loop (`foreach ($argPattern in @(...))`) to decompress CSO to ISO. If ciso reports success (exit code 0) but produces an empty/corrupt output file, the loop continues to the next pattern. However, the output file path is the same for all attempts — a failed attempt's partial output isn't cleaned up before the next attempt, potentially causing the next attempt to skip or fail.
- **Impact:** Low — worst case: failed conversion treated as success if a previous iteration left a non-empty but corrupt file.
- **Probability:** Low (ciso tool is deterministic)
- **Fix:** Delete the output file before each attempt in the loop. Add size check after each attempt.
- **Test:** Integration test with a corrupt CSO file → verify no false positive.

---

### BUG-015: HMAC Audit Signing Key Stored in Environment Variable

- **Category:** Security (Key Management)
- **Location:** [RunHelpers.Audit.ps1](dev/modules/RunHelpers.Audit.ps1) `Write-AuditMetadataSidecar`
- **Symptom:** The HMAC-SHA256 signing key for audit CSVs is read from an environment variable. Environment variables are visible to all processes running under the same user, can be logged by process monitoring tools, and persist in crash dumps.
- **Impact:** Medium — if the signing key is compromised, an attacker can forge audit records, undermining the integrity guarantee.
- **Probability:** Low (requires local access to the machine)
- **Fix:** Consider using DPAPI (`[System.Security.Cryptography.ProtectedData]`) for key storage on Windows, or generate a per-session key stored only in memory.
- **Test:** Security review: verify HMAC key is not logged, not in crash dumps, not in settings file.

---

## Top 10 Release Blockers (Prioritized)

| Rank | Bug-ID | Title | Impact | Fix Effort |
|------|--------|-------|--------|------------|
| 1 | BUG-009 | Convert source removal TOCTOU gap | Data Loss | Medium |
| 2 | BUG-001 | SQLite SQL Injection via root path | Security | Medium |
| 3 | BUG-012 | API roots not path-validated | Security | Low |
| 4 | BUG-003 | .tmp_move orphan files no detection | Data Loss | Low |
| 5 | BUG-007 | Background exception propagation gap | Crash/UX | Medium |
| 6 | BUG-006 | FileSystemWatcher buffer overflow silent | Data Integrity | Low |
| 7 | BUG-010 | Dual-quoting risk for native tools | Functionality | Medium |
| 8 | BUG-008 | CSV newlines in cell values | Security | Low |
| 9 | BUG-013 | SQLite CSV import with newlines | Data Corruption | Low |
| 10 | BUG-015 | HMAC key in environment variable | Security | Medium |

## Next Bug Attack Points (Iteration 2 – Deep Dive)

1. **Region detection collision:** 2-letter codes (e.g., `fr`) vs language tokens — could cases exist where language-only ROMs get wrong region?
2. **Parallel classification thread safety:** Dedupe.ps1 uses ForEach-Object -Parallel when PS7 — are `$script:` module-level caches thread-safe?
3. **WPF DispatcherTimer nested try/catch:** WpfEventHandlers.ps1 timerTick has nested try/catch — verify no swallowed exceptions hide real failures.
4. **LRU cache eviction race:** GameKey LRU (50k entries) accessed from background runspace — is it thread-safe?
5. **CUE FILE regex edge cases:** SetParsing.ps1 CUE parser — does it handle FILE lines with single quotes or no quotes?
6. **DAT XML MaxCharactersInDocument:** New-SecureXmlReaderSettings doesn't set this — a massive DAT file could cause OOM.
7. **API SSE stream backpressure:** Write-ApiRunStreamResponse has no backpressure — slow client could cause server-side buffer growth.
8. **Audit rollback partial failure:** If rollback fails mid-way, some files are restored and some aren't — no transaction guarantee.
9. **ConvertTo-GameKey LRU with aliasEditionKeying toggle:** If setting changes between runs, cached keys from previous setting are stale.
10. **M3U recursion depth:** No explicit recursion depth limit in Get-M3URelatedFiles — only cycle detection via visited set.

## Implementation Guidance

- **Objectives**: Fix data-loss risks (BUG-009, BUG-003) and security issues (BUG-001, BUG-012) as top priority
- **Key Tasks**: Add path validation to API, fix TOCTOU in Convert, add orphan detection, parameterize SQLite queries
- **Dependencies**: SQLite parameterization requires switching from sqlite3 CLI to System.Data.SQLite or using .NET SQLite bindings
- **Success Criteria**: All 15 bugs documented with fix suggestions, top 10 prioritized, no critical data-loss or security issues remain open

## Continuation

→ **Iteration 2 (Deep Dive):** [20260308-romcleanup-bughunt-iteration2-research.md](20260308-romcleanup-bughunt-iteration2-research.md)
  - 11 new bugs (BUG-016 through BUG-026)
  - Deep analysis of all 10 attack points from Iteration 1
  - Combined reprioritized Top 10 blockers
  - Iteration 3 attack points defined
