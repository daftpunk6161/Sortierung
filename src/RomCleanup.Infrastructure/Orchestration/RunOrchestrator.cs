using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Central pipeline orchestrator for ROM cleanup operations.
/// Port of Invoke-CliRunAdapter / RunHelpers.Execution.ps1.
/// Encapsulates: Preflight → Scan → Dedupe → Sort → Convert → Move → Report.
/// Used by both CLI and API entry points.
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
    /// Execute the full pipeline: Scan → Dedupe → (optional Sort) → (optional Convert) → Move → Report.
    /// </summary>
    public RunResult Execute(RunOptions options, CancellationToken cancellationToken = default)
    {
        var result = new RunResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Phase 1: Preflight
        _onProgress?.Invoke("Preflight...");
        var preflight = Preflight(options);
        result.Preflight = preflight;
        if (preflight.ShouldReturn)
        {
            result.Status = "blocked";
            result.ExitCode = 3;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Scan
        _onProgress?.Invoke("Scanning files...");
        var candidates = ScanFiles(options, cancellationToken);
        result.TotalFilesScanned = candidates.Count;

        if (candidates.Count == 0)
        {
            result.Status = "ok";
            result.ExitCode = 0;
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3: Deduplicate
        _onProgress?.Invoke("Deduplicating...");
        var groups = DeduplicationEngine.Deduplicate(candidates);
        result.GroupCount = groups.Count;
        result.WinnerCount = groups.Count;
        result.LoserCount = groups.Sum(g => g.Losers.Count);
        result.AllCandidates = candidates;
        result.DedupeGroups = groups;

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: Console Sort (optional, Move mode only)
        if (options.SortConsole && options.Mode == "Move" && _consoleDetector is not null)
        {
            _onProgress?.Invoke("Sorting by console...");
            var sorter = new ConsoleSorter(_fs, _consoleDetector);
            result.ConsoleSortResult = sorter.Sort(
                options.Roots, options.Extensions, dryRun: false, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Move (if Mode=Move)
        if (options.Mode == "Move")
        {
            _onProgress?.Invoke("Moving files...");
            var moveResult = ExecuteMovePhase(groups, options, cancellationToken);
            result.MoveResult = moveResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Format Conversion (optional, Move mode only)
        if (options.ConvertFormat is not null && options.Mode == "Move" && _converter is not null)
        {
            _onProgress?.Invoke("Converting formats...");
            int converted = 0;
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(group.Winner.MainPath).ToLowerInvariant();
                var consoleKey = group.Winner.Type ?? "";
                var target = _converter.GetTargetFormat(consoleKey, ext);
                if (target is not null)
                {
                    var convResult = _converter.Convert(group.Winner.MainPath, target);
                    if (convResult.Outcome == ConversionOutcome.Success) converted++;
                }
            }
            result.ConvertedCount = converted;
        }

        sw.Stop();
        result.Status = "ok";
        result.ExitCode = 0;
        result.DurationMs = sw.ElapsedMilliseconds;
        return result;
    }

    private List<RomCandidate> ScanFiles(RunOptions options, CancellationToken ct)
    {
        var versionScorer = new VersionScorer();
        var candidates = new List<RomCandidate>();

        foreach (var root in options.Roots)
        {
            ct.ThrowIfCancellationRequested();
            var files = _fs.GetFilesSafe(root, options.Extensions);

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(filePath);
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
                    consoleKey = _consoleDetector.Detect(filePath, root);

                var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
                var regionScore = FormatScorer.GetRegionScore(regionTag, options.PreferRegions);
                var fmtScore = FormatScorer.GetFormatScore(ext);
                var verScore = versionScorer.GetVersionScore(fileName);

                long sizeBytes = 0;
                try { sizeBytes = new FileInfo(filePath).Length; } catch { }

                bool datMatch = false;
                if (_datIndex is not null && _hashService is not null && consoleKey != "UNKNOWN")
                {
                    var hash = _hashService.GetHash(filePath, options.HashType);
                    if (hash is not null)
                        datMatch = _datIndex.Lookup(consoleKey, hash) is not null;
                }

                candidates.Add(new RomCandidate
                {
                    MainPath = filePath,
                    GameKey = gameKey,
                    Region = regionTag,
                    Category = categoryStr,
                    RegionScore = regionScore,
                    FormatScore = fmtScore,
                    VersionScore = (int)verScore,
                    SizeBytes = sizeBytes,
                    Extension = ext,
                    DatMatch = datMatch,
                    Type = consoleKey
                });
            }
        }

        return candidates;
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

                if (_fs.MoveItemSafely(loser.MainPath, destPath))
                {
                    moveCount++;
                    savedBytes += loser.SizeBytes;

                    // Audit row per move
                    if (!string.IsNullOrEmpty(options.AuditPath))
                    {
                        _audit.AppendAuditRow(options.AuditPath, root, loser.MainPath, destPath,
                            "Move", loser.Category, "", "region-dedupe");
                    }

                    // BUG RUN-001: Incremental audit flush every 50 moves
                    if (moveCount % 50 == 0 && !string.IsNullOrEmpty(options.AuditPath))
                    {
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

    private static string? FindRootForPath(string filePath, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return root;
        }
        return roots.Count > 0 ? roots[0] : null;
    }
}

/// <summary>Options for a run execution.</summary>
public sealed class RunOptions
{
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public string Mode { get; init; } = "DryRun";
    public string[] PreferRegions { get; init; } = { "EU", "US", "WORLD", "JP" };
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; }
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public string? TrashRoot { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
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
    public int ConvertedCount { get; set; }
    public long DurationMs { get; set; }

    /// <summary>All scanned candidates (for report generation).</summary>
    public IReadOnlyList<RomCandidate> AllCandidates { get; set; } = Array.Empty<RomCandidate>();

    /// <summary>Dedupe group results (for DryRun JSON output and reports).</summary>
    public IReadOnlyList<DedupeResult> DedupeGroups { get; set; } = Array.Empty<DedupeResult>();
}

/// <summary>Result of the move phase.</summary>
public sealed record MovePhaseResult(int MoveCount, int FailCount, long SavedBytes);
