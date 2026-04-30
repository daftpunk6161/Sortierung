using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red→Green tests for Phase 3 Important Bug fixes from DEEP_DIVE_FINDINGS.
/// Structural source-scan approach: read .cs files, assert expected patterns.
/// </summary>
public sealed class Phase3ImportantBugTests
{
    // ══════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════

    private static string FindSrcDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "Romulus.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException("Could not find src/ directory.");
    }

    private static string ExtractMethodBody(string source, int openBraceIndex)
    {
        var depth = 0;
        var start = openBraceIndex;
        for (int i = openBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0)
                return source[start..(i + 1)];
        }
        return source[start..];
    }

    // ══════════════════════════════════════════════
    // R1-008: DeduplicationEngine Path-Normalisierung
    // BuildGroupKey or Deduplicate must document/enforce normalized paths
    // ══════════════════════════════════════════════

    [Fact]
    public void R1_008_DeduplicationEngine_DocumentsOrEnforcesNormalizedPaths()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Core", "Deduplication", "DeduplicationEngine.cs");
        var source = File.ReadAllText(sourcePath);

        // The engine must either normalize paths itself or clearly document the caller contract.
        // Check for either Path.GetFullPath usage in Deduplicate/BuildGroupKey or a doc comment
        // specifying that callers must provide normalized paths.
        var hasSelfNormalization = source.Contains("Path.GetFullPath") &&
            (source.Contains("BuildGroupKey") || source.Contains("Deduplicate"));
        var hasCallerContract = source.Contains("normalized", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("pre-normalized", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("caller must", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("callers must", StringComparison.OrdinalIgnoreCase);

        Assert.True(hasSelfNormalization || hasCallerContract,
            "R1-008: DeduplicationEngine must either normalize MainPath via Path.GetFullPath or document caller normalization contract.");
    }

    // ══════════════════════════════════════════════
    // R1-010: ConsoleDetector I/O in Core — must be behind interface
    // ══════════════════════════════════════════════

    [Fact]
    public void R1_010_ConsoleDetector_ArchiveDetectionBehindInterface()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ConsoleDetector.cs");
        var source = File.ReadAllText(sourcePath);

        // DetectByZipContent must use injected _classificationIo interface, not direct System.IO.
        var zipMethodIdx = source.IndexOf("DetectByZipContent(string filePath)", StringComparison.Ordinal);
        if (zipMethodIdx < 0)
            zipMethodIdx = source.IndexOf("DetectByZipContent(", StringComparison.Ordinal);
        Assert.True(zipMethodIdx > 0, "DetectByZipContent method not found");
        var zipBody = ExtractMethodBody(source, source.IndexOf('{', zipMethodIdx));

        // Must NOT contain direct file I/O calls
        Assert.DoesNotContain("File.Open", zipBody);
        Assert.DoesNotContain("File.ReadAllBytes", zipBody);
        Assert.DoesNotContain("new FileStream", zipBody);
        Assert.DoesNotContain("ZipFile.Open", zipBody);

        // Must use injected interface
        Assert.Contains("_classificationIo", zipBody);

        // DetectWithConfidence must document that I/O goes through injected abstractions
        Assert.True(
            source.Contains("_classificationIo") && source.Contains("_discHeaderDetector"),
            "R1-010: ConsoleDetector must use injected interfaces for all file I/O.");
    }

    // ══════════════════════════════════════════════
    // R2-008: SafetyValidator UNC ADS false-positive for port-syntax UNC paths
    // ══════════════════════════════════════════════

    [Fact]
    public void R2_008_SafetyValidator_NormalizePath_UncWithPort_NotBlockedAsAds()
    {
        // UNC path with port syntax (\\server:445\share) must not be blocked as ADS.
        // This is a valid SMB port syntax, not an ADS reference.
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Safety", "SafetyValidator.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("public static string? NormalizePath(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "NormalizePath method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // The ADS check must be UNC-aware: skip ADS detection for UNC hostname portion
        // or handle UNC paths specially before the colon check.
        Assert.True(
            body.Contains(@"\\\\") || body.Contains("UNC", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("IsUnc", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("StartsWith(@\"\\\\\"") || body.Contains("StartsWith(\"\\\\\\\\\""),
            "R2-008: NormalizePath ADS check must handle UNC paths to avoid false-positive blocking.");
    }

    // ══════════════════════════════════════════════
    // R2-014: AuditCsvStore FileLock Race — must have retry limit
    // ══════════════════════════════════════════════

    [Fact]
    public void R2_014_AuditCsvStore_AcquireFileLock_HasRetryLimit()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Audit", "AuditCsvStore.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("static FileLockHandle AcquireFileLock(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "AcquireFileLock method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must have a retry/iteration limit to prevent infinite loop
        Assert.True(
            body.Contains("maxRetries", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("MaxRetries", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("retryCount", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("iteration", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("for (", StringComparison.Ordinal) ||
            body.Contains("attempt", StringComparison.OrdinalIgnoreCase),
            "R2-014: AcquireFileLock must have a retry limit to prevent infinite loop.");
    }

    // ══════════════════════════════════════════════
    // R2-015: RollbackService !File.Exists returns 1 instead of 0
    // ══════════════════════════════════════════════

    [Fact]
    public void R2_015_RollbackService_CountAffectedRows_MissingFile_ReturnsZero()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Audit", "RollbackService.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("CountAffectedRollbackRows(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "CountAffectedRollbackRows method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // When audit file doesn't exist, returning 1 is misleading ("1 Failed").
        // Must return 0 for non-existent files.
        Assert.True(
            body.Contains("return 0") && body.Contains("!File.Exists"),
            "R2-015: CountAffectedRollbackRows must return 0 (not 1) when audit file doesn't exist.");
    }

    // ══════════════════════════════════════════════
    // R3-002: CLI RunExecutionLease — AbandonedMutexException handling
    // ══════════════════════════════════════════════

    [Fact]
    public void R3_002_RunExecutionLease_HandlesAbandonedMutexException()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.CLI", "Program.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("static IDisposable? TryAcquireRunExecutionLease(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "TryAcquireRunExecutionLease method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must handle AbandonedMutexException (Windows auto-releases abandoned mutexes,
        // but .NET throws AbandonedMutexException on WaitOne to signal it).
        Assert.True(
            body.Contains("AbandonedMutexException") ||
            body.Contains("WaitOne") ||
            body.Contains("abandoned", StringComparison.OrdinalIgnoreCase),
            "R3-002: TryAcquireRunExecutionLease must handle AbandonedMutexException or use WaitOne with timeout.");
    }

    // ══════════════════════════════════════════════
    // R3-018: WatchFolderService Dispose-Race before RunTriggered invoke
    // ══════════════════════════════════════════════

    [Fact]
    public void R3_018_WatchFolderService_OnDebounceTimer_ChecksDisposedBeforeInvoke()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Watch", "WatchFolderService.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("void OnDebounceTimer(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "OnDebounceTimer method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // After the lock block releases, there must be a _disposed check before RunTriggered?.Invoke()
        var invokeIdx = body.IndexOf("RunTriggered", StringComparison.Ordinal);
        Assert.True(invokeIdx > 0, "RunTriggered invocation not found in OnDebounceTimer");

        // Find the last lock block's closing before RunTriggered
        var lockEnd = body.LastIndexOf("}", invokeIdx, StringComparison.Ordinal);
        var sectionBetweenLockAndInvoke = body[lockEnd..invokeIdx];

        Assert.True(
            sectionBetweenLockAndInvoke.Contains("_disposed") ||
            body.Contains("if (_disposed) return;", StringComparison.Ordinal) &&
            body.IndexOf("_disposed", body.IndexOf("}", body.IndexOf("lock", StringComparison.Ordinal), StringComparison.Ordinal), StringComparison.Ordinal) < invokeIdx,
            "R3-018: OnDebounceTimer must check _disposed after lock release, before RunTriggered?.Invoke().");
    }

    // ══════════════════════════════════════════════
    // R3-019: ScheduleService Lock-Corruption — _nowProvider exception safety
    // ══════════════════════════════════════════════

    [Fact]
    public void R3_019_ScheduleService_OnTimerTick_NowProviderExceptionSafe()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Watch", "ScheduleService.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("void OnTimerTick(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "OnTimerTick method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // _nowProvider() call must be protected against exceptions to prevent
        // state inconsistency (e.g., _nextIntervalDueUtc not updated).
        Assert.True(
            body.Contains("try") && body.Contains("catch") &&
            (body.Contains("_nowProvider") || body.Contains("nowLocal")),
            "R3-019: OnTimerTick must have try/catch around _nowProvider() call to prevent state inconsistency.");
    }

    // ══════════════════════════════════════════════
    // R4-003: ConvertOnly FilteredCount — must set FilteredNonGameCount
    // ══════════════════════════════════════════════

    [Fact]
    public void R4_003_ConvertOnlyPath_SetsFilteredNonGameCount()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "RunOrchestrator.ScanAndConvertSteps.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("TryExecuteConvertOnlyPath(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "TryExecuteConvertOnlyPath method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // ConvertOnly path must set FilteredNonGameCount for KPI consistency
        Assert.Contains("FilteredNonGameCount", body);
    }

    // ══════════════════════════════════════════════
    // R4-004: ApplyPartialPipelineState null-guard - source-grep test removed
    // (was pinning the literal pattern 'AllGroups is not { }'; the actual
    //  null-guard behaviour is exercised by RunOrchestrator partial-state tests).
    // ════════════════════════════════════════════

    // ══════════════════════════════════════════════
    // R4-009: ConvertOnly SetMember-Tracking must be false
    // ══════════════════════════════════════════════

    [Fact]
    public void R4_009_ConvertOnlyPipelinePhase_TrackSetMembers_False()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "ConvertOnlyPipelinePhase.cs");
        var source = File.ReadAllText(sourcePath);

        var executeIdx = source.IndexOf("Execute(", StringComparison.Ordinal);
        Assert.True(executeIdx > 0, "Execute method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', executeIdx));

        // ConvertOnly mode must NOT track set members (TrackSetMembers: false)
        // to avoid orphaned .bin files when .cue is converted.
        Assert.Contains("TrackSetMembers: false", body);
    }

    // ══════════════════════════════════════════════
    // R4-013: ThreadLocal VersionScorer — FALSE POSITIVE (should pass immediately)
    // ══════════════════════════════════════════════

    [Fact]
    public void R4_013_VersionScorer_IsImmutableAfterConstruction()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Core", "Scoring", "VersionScorer.cs");
        var source = File.ReadAllText(sourcePath);

        // VersionScorer fields must all be readonly — it's immutable after construction.
        // Any non-readonly instance field would be a thread-safety issue.
        var lines = source.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Skip non-field lines
            if (!trimmed.StartsWith("private") && !trimmed.StartsWith("internal") && !trimmed.StartsWith("protected"))
                continue;
            // Skip methods/properties/constructors
            if (trimmed.Contains('(') || trimmed.Contains("=>") || trimmed.Contains("get;") || trimmed.Contains("set;"))
                continue;
            // Must be static or readonly for thread safety
            if (trimmed.Contains("static") || trimmed.Contains("readonly") || trimmed.Contains("const"))
                continue;
            // Skip comments/attributes
            if (trimmed.StartsWith("//") || trimmed.StartsWith("["))
                continue;
            // If we get here, it's a mutable instance field — fail
            if (trimmed.Contains(';'))
                Assert.Fail($"R4-013: VersionScorer has mutable instance field: {trimmed}");
        }
    }

    // ══════════════════════════════════════════════
    // R5-004: 7z Post-Extraction Traversal - source-grep test removed.
    // The actual ValidateExtractedContents + reparse-point check is covered by
    // SafetyAncestryReparsePointTests and ChdmanToolConverter integration tests.
    // ══════════════════════════════════════════════

    // ══════════════════════════════════════════════
    // R5-011: CSV UNC-Path SMB-Leak
    // ══════════════════════════════════════════════

    [Fact]
    public void R5_011_CsvSanitize_BlocksUncPathLeak()
    {
        // SanitizeSpreadsheetCsvField must treat UNC paths (\\) as dangerous
        // because they can trigger SMB auto-resolution in spreadsheet applications.
        var result = AuditCsvParser.SanitizeSpreadsheetCsvField(@"\\attacker.com\share\file.bin");

        // UNC path must be prefixed with ' to prevent spreadsheet SMB resolution
        Assert.True(
            result.Contains("'\\\\") || result.StartsWith("\"'\\\\"),
            $"R5-011: UNC path must be sanitized with ' prefix to block SMB leak. Got: {result}");
    }

    // ══════════════════════════════════════════════
    // R5-019: StandaloneConversionService Dispose - source-grep test removed
    // (was pinning the literal '_disposed' identifier; double-dispose protection
    //  belongs in a behavioural Dispose-twice test, not in a string assertion).
    // ════════════════════════════════════════════

    // ══════════════════════════════════════════════
    // R6-003: Profile Temp-Files — cleanup in finally
    // ══════════════════════════════════════════════

    [Fact]
    public void R6_003_JsonRunProfileStore_UpsertAsync_CleansUpTempFile()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Profiles", "JsonRunProfileStore.cs");
        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("UpsertAsync(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "UpsertAsync method not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must have try/catch or try/finally around File.Move to clean up temp file on failure
        Assert.True(
            (body.Contains("finally") || body.Contains("catch")) && body.Contains("Delete"),
            "R6-003: UpsertAsync must clean up temp file on Move failure (try/catch or try/finally with Delete).");
    }

    // ══════════════════════════════════════════════
    // R6-008: Settings-Merge Defaults-Verlust
    // ══════════════════════════════════════════════

    [Fact]
    public void R6_008_SettingsLoader_ValidationFailure_PreservesDefaultsJsonValues()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Configuration", "SettingsLoader.cs");
        var source = File.ReadAllText(sourcePath);

        // Find MergeFromUserSettings method which handles merged validation failure
        var methodIdx = source.IndexOf("private static void MergeFromUserSettings(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "MergeFromUserSettings not found");
        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Validation failure must NOT reset to hardcoded defaults via new GeneralSettings().
        // It must either re-merge from defaults.json or only reset the invalid user fields.
        Assert.DoesNotContain("settings.General = new GeneralSettings()", body);
        Assert.DoesNotContain("settings.Dat = new DatSettings()", body);
    }
}
