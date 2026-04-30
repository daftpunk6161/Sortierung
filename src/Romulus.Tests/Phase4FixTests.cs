using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red→Green tests for remaining R1-R6 findings that NEED FIX.
/// Structural source-scan approach: read .cs files, assert expected patterns.
/// </summary>
public sealed class Phase4FixTests
{
    private static string FindSrcDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "Romulus.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException("Could not find src/ directory.");
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var idx = source.IndexOf(signature, StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        var braceIdx = source.IndexOf('{', idx);
        if (braceIdx < 0) return string.Empty;
        var depth = 0;
        for (int i = braceIdx; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0) return source[braceIdx..(i + 1)];
        }
        return source[braceIdx..];
    }

    // ═══════════════════════════════════════
    // R2-010: HeaderRepairService retry on locked files
    // ═══════════════════════════════════════
    [Fact]
    public void R2_010_HeaderRepairService_HasRetryOnLockedFiles()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Hashing", "HeaderRepairService.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public bool RepairNesHeader(");
        // Must have retry logic (loop + IOException catch) around File.Open
        Assert.True(
            method.Contains("retry", StringComparison.OrdinalIgnoreCase)
            || (method.Contains("for (") && method.Contains("IOException"))
            || (method.Contains("while") && method.Contains("IOException")),
            "R2-010: RepairNesHeader must have retry loop for locked files");
    }

    // ═══════════════════════════════════════
    // R2-013: FileHashService TTL staleness check
    // ═══════════════════════════════════════
    [Fact]
    public void R2_013_FileHashService_PersistentCacheHasTTL()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Hashing", "FileHashService.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "private bool TryGetPersistedHash(");
        // Must check entry age (RecordedUtcTicks, TimeSpan, Days)
        Assert.True(
            method.Contains("RecordedUtc", StringComparison.OrdinalIgnoreCase)
            || method.Contains("Days", StringComparison.OrdinalIgnoreCase)
            || method.Contains("TimeSpan", StringComparison.OrdinalIgnoreCase),
            "R2-013: TryGetPersistedHash must check entry age (TTL)");
    }

    // ═══════════════════════════════════════
    // R2-016: FileSystemAdapter NFC cache
    // ═══════════════════════════════════════
    [Fact]
    public void R2_016_FileSystemAdapter_NfcNormalizationHasCache()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "FileSystem", "FileSystemAdapter.cs");
        var source = File.ReadAllText(path);
        // NormalizePathNfc area must have a cache (ConcurrentDictionary, Dictionary, or dedicated cache)
        var nfcIdx = source.IndexOf("NormalizePathNfc", StringComparison.Ordinal);
        Assert.True(nfcIdx >= 0, "R2-016: NormalizePathNfc method must exist");
        var area = source.Substring(Math.Max(0, nfcIdx - 500), Math.Min(1000, source.Length - Math.Max(0, nfcIdx - 500)));
        Assert.True(
            area.Contains("_nfcCache", StringComparison.OrdinalIgnoreCase)
            || area.Contains("Cache", StringComparison.Ordinal)
            || area.Contains("Dictionary", StringComparison.Ordinal),
            "R2-016: NormalizePathNfc must use a cache to avoid redundant normalization");
    }

    // ═══════════════════════════════════════
    // R3_009 (DashboardDataBuilder null datRoot pin),
    // R3_017 (FeatureCommandService.Data _datUpdateRunning try/finally pin),
    // R4_017 (RunOrchestrator TryFlushHashCache signature pin),
    // R4_023 (RunResultBuilder IsPartial property-name pin):
    // removed per testing.instructions.md - pure source-string-grep, no
    // behavioural assertion, broke on any harmless rename or refactor.
    // ═══════════════════════════════════════

    // ═══════════════════════════════════════
    // R3-020: ScheduleService FlushPending IsBusyCheck exception
    // ═══════════════════════════════════════
    [Fact]
    public void R3_020_ScheduleService_FlushPendingProtectsIsBusyCheck()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Watch", "ScheduleService.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public void FlushPendingIfNeeded(");
        // IsBusyCheck invocation must be protected by try/catch
        Assert.True(
            method.Contains("try") && method.Contains("catch"),
            "R3-020: FlushPendingIfNeeded must have try/catch around IsBusyCheck");
    }

    // ═══════════════════════════════════════
    // R4-010: PhasePlanExecutor stores failure details
    // ═══════════════════════════════════════
    [Fact]
    public void R4_010_PhasePlanExecutor_StoresFailedPhaseInfo()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "PhasePlanExecutor.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public void Execute(");
        // When a phase fails, must store info in pipelineState (not just log+break)
        Assert.True(
            method.Contains("pipelineState.SetFailedPhase", StringComparison.OrdinalIgnoreCase)
            || method.Contains("pipelineState.FailedPhase", StringComparison.OrdinalIgnoreCase)
            || method.Contains("pipelineState.LastError", StringComparison.OrdinalIgnoreCase)
            || method.Contains("FailedPhaseName", StringComparison.OrdinalIgnoreCase),
            "R4-010: PhasePlanExecutor must store failed phase info in PipelineState");
    }

    // ═══════════════════════════════════════
    // R4-016: DatRenameResult includes collision count
    // ═══════════════════════════════════════
    [Fact]
    public void R4_016_DatRenameResult_HasCollisionCount()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "DatRenamePipelinePhase.cs");
        var source = File.ReadAllText(path);
        Assert.True(
            source.Contains("CollisionCount", StringComparison.OrdinalIgnoreCase)
            || source.Contains("collisionCount", StringComparison.Ordinal),
            "R4-016: DatRenameResult must include collision count");
    }

    // ═══════════════════════════════════════
    // R4-021: Extensions normalized to lowercase
    // ═══════════════════════════════════════
    [Fact]
    public void R4_021_RunOptionsBuilder_ExtensionsLowercased()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "RunOptionsBuilder.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public static RunOptions Normalize(");
        // Extensions must be lowercased (ToLowerInvariant or ToLower)
        var extArea = method;
        Assert.True(
            extArea.Contains("ToLowerInvariant", StringComparison.Ordinal)
            && (extArea.Contains("normalizedExtensions") || extArea.Contains("Extensions")),
            "R4-021: Extensions must be normalized to lowercase in RunOptionsBuilder.Normalize");
    }

    // ═══════════════════════════════════════
    // R4-024: PhasePlanning path normalization
    // ═══════════════════════════════════════
    [Fact]
    public void R4_024_PhasePlanning_PathsNormalizedBeforeMutation()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "PhasePlanning.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public void ApplyPathMutations(");
        // Paths should be normalized before insertion into mutation map
        Assert.True(
            method.Contains("GetFullPath", StringComparison.Ordinal)
            || method.Contains("NormalizePathNfc", StringComparison.Ordinal)
            || method.Contains("OrdinalIgnoreCase", StringComparison.Ordinal),
            "R4-024: ApplyPathMutations must normalize paths (GetFullPath/NormalizePathNfc/OrdinalIgnoreCase dict)");
    }

    // ═══════════════════════════════════════
    // R5-003: ReportGenerator CsvSafe no double quoting
    // ═══════════════════════════════════════
    [Fact]
    public void R5_003_ReportGenerator_CsvSafeNoDoubleQuoting()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Reporting", "ReportGenerator.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "private static string CsvSafe(");
        // Must not add quotes if SanitizeSpreadsheetCsvField already quoted
        // The method should check if already quoted or delegate fully to sanitizer
        Assert.True(
            method.Contains("StartsWith('\"')", StringComparison.Ordinal)
            || method.Contains("StartsWith(\"\\\"\")", StringComparison.Ordinal)
            || method.Contains("already", StringComparison.OrdinalIgnoreCase)
            || !method.Contains("Replace(\"\\\"\"", StringComparison.Ordinal),
            "R5-003: CsvSafe must not double-quote already sanitized values");
    }

    // ═══════════════════════════════════════
    // R5-006: ReportGenerator format string validation
    // ═══════════════════════════════════════
    [Fact]
    public void R5_006_ReportGenerator_FormatStringPlaceholderValidation()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Reporting", "ReportGenerator.cs");
        var source = File.ReadAllText(path);
        // The safe-format method must validate placeholder count or use try/catch
        Assert.True(
            source.Contains("FormatException", StringComparison.Ordinal)
            || source.Contains("placeholder", StringComparison.OrdinalIgnoreCase),
            "R5-006: Format method must handle FormatException or validate placeholders");
    }

    // ═══════════════════════════════════════
    // R5-017: ChdmanToolConverter temp in system temp
    // ═══════════════════════════════════════
    [Fact]
    public void R5_017_ChdmanToolConverter_TempExtractionUsesSystemTemp()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Conversion", "ChdmanToolConverter.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "private ConversionResult ConvertArchiveToChdman(");
        // extractDir should use Path.GetTempPath() or system temp
        Assert.True(
            method.Contains("GetTempPath", StringComparison.Ordinal)
            || method.Contains("TempPath", StringComparison.Ordinal),
            "R5-017: Archive extraction must use system temp dir, not source dir");
    }

    // ═══════════════════════════════════════
    // R5-020: ConversionOutputValidator magic bytes
    // ═══════════════════════════════════════
    [Fact]
    public void R5_020_ConversionOutputValidator_ChecksMagicBytes()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Conversion", "ConversionOutputValidator.cs");
        var source = File.ReadAllText(path);
        // Must have magic byte header validation infrastructure (R5-020)
        Assert.True(
            source.Contains("MagicHeader", StringComparison.OrdinalIgnoreCase)
            || source.Contains("magic", StringComparison.OrdinalIgnoreCase),
            "R5-020: ConversionOutputValidator must have magic byte header validation");
        Assert.True(
            source.Contains("ValidateMagicHeader", StringComparison.Ordinal),
            "R5-020: ConversionOutputValidator must expose ValidateMagicHeader method");
    }

    // ═══════════════════════════════════════
    // R6-007: RunProfileValidator Windows reserved names
    // ═══════════════════════════════════════
    [Fact]
    public void R6_007_RunProfileValidator_RejectsWindowsReservedNames()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Profiles", "RunProfileValidator.cs");
        var source = File.ReadAllText(path);
        Assert.True(
            source.Contains("CON", StringComparison.Ordinal)
            && source.Contains("PRN", StringComparison.Ordinal),
            "R6-007: RunProfileValidator must check for Windows reserved names (CON, PRN, etc.)");
    }

    // ═══════════════════════════════════════
    // R6-009: FileHashService tmp cleanup in FlushPersistentCache
    // ═══════════════════════════════════════
    [Fact]
    public void R6_009_FileHashService_FlushPersistentCacheTmpCleanup()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Hashing", "FileHashService.cs");
        var source = File.ReadAllText(path);
        var method = ExtractMethodBody(source, "public void FlushPersistentCache(");
        // Must have try/finally or try/catch around temp file write+move to clean up .tmp
        Assert.True(
            method.Contains("try") && (method.Contains("finally") || method.Contains("catch"))
            && method.Contains("Delete"),
            "R6-009: FlushPersistentCache must clean up .tmp files on failure");
    }
}
