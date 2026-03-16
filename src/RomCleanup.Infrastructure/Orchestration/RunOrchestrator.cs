using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
using RomCleanup.Core.SetParsing;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Central pipeline orchestrator for ROM cleanup operations.
/// Port of Invoke-CliRunAdapter / RunHelpers.Execution.ps1.
/// Encapsulates: Preflight → Scan → Dedupe → JunkRemoval → Move → Sort → Convert → Report.
/// Used by both CLI and API entry points.
/// DESIGN-03: This class orchestrates all phases. Future refactor target: extract each phase
/// into a dedicated handler (ScanPhase, DedupePhase, MovePhase, etc.) behind an IPipelinePhase
/// interface for improved testability and SRP compliance.
/// </summary>
public sealed class RunOrchestrator
{
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly ConsoleDetector? _consoleDetector;
    private readonly FileHashService? _hashService;
    private readonly IFormatConverter? _converter;
    private readonly DatIndex? _datIndex;
    private readonly Action<string>? _onProgress;

    public RunOrchestrator(
        IFileSystem fs,
        IAuditStore audit,
        ConsoleDetector? consoleDetector = null,
        FileHashService? hashService = null,
        IFormatConverter? converter = null,
        DatIndex? datIndex = null,
        Action<string>? onProgress = null)
    {
        _fs = fs;
        _audit = audit;
        _consoleDetector = consoleDetector;
        _hashService = hashService;
        _converter = converter;
        _datIndex = datIndex;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Validate all prerequisites before a run.
    /// Port of Invoke-RunPreflight from RunHelpers.Execution.ps1.
    /// Checks: roots exist, audit dir writable (F-06), tools available.
    /// </summary>
    public OperationResult Preflight(RunOptions options)
    {
        var warnings = new List<string>();

        if (options.Roots.Count == 0)
            return OperationResult.Blocked("No roots specified");

        foreach (var root in options.Roots)
        {
            if (!_fs.TestPath(root, "Container"))
                return OperationResult.Blocked($"Root does not exist: {root}");
        }

        // F-06: Audit directory write test
        if (!string.IsNullOrEmpty(options.AuditPath))
        {
            var auditDir = Path.GetDirectoryName(options.AuditPath);
            if (!string.IsNullOrEmpty(auditDir))
            {
                try
                {
                    _fs.EnsureDirectory(auditDir);
                    var testFile = Path.Combine(auditDir, $".write_test_{Guid.NewGuid():N}");
                    File.WriteAllText(testFile, "");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return OperationResult.Blocked($"Audit directory not writable: {ex.Message}");
                }
            }
        }

        if (options.EnableDat && _datIndex is null)
            warnings.Add("DAT enabled but no DatIndex loaded");

        if (options.Extensions.Count == 0)
            warnings.Add("No file extensions specified — scan will find nothing");

        var result = OperationResult.Ok("preflight-passed");
        result.Warnings.AddRange(warnings);
        result.Meta["RootCount"] = options.Roots.Count;
        result.Meta["ExtensionCount"] = options.Extensions.Count;
        return result;
    }

    /// <summary>
    /// Execute the full pipeline: Scan → Dedupe → JunkRemoval → Move → Sort → (optional Convert) → Report.
    /// </summary>
    public RunResult Execute(RunOptions options, CancellationToken cancellationToken = default)
    {
        var result = new RunResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // V2-M09: Structured phase metrics collection
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        try
        {

        // Phase 1: Preflight
        metrics.StartPhase("Preflight");
        _onProgress?.Invoke("[Preflight] Voraussetzungen prüfen…");
        var preflight = Preflight(options);
        result.Preflight = preflight;
        metrics.CompletePhase();
        if (preflight.ShouldReturn)
        {
            _onProgress?.Invoke($"[Preflight] Blockiert: {preflight.Reason}");
            result.Status = "blocked";
            result.ExitCode = 3;
            return result;
        }
        _onProgress?.Invoke($"[Preflight] OK — {options.Roots.Count} Root(s), {options.Extensions.Count} Extension(s), Modus: {options.Mode}");
        if (preflight.Warnings.Count > 0)
            foreach (var w in preflight.Warnings)
                _onProgress?.Invoke($"[Preflight] Warnung: {w}");

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Scan
        // V2-H01: Report scan progress (file count) for large collections
        metrics.StartPhase("Scan");
        _onProgress?.Invoke($"[Scan] Scanne {options.Roots.Count} Root-Ordner…");
        foreach (var root in options.Roots)
            _onProgress?.Invoke($"[Scan] Root: {root}");
        var scanSw = System.Diagnostics.Stopwatch.StartNew();
        var candidates = ScanFiles(options, cancellationToken);
        scanSw.Stop();
        result.TotalFilesScanned = candidates.Count;
        _onProgress?.Invoke($"[Scan] Abgeschlossen: {candidates.Count} Dateien in {scanSw.ElapsedMilliseconds}ms");
        metrics.CompletePhase(candidates.Count);

        // V2-H01: Memory warning for very large scans
        if (candidates.Count > 100_000)
            _onProgress?.Invoke($"WARNING: {candidates.Count:N0} files scanned — high memory usage. Consider scanning fewer roots.");

        if (candidates.Count == 0)
        {
            result.Status = "ok";
            result.ExitCode = 0;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ConvertOnly mode: skip Dedupe/Junk/Move/Sort — go straight to conversion
        if (options.ConvertOnly && _converter is not null)
        {
            metrics.StartPhase("FormatConvert");
            _onProgress?.Invoke($"[Convert] Nur-Konvertierung: {candidates.Count} Dateien…");
            int converted = 0, convertErrors = 0, convertSkipped = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = candidate.MainPath;
                if (!File.Exists(path)) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var consoleKey = candidate.ConsoleKey ?? "";
                var target = _converter.GetTargetFormat(consoleKey, ext);
                if (target is null) { convertSkipped++; continue; }
                // Skip files already in target format
                if (string.Equals(ext, target.Extension, StringComparison.OrdinalIgnoreCase)) { convertSkipped++; continue; }
                _onProgress?.Invoke($"[Convert] {Path.GetFileName(path)} → {target.Extension}");
                var convResult = _converter.Convert(path, target, cancellationToken);
                if (convResult.Outcome == ConversionOutcome.Success)
                {
                    converted++;
                    if (convResult.TargetPath is not null && !_converter.Verify(convResult.TargetPath, target))
                    {
                        _onProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                        convertErrors++;
                    }
                    if (!string.IsNullOrEmpty(options.AuditPath) && convResult.TargetPath is not null)
                    {
                        var root = FindRootForPath(path, options.Roots);
                        if (root is not null)
                            _audit.AppendAuditRow(options.AuditPath, root, path, convResult.TargetPath, "CONVERT", "GAME", "", $"format-convert:{target.ToolName}");
                    }
                    // Move source to trash
                    if (convResult.TargetPath is not null && File.Exists(convResult.TargetPath))
                    {
                        var root = FindRootForPath(path, options.Roots);
                        if (root is not null)
                        {
                            var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
                            var trashDir = Path.Combine(trashBase, "_TRASH_CONVERTED");
                            _fs.EnsureDirectory(trashDir);
                            var fileName = Path.GetFileName(path);
                            var trashDest = _fs.ResolveChildPathWithinRoot(trashBase, Path.Combine("_TRASH_CONVERTED", fileName));
                            if (trashDest is not null)
                                try { _fs.MoveItemSafely(path, trashDest); } catch (Exception ex) { _onProgress?.Invoke($"WARNING: Could not move source after conversion: {ex.Message}"); }
                        }
                    }
                }
                else if (convResult.Outcome == ConversionOutcome.Skipped) convertSkipped++;
                else { convertErrors++; _onProgress?.Invoke($"WARNING: Conversion failed for {path}: {convResult.Reason}"); }
            }
            result.ConvertedCount = converted;
            result.ConvertErrorCount = convertErrors;
            result.ConvertSkippedCount = convertSkipped;
            result.AllCandidates = candidates;
            result.TotalFilesScanned = candidates.Count;
            _onProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertErrors} Fehler");
            metrics.CompletePhase(converted);

            sw.Stop();
            result.Status = "ok";
            result.ExitCode = 0;
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();
            result.ReportPath = GenerateReport(result, options);
            return result;
        }

        // Phase 3: Deduplicate
        metrics.StartPhase("Deduplicate");
        _onProgress?.Invoke($"[Dedupe] Gruppiere {candidates.Count} Dateien nach GameKey…");
        var dedupeSw = System.Diagnostics.Stopwatch.StartNew();
        var groups = DeduplicationEngine.Deduplicate(candidates);
        dedupeSw.Stop();
        result.GroupCount = groups.Count;
        result.WinnerCount = groups.Count;
        result.LoserCount = groups.Sum(g => g.Losers.Count);
        result.AllCandidates = candidates;
        result.DedupeGroups = groups;
        var junkGroupCount = groups.Count(g => g.Winner.Category == "JUNK");
        _onProgress?.Invoke($"[Dedupe] Abgeschlossen in {dedupeSw.ElapsedMilliseconds}ms: {groups.Count} Gruppen, Keep={groups.Count - result.LoserCount}, Move={result.LoserCount}, Junk={junkGroupCount}");
        metrics.CompletePhase(groups.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3b: Remove junk (if enabled)
        var junkRemovedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.RemoveJunk && options.Mode == "Move")
        {
            metrics.StartPhase("JunkRemoval");
            _onProgress?.Invoke("[Junk] Entferne Junk-Dateien…");
            var junkResult = ExecuteJunkRemovalPhase(candidates, groups, options, junkRemovedPaths, cancellationToken);
            result.JunkRemovedCount = junkResult.MoveCount;
            result.MoveResult = junkResult;
            _onProgress?.Invoke($"[Junk] {junkResult.MoveCount} Junk-Dateien entfernt");
            metrics.CompletePhase(junkResult.MoveCount);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: Move (if Mode=Move)
        if (options.Mode == "Move")
        {
            metrics.StartPhase("Move");
            var totalLosers = groups.Sum(g => g.Losers.Count);
            _onProgress?.Invoke($"[Move] Verschiebe {totalLosers} Duplikate in Trash…");
            var moveResult = ExecuteMovePhase(groups, options, cancellationToken);
            _onProgress?.Invoke($"[Move] Abgeschlossen: {moveResult.MoveCount} verschoben, {moveResult.FailCount} Fehler");

            if (result.MoveResult is not null)
            {
                moveResult = new MovePhaseResult(
                    result.MoveResult.MoveCount + moveResult.MoveCount,
                    result.MoveResult.FailCount + moveResult.FailCount,
                    result.MoveResult.SavedBytes + moveResult.SavedBytes);
            }
            result.MoveResult = moveResult;
            metrics.CompletePhase(moveResult.MoveCount);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Console Sort (optional, Move mode only)
        if (options.SortConsole && options.Mode == "Move" && _consoleDetector is not null)
        {
            metrics.StartPhase("ConsoleSort");
            _onProgress?.Invoke("[Sort] Sortiere Dateien nach Konsole…");
            var sorter = new ConsoleSorter(_fs, _consoleDetector);
            result.ConsoleSortResult = sorter.Sort(
                options.Roots, options.Extensions, dryRun: false, cancellationToken);
            _onProgress?.Invoke($"[Sort] Konsolen-Sortierung abgeschlossen");
            metrics.CompletePhase();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Format Conversion (optional, Move mode only)
        // Issue #16: Use actual file paths — after Sort, files may have moved.
        if (options.ConvertFormat is not null && options.Mode == "Move" && _converter is not null)
        {
            metrics.StartPhase("FormatConvert");
            _onProgress?.Invoke($"[Convert] Starte Formatkonvertierung für {groups.Count} Gruppen…");
            int converted = 0;
            int convertErrors = 0;
            int convertSkipped = 0;
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var winnerPath = group.Winner.MainPath;

                if (junkRemovedPaths.Contains(winnerPath))
                    continue;

                // After Sort phase, files may have moved — skip if winner no longer at original path
                if (!File.Exists(winnerPath))
                    continue;

                var ext = Path.GetExtension(winnerPath).ToLowerInvariant();
                var consoleKey = group.Winner.ConsoleKey ?? "";
                var target = _converter.GetTargetFormat(consoleKey, ext);
                if (target is not null)
                {
                    var convResult = _converter.Convert(winnerPath, target, cancellationToken);
                    if (convResult.Outcome == ConversionOutcome.Success)
                    {
                        converted++;

                        // Issue #17: Verify converted file
                        if (convResult.TargetPath is not null && !_converter.Verify(convResult.TargetPath, target))
                        {
                            _onProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                            convertErrors++;
                        }

                        // Issue #17: Audit entry for conversion
                        if (!string.IsNullOrEmpty(options.AuditPath) && convResult.TargetPath is not null)
                        {
                            var root = FindRootForPath(winnerPath, options.Roots);
                            if (root is not null)
                            {
                                _audit.AppendAuditRow(options.AuditPath, root, winnerPath,
                                    convResult.TargetPath, "CONVERT", "GAME", "", $"format-convert:{target.ToolName}");
                            }
                        }

                        // Issue #17: Move source to trash after successful conversion
                        if (convResult.TargetPath is not null && File.Exists(convResult.TargetPath))
                        {
                            var root = FindRootForPath(winnerPath, options.Roots);
                            if (root is not null)
                            {
                                var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
                                var trashDir = Path.Combine(trashBase, "_TRASH_CONVERTED");
                                _fs.EnsureDirectory(trashDir);
                                var fileName = Path.GetFileName(winnerPath);
                                var trashDest = _fs.ResolveChildPathWithinRoot(trashBase, Path.Combine("_TRASH_CONVERTED", fileName));
                                if (trashDest is not null)
                                {
                                    try
                                    {
                                        _fs.MoveItemSafely(winnerPath, trashDest);
                                    }
                                    catch (Exception ex)
                                    {
                                        _onProgress?.Invoke($"WARNING: Could not move source after conversion: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                    else if (convResult.Outcome == ConversionOutcome.Skipped)
                    {
                        convertSkipped++;
                    }
                    else
                    {
                        convertErrors++;
                        _onProgress?.Invoke($"WARNING: Conversion failed for {winnerPath}: {convResult.Reason}");
                    }
                }
            }
            result.ConvertedCount = converted;
            result.ConvertErrorCount = convertErrors;
            result.ConvertSkippedCount = convertSkipped;
            _onProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertErrors} Fehler");
            metrics.CompletePhase(converted);
        }

        sw.Stop();
        result.Status = "ok";
        result.ExitCode = 0;
        result.DurationMs = sw.ElapsedMilliseconds;
        result.PhaseMetrics = metrics.GetMetrics();

        // FEAT-03: Write final audit sidecar with HMAC signature
        _onProgress?.Invoke("[Audit] Schreibe Audit-Sidecar…");
        if (!string.IsNullOrEmpty(options.AuditPath) && File.Exists(options.AuditPath))
        {
            var auditLines = File.ReadAllLines(options.AuditPath);
            var rowCount = Math.Max(0, auditLines.Length - 1); // exclude header
            _audit.WriteMetadataSidecar(options.AuditPath, new Dictionary<string, object>
            {
                ["RowCount"] = rowCount,
                ["Mode"] = options.Mode,
                ["Status"] = "completed"
            });
        }

        // FEAT-02: Generate report at end of pipeline
        if (!string.IsNullOrEmpty(options.ReportPath))
        {
            _onProgress?.Invoke("[Report] Generiere HTML-Report…");
            result.ReportPath = GenerateReport(result, options);
            if (!string.IsNullOrEmpty(result.ReportPath))
                _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
        }

        _onProgress?.Invoke($"[Fertig] Pipeline abgeschlossen in {sw.ElapsedMilliseconds}ms — {result.TotalFilesScanned} Dateien, {result.GroupCount} Gruppen");
        return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Status = "cancelled";
            result.ExitCode = 2;
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            // Issue #19: Write partial audit sidecar so rollback is possible after cancel
            if (!string.IsNullOrEmpty(options.AuditPath) && File.Exists(options.AuditPath))
            {
                var auditLines = File.ReadAllLines(options.AuditPath);
                var rowCount = Math.Max(0, auditLines.Length - 1);
                _audit.WriteMetadataSidecar(options.AuditPath, new Dictionary<string, object>
                {
                    ["RowCount"] = rowCount,
                    ["Mode"] = options.Mode,
                    ["Status"] = "partial"
                });
            }

            return result;
        }
    }

    // V2-MEM-H01 DEFERRED (langfristig): ScanFiles lädt alle Kandidaten synchron in eine
    // List<RomCandidate>. Bei >100k Dateien verursacht das hohen Speicherverbrauch (~2x durch
    // Deduplizierung mit GroupBy/OrderBy/ToList). Langfristige Lösung: IAsyncEnumerable-basiertes
    // Streaming durch die gesamte Pipeline (ScanFiles → Deduplicate → Move → Sort). Dies
    // erfordert Änderungen an DeduplicationEngine, RunOrchestrator und allen nachgelagerten
    // Phasen. Aktuell zeigt der Orchestrator bei >100k Dateien eine Speicher-Warnung an.
    private List<RomCandidate> ScanFiles(RunOptions options, CancellationToken ct)
    {
        var versionScorer = new VersionScorer();
        var candidates = new List<RomCandidate>();

        // FEAT-04: Track set members so they are not independently deduplicated
        var setMemberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // V2-H11: Folder-level cache for ConsoleDetector — same folder always yields same console
        var folderConsoleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in options.Roots)
        {
            ct.ThrowIfCancellationRequested();
            _onProgress?.Invoke($"[Scan] {root}: Dateien sammeln…");
            var files = _fs.GetFilesSafe(root, options.Extensions);
            _onProgress?.Invoke($"[Scan] {root}: {files.Count} Dateien gefunden");

            int processed = 0;
            int fileCount = files.Count;
            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                // Skip files in blocklisted directories
                if (ExecutionHelpers.IsBlocklisted(filePath))
                    continue;

                processed++;
                if (processed % 500 == 0)
                    _onProgress?.Invoke($"[Scan] {processed}/{fileCount} Dateien verarbeitet…");

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var gameKey = GameKeyNormalizer.Normalize(fileName);
                var category = FileClassifier.Classify(fileName, options.AggressiveJunk);

                var categoryStr = category switch
                {
                    FileCategory.Bios => "BIOS",
                    FileCategory.Junk => "JUNK",
                    _ => "GAME"
                };

                string consoleKey = "";
                if (_consoleDetector is not null)
                {
                    var folder = Path.GetDirectoryName(filePath) ?? "";
                    if (!folderConsoleCache.TryGetValue(folder, out var cachedKey))
                    {
                        cachedKey = _consoleDetector.Detect(filePath, root);
                        folderConsoleCache[folder] = cachedKey;
                    }
                    consoleKey = cachedKey;
                }

                var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
                var regionScore = FormatScorer.GetRegionScore(regionTag, options.PreferRegions);
                var fmtScore = FormatScorer.GetFormatScore(ext);
                var verScore = versionScorer.GetVersionScore(fileName);

                long sizeBytes = 0;
                if (File.Exists(filePath))
                    try { sizeBytes = new FileInfo(filePath).Length; }
                    catch (Exception ex)
                    {
                        _onProgress?.Invoke($"WARNING: Could not read file size for {filePath}: {ex.Message}");
                    }

                bool datMatch = false;
                if (_datIndex is not null && _hashService is not null && consoleKey != "UNKNOWN")
                {
                    // Show progress for large-file hashing so the user knows what's happening
                    if (sizeBytes > 50_000_000) // >50MB
                    {
                        var sizeMb = sizeBytes / (1024.0 * 1024.0);
                        _onProgress?.Invoke($"[Scan] Hash: {Path.GetFileName(filePath)} ({sizeMb:F0} MB)…");
                    }
                    var hash = _hashService.GetHash(filePath, options.HashType);
                    if (hash is not null)
                        datMatch = _datIndex.Lookup(consoleKey, hash) is not null;
                }

                var headerScore = FormatScorer.GetHeaderVariantScore(root, filePath);
                var sizeTieBreak = FormatScorer.GetSizeTieBreakScore(null, ext, sizeBytes);

                // FEAT-04: Discover set members (CUE→BIN, GDI→tracks, CCD→IMG/SUB, M3U→discs)
                var setMembers = GetSetMembers(filePath, ext);
                foreach (var member in setMembers)
                    setMemberPaths.Add(member);

                // FEAT-07: Calculate CompletenessScore
                var completeness = CalculateCompletenessScore(filePath, ext, setMembers, datMatch);

                candidates.Add(new RomCandidate
                {
                    MainPath = filePath,
                    GameKey = gameKey,
                    Region = regionTag,
                    Category = categoryStr,
                    RegionScore = regionScore,
                    FormatScore = fmtScore,
                    VersionScore = verScore,
                    HeaderScore = headerScore,
                    CompletenessScore = completeness,
                    SizeTieBreakScore = sizeTieBreak,
                    SizeBytes = sizeBytes,
                    Extension = ext,
                    DatMatch = datMatch,
                    ConsoleKey = consoleKey
                });
            }
        }

        // FEAT-04: Remove candidates that are set members of a primary file
        if (setMemberPaths.Count > 0)
        {
            candidates.RemoveAll(c => setMemberPaths.Contains(c.MainPath));
        }

        return candidates;
    }

    /// <summary>
    /// Move standalone junk files (sole members of their GameKey group) to trash.
    /// These are never in any Losers list because deduplication only makes losers
    /// when there are multiple candidates for the same GameKey.
    /// </summary>
    private MovePhaseResult ExecuteJunkRemovalPhase(
        IReadOnlyList<RomCandidate> allCandidates,
        IReadOnlyList<DedupeResult> groups,
        RunOptions options,
        HashSet<string> junkRemovedPaths,
        CancellationToken ct)
    {
        // Collect all junk candidates: JUNK winners that are the sole member of their group,
        // plus any JUNK losers (already handled by ExecuteMovePhase, but we also catch solo junk here).
        var junkToRemove = groups
            .Where(g => g.Losers.Count == 0 && g.Winner.Category == "JUNK")
            .Select(g => g.Winner)
            .ToList();

        int moveCount = 0, failCount = 0;
        long savedBytes = 0;

        foreach (var junk in junkToRemove)
        {
            ct.ThrowIfCancellationRequested();

            var root = FindRootForPath(junk.MainPath, options.Roots);
            if (root is null) { failCount++; continue; }

            var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
            var trashDir = Path.Combine(trashBase, "_TRASH_JUNK");
            _fs.EnsureDirectory(trashDir);

            var fileName = Path.GetFileName(junk.MainPath);
            var destPath = _fs.ResolveChildPathWithinRoot(
                trashBase, Path.Combine("_TRASH_JUNK", fileName));

            if (destPath is null) { failCount++; continue; }

            var actualDest = _fs.MoveItemSafely(junk.MainPath, destPath);
            if (actualDest is not null)
            {
                moveCount++;
                savedBytes += junk.SizeBytes;
                junkRemovedPaths.Add(junk.MainPath);

                if (!string.IsNullOrEmpty(options.AuditPath))
                {
                    // V2-M01: Distinct JUNK_REMOVE action for audit differentiation
                    // Issue #12: Log actual destination path (may include __DUP suffix)
                    _audit.AppendAuditRow(options.AuditPath, root, junk.MainPath, actualDest,
                        "JUNK_REMOVE", "JUNK", "", "junk-removal");
                }
            }
            else
            {
                failCount++;
            }
        }

        return new MovePhaseResult(moveCount, failCount, savedBytes);
    }

    /// <summary>
    /// Move losers/junk to trash with incremental audit flush.
    /// Port of Invoke-MovePhase from RunHelpers.Execution.ps1.
    /// </summary>
    private MovePhaseResult ExecuteMovePhase(
        IReadOnlyList<DedupeResult> groups,
        RunOptions options,
        CancellationToken ct)
    {
        int moveCount = 0, failCount = 0;
        long savedBytes = 0;

        foreach (var group in groups)
        {
            foreach (var loser in group.Losers)
            {
                ct.ThrowIfCancellationRequested();

                // Derive root from the file path
                var root = FindRootForPath(loser.MainPath, options.Roots);
                if (root is null) { failCount++; continue; }

                var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
                var trashDir = Path.Combine(trashBase, "_TRASH_REGION_DEDUPE");
                _fs.EnsureDirectory(trashDir);

                var fileName = Path.GetFileName(loser.MainPath);
                var destPath = _fs.ResolveChildPathWithinRoot(
                    trashBase, Path.Combine("_TRASH_REGION_DEDUPE", fileName));

                if (destPath is null) { failCount++; continue; }

                // Issue #24: ConflictPolicy support
                if (string.Equals(options.ConflictPolicy, "Skip", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(destPath))
                {
                    _onProgress?.Invoke($"Skip (conflict): {Path.GetFileName(loser.MainPath)}");
                    continue;
                }

                var actualDest = _fs.MoveItemSafely(loser.MainPath, destPath);
                if (actualDest is not null)
                {
                    moveCount++;
                    savedBytes += loser.SizeBytes;

                    // Audit row per move
                    // Issue #12: Log actual destination path (may include __DUP suffix)
                    if (!string.IsNullOrEmpty(options.AuditPath))
                    {
                        _audit.AppendAuditRow(options.AuditPath, root, loser.MainPath, actualDest,
                            "Move", loser.Category, "", "region-dedupe");
                    }

                    // BUG RUN-001: Incremental audit flush every 50 moves
                    if (moveCount % 50 == 0 && !string.IsNullOrEmpty(options.AuditPath))
                    {
                        // V2-BUG-H04: Flush CSV before writing sidecar to ensure consistency
                        _audit.Flush(options.AuditPath);
                        _audit.WriteMetadataSidecar(options.AuditPath, new Dictionary<string, object>
                        {
                            ["IncrementalFlush"] = true,
                            ["MoveCount"] = moveCount
                        });
                    }
                }
                else
                {
                    failCount++;
                }
            }
        }

        return new MovePhaseResult(moveCount, failCount, savedBytes);
    }

    private string? FindRootForPath(string filePath, IReadOnlyList<string> roots)
    {
        var fullPath = Path.GetFullPath(filePath);
        foreach (var root in roots)
        {
            if (fullPath.StartsWith(
                    NormalizeRootForContainment(root), StringComparison.OrdinalIgnoreCase))
                return root;
        }
        return null;
    }

    /// <summary>Cache normalized root paths to avoid repeated Path.GetFullPath on UNC per file.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _normalizedRoots = new();

    private string NormalizeRootForContainment(string root)
        => _normalizedRoots.GetOrAdd(root, r =>
            Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);

    /// <summary>FEAT-04: Get set member files for a primary file (CUE→BIN, GDI→tracks, etc.).</summary>
    private static IReadOnlyList<string> GetSetMembers(string filePath, string ext)
    {
        return ext switch
        {
            ".cue" => CueSetParser.GetRelatedFiles(filePath),
            ".gdi" => GdiSetParser.GetRelatedFiles(filePath),
            ".ccd" => CcdSetParser.GetRelatedFiles(filePath),
            ".m3u" => M3uPlaylistParser.GetRelatedFiles(filePath),
            _ => Array.Empty<string>()
        };
    }

    /// <summary>FEAT-07: Calculate completeness score based on set integrity and DAT match.</summary>
    private static int CalculateCompletenessScore(string filePath, string ext, IReadOnlyList<string> setMembers, bool datMatch)
    {
        int score = 0;

        // DAT-verified ROM gets a boost
        if (datMatch)
            score += 50;

        // Set completeness: check if all referenced files exist
        if (ext is ".cue" or ".gdi" or ".ccd" or ".m3u")
        {
            var missing = ext switch
            {
                ".cue" => CueSetParser.GetMissingFiles(filePath),
                ".gdi" => GdiSetParser.GetMissingFiles(filePath),
                ".ccd" => CcdSetParser.GetMissingFiles(filePath),
                ".m3u" => M3uPlaylistParser.GetMissingFiles(filePath),
                _ => Array.Empty<string>()
            };

            // Complete set = +50, incomplete = -50
            score += missing.Count == 0 ? 50 : -50;
        }
        else if (setMembers.Count == 0)
        {
            // Standalone file — neutral completeness
            score += 25;
        }

        return score;
    }

    /// <summary>FEAT-02: Generate HTML and CSV reports from pipeline results.</summary>
    private string? GenerateReport(RunResult result, RunOptions options)
    {
        try
        {
            var actualPath = RunReportWriter.WriteReport(options.ReportPath!, result, options.Mode);
            _onProgress?.Invoke($"Report written: {actualPath}");
            return actualPath;
        }
        catch (Exception ex)
        {
            _onProgress?.Invoke($"WARNING: Report generation failed: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Options for a run execution.</summary>
public sealed class RunOptions
{
    /// <summary>
    /// Default file extensions for ROM scanning. Shared across CLI, API, and WPF entry points.
    /// </summary>
    public static readonly string[] DefaultExtensions =
    {
        ".zip", ".7z", ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd",
        ".rvz", ".gcz", ".wbfs", ".nsp", ".xci", ".nes", ".snes",
        ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".3ds",
        ".n64", ".z64", ".v64", ".md", ".gen", ".sms", ".gg",
        ".pce", ".ngp", ".ws", ".rom", ".pbp", ".pkg"
    };

    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public string Mode { get; init; } = "DryRun";
    public string[] PreferRegions { get; init; } = { "EU", "US", "WORLD", "JP" };
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; } = true;
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public string? TrashRoot { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";
    public HashSet<string> DiscBasedConsoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Full result of a pipeline execution.</summary>
public sealed class RunResult
{
    public string Status { get; set; } = "ok";
    public int ExitCode { get; set; }
    public OperationResult? Preflight { get; set; }
    public int TotalFilesScanned { get; set; }
    public int GroupCount { get; set; }
    public int WinnerCount { get; set; }
    public int LoserCount { get; set; }
    public MovePhaseResult? MoveResult { get; set; }
    public ConsoleSortResult? ConsoleSortResult { get; set; }
    public int JunkRemovedCount { get; set; }
    public int ConvertedCount { get; set; }
    public int ConvertErrorCount { get; set; }
    public int ConvertSkippedCount { get; set; }
    public long DurationMs { get; set; }
    public string? ReportPath { get; set; }

    /// <summary>All scanned candidates (for report generation).</summary>
    public IReadOnlyList<RomCandidate> AllCandidates { get; set; } = Array.Empty<RomCandidate>();

    /// <summary>Dedupe group results (for DryRun JSON output and reports).</summary>
    public IReadOnlyList<DedupeResult> DedupeGroups { get; set; } = Array.Empty<DedupeResult>();

    /// <summary>V2-M09: Structured phase timing metrics.</summary>
    public PhaseMetricsResult? PhaseMetrics { get; set; }
}

/// <summary>Result of the move phase.</summary>
public sealed record MovePhaseResult(int MoveCount, int FailCount, long SavedBytes);
