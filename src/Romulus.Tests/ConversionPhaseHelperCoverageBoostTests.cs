using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for ConversionPhaseHelper: ProcessConversionResult error paths
/// (Skipped/Blocked/Error outcomes), ExecuteBatch edge cases (empty, DryRun, cancellation,
/// parallel path, progress reporting), and ConvertSingleFile null-target-path scenario.
/// Targets ~106 uncovered lines.
/// </summary>
public sealed class ConversionPhaseHelperCoverageBoostTests : IDisposable
{
    private readonly string _root;

    public ConversionPhaseHelperCoverageBoostTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CPH_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    // ══════ ExecuteBatch ════════════════════════════════════════════

    [Fact]
    public void ExecuteBatch_EmptyWorkItems_ReturnsZeroCounts()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeMove);

        var result = ConversionPhaseHelper.ExecuteBatch(
            [], converter, options, CreateContext(RunConstants.ModeMove), "files", CancellationToken.None);

        Assert.Equal(0, result.Converted);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Blocked);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void ExecuteBatch_DryRun_AllItemsReturnNull()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeDryRun);
        var filePath = CreateFile("dryrun.iso");

        var items = new[]
        {
            new ConversionPhaseHelper.ConversionWorkItem(0, filePath, "PS1", false, false)
        };

        var result = ConversionPhaseHelper.ExecuteBatch(
            items, converter, options, CreateContext(RunConstants.ModeDryRun), "files", CancellationToken.None);

        // DryRun → ConvertSingleFile returns null → no results collected
        Assert.Equal(0, result.Converted);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void ExecuteBatch_CancellationToken_ThrowsOnCancel()
    {
        var converter = new CancellationSensitiveConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var items = new[]
        {
            new ConversionPhaseHelper.ConversionWorkItem(0, CreateFile("a.iso"), "PS1", false, false)
        };

        Assert.Throws<OperationCanceledException>(() =>
            ConversionPhaseHelper.ExecuteBatch(items, converter, options, ctx, "files", cts.Token));
    }

    [Fact]
    public void ExecuteBatch_SkipBeforeConversion_ItemNotConverted()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);

        var items = new[]
        {
            new ConversionPhaseHelper.ConversionWorkItem(0, CreateFile("skip.iso"), "PS1", false, true)
        };

        var result = ConversionPhaseHelper.ExecuteBatch(
            items, converter, options, ctx, "files", CancellationToken.None);

        Assert.Equal(0, result.Converted);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void ExecuteBatch_MultipleItems_ReportsProgress()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var messages = new List<string>();
        var ctx = CreateContext(RunConstants.ModeMove, onProgress: msg => messages.Add(msg));

        var items = Enumerable.Range(0, 5)
            .Select(i => new ConversionPhaseHelper.ConversionWorkItem(
                i, CreateFile($"game{i}.iso"), "PS1", false, false))
            .ToArray();

        ConversionPhaseHelper.ExecuteBatch(
            items, converter, options, ctx, "files", CancellationToken.None);

        // Progress messages should be emitted (workItemCount <= 50 → interval=1 → every item)
        Assert.True(messages.Count > 0);
        Assert.Contains(messages, m => m.Contains("[Convert] Fortschritt:"));
    }

    [Fact]
    public void ExecuteBatch_ThreeOrMore_ParallelPathTriggered()
    {
        // GetParallelism(count > 1) → Math.Min(MaxParallelConversions=2, count)
        // This exercises the Parallel.ForEach branch
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);

        var items = Enumerable.Range(0, 3)
            .Select(i => new ConversionPhaseHelper.ConversionWorkItem(
                i, CreateFile($"par{i}.iso"), "PS1", false, false))
            .ToArray();

        var result = ConversionPhaseHelper.ExecuteBatch(
            items, converter, options, ctx, "files", CancellationToken.None);

        // All items skipped by SkippingConverter (returns Skipped outcome)
        Assert.Equal(3, result.Skipped);
        Assert.Equal(3, result.Results.Count);
    }

    // ══════ ConvertSingleFile ═══════════════════════════════════════

    [Fact]
    public void ConvertSingleFile_DryRun_ReturnsNull()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeDryRun);
        var ctx = CreateContext(RunConstants.ModeDryRun);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("test.iso"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertSingleFile_NoTargetFormat_ReturnsNull()
    {
        var converter = new NullTargetFormatConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.xyz"), "UNKNOWN", converter, options, ctx, counters, false, CancellationToken.None);

        // No target format → returns null (not a conversion attempt)
        Assert.Null(result);
    }

    [Fact]
    public void ConvertSingleFile_AlreadyTargetFormat_ReturnsNull()
    {
        // File is .chd, target format is .chd → same extension → skip
        var converter = new ChdTargetConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.chd"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertSingleFile_ConverterReturnsSkipped_IncrementsSkipCounter()
    {
        var converter = new SkippingConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.iso"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Skipped, result!.Outcome);
        Assert.Equal(1, counters.Skipped);
    }

    [Fact]
    public void ConvertSingleFile_ConverterReturnsBlocked_IncrementsBlockedCounter()
    {
        var converter = new BlockedConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.iso"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Blocked, result!.Outcome);
        Assert.Equal(1, counters.Blocked);
    }

    [Fact]
    public void ConvertSingleFile_ConverterReturnsError_IncrementsErrorCounterAndCleansUp()
    {
        var targetPath = Path.Combine(_root, "game.chd");
        File.WriteAllBytes(targetPath, [1, 2, 3]);
        var converter = new ErrorConverter(targetPath);
        var options = MakeOptions(RunConstants.ModeMove);
        var messages = new List<string>();
        var ctx = CreateContext(RunConstants.ModeMove, onProgress: msg => messages.Add(msg));
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.iso"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.Equal(1, counters.Errors);
        Assert.Contains(messages, m => m.Contains("WARNING"));
    }

    [Fact]
    public void ConvertSingleFile_SuccessButNullTargetPath_ReturnsError()
    {
        // Converter returns Success but TargetPath is null → verification fails (IsVerificationSuccessful 
        // returns false for null TargetPath) → outcome becomes Error
        var converter = new NullPathSuccessConverter();
        var options = MakeOptions(RunConstants.ModeMove);
        var ctx = CreateContext(RunConstants.ModeMove);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            CreateFile("game.iso"), "PS1", converter, options, ctx, counters, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.Equal(1, counters.Errors);
    }

    // ══════ ConversionCounters ══════════════════════════════════════

    [Fact]
    public void ConversionCounters_DefaultsToZero()
    {
        var c = new ConversionPhaseHelper.ConversionCounters();
        Assert.Equal(0, c.Converted);
        Assert.Equal(0, c.Errors);
        Assert.Equal(0, c.Skipped);
        Assert.Equal(0, c.Blocked);
    }

    [Fact]
    public void ConversionCounters_IncrementWorks()
    {
        var c = new ConversionPhaseHelper.ConversionCounters();
        c.Converted = 5;
        c.Errors = 2;
        c.Skipped = 3;
        c.Blocked = 1;
        Assert.Equal(5, c.Converted);
        Assert.Equal(2, c.Errors);
        Assert.Equal(3, c.Skipped);
        Assert.Equal(1, c.Blocked);
    }

    // ══════ ConversionWorkItem ══════════════════════════════════════

    [Fact]
    public void ConversionWorkItem_RecordProperties()
    {
        var item = new ConversionPhaseHelper.ConversionWorkItem(7, "/p/game.iso", "PS1", true, false);
        Assert.Equal(7, item.Index);
        Assert.Equal("/p/game.iso", item.FilePath);
        Assert.Equal("PS1", item.ConsoleKey);
        Assert.True(item.TrackSetMembers);
        Assert.False(item.SkipBeforeConversion);
    }

    // ══════ ConversionBatchResult ══════════════════════════════════

    [Fact]
    public void ConversionBatchResult_RecordProperties()
    {
        var res = new List<ConversionResult>
        {
            new("src.iso", "tgt.chd", ConversionOutcome.Success)
        };
        var batch = new ConversionPhaseHelper.ConversionBatchResult(10, 2, 1, 0, res);
        Assert.Equal(10, batch.Converted);
        Assert.Equal(2, batch.Errors);
        Assert.Equal(1, batch.Skipped);
        Assert.Equal(0, batch.Blocked);
        Assert.Same(res, batch.Results);
    }

    // ══════ Helpers ════════════════════════════════════════════════

    private string CreateFile(string relativePath, string content = "x")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private RunOptions MakeOptions(string mode) =>
        new()
        {
            Roots = [_root],
            Mode = mode,
            Extensions = [".iso", ".bin", ".cue"],
            AuditPath = Path.Combine(_root, "audit.csv")
        };

    private PipelineContext CreateContext(string mode, IFileSystem? fs = null, Action<string>? onProgress = null)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = MakeOptions(mode),
            FileSystem = fs ?? new FileSystemAdapter(),
            AuditStore = new StubAuditStore(),
            Metrics = metrics,
            OnProgress = onProgress
        };
    }

    // ──── Test doubles ─────────────────────────────────────────────

    /// <summary>Converter that always returns Skipped.</summary>
    private sealed class SkippingConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Skipped, "already-target");

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    /// <summary>Converter that always returns Blocked.</summary>
    private sealed class BlockedConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Blocked, "blocked-by-policy");

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    /// <summary>Converter that returns Error with a target file.</summary>
    private sealed class ErrorConverter(string targetPath) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, targetPath, ConversionOutcome.Error, "tool-crashed");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    /// <summary>Converter where GetTargetFormat returns null → no conversion defined.</summary>
    private sealed class NullTargetFormatConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension) => null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Error, "should-not-be-called");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    /// <summary>Target is .chd — used with .chd input to test already-target-format shortcircuit.</summary>
    private sealed class ChdTargetConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Error, "should-not-be-called");

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    /// <summary>Converter returning Success but with null TargetPath.</summary>
    private sealed class NullPathSuccessConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Success);

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    /// <summary>Converter that checks cancellation token — used for cancellation test.</summary>
    private sealed class CancellationSensitiveConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return new(sourcePath, null, ConversionOutcome.Skipped);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    /// <summary>Stub audit store that records entries but does nothing.</summary>
    private sealed class StubAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false) => [];
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void AppendAuditRows(string auditCsvPath, IReadOnlyList<AuditAppendRow> rows) { }
    }
}
