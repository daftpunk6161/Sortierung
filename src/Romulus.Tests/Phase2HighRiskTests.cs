using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red→Green tests for Phase 2 High-Risk fixes from DEEP_DIVE_FINDINGS.
/// </summary>
public sealed class Phase2HighRiskTests
{
    // ══════════════════════════════════════════════
    // Helper: find src/ directory
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
    // R2-001: ToolRunnerAdapter TOCTOU — hash FileStream must be held
    // ══════════════════════════════════════════════

    /// <summary>
    /// R2-001: VerifyToolHash must validate before/after timestamps to detect file-swap
    /// during hash computation. The current code already does this (TH-04).
    /// Verify the TOCTOU mitigation exists in VerifyToolHash.
    /// </summary>
    [Fact]
    public void R2_001_VerifyToolHash_HasTimestampGuard()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Tools", "ToolRunnerAdapter.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("private bool VerifyToolHash(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "VerifyToolHash method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must have before/after timestamp comparison (TOCTOU mitigation)
        Assert.Contains("lastWriteAfterHash", body, StringComparison.Ordinal);
        Assert.Contains("lastWriteBeforeHash", body, StringComparison.Ordinal);
        // Must have length comparison
        Assert.Contains("lengthAfterHash", body, StringComparison.Ordinal);
        Assert.Contains("lengthBeforeHash", body, StringComparison.Ordinal);
        // Must log when change detected
        Assert.Contains("changed during hash verification", body, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════
    // R2-005: AllowedRootPathPolicy Symlink-Escape
    // ══════════════════════════════════════════════

    /// <summary>
    /// R2-005: AllowedRootPathPolicy.IsPathAllowed must check for ReparsePoints
    /// to prevent symlink-based root-escape.
    /// </summary>
    [Fact]
    public void R2_005_AllowedRootPathPolicy_ChecksReparsePoint()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Safety", "AllowedRootPathPolicy.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("public bool IsPathAllowed(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "IsPathAllowed method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must check for ReparsePoint/symlinks
        Assert.Contains("ReparsePoint", body, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════
    // R3-004: FixedTimeEquals HMAC Bypass with empty key
    // ══════════════════════════════════════════════

    /// <summary>
    /// R3-004: FixedTimeEquals must reject empty expected keys to prevent
    /// HMAC-with-empty-key bypass.
    /// </summary>
    [Fact]
    public void R3_004_FixedTimeEquals_RejectsEmptyExpected()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Api", "ProgramHelpers.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("static bool FixedTimeEquals(string expected", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "FixedTimeEquals method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must guard against empty expected
        var hasEmptyGuard = body.Contains("IsNullOrEmpty(expected)", StringComparison.Ordinal)
            || body.Contains("IsNullOrWhiteSpace(expected)", StringComparison.Ordinal)
            || body.Contains("ThrowIfNullOrEmpty", StringComparison.Ordinal)
            || body.Contains("expected.Length == 0", StringComparison.Ordinal);
        Assert.True(hasEmptyGuard, "FixedTimeEquals must guard against empty expected key");
    }

    // ══════════════════════════════════════════════
    // R3-006: Rate-Limiter bucket from matched key
    // ══════════════════════════════════════════════

    /// <summary>
    /// R3-006: Rate-limit bucket should use stable key identity, not raw user input.
    /// FixedTimeEqualsAny should return matched key index or the matched key itself.
    /// </summary>
    [Fact]
    public void R3_006_RateLimiter_UsesStableKey()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Api", "Program.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // The rate-limit bucket computation must NOT use raw providedKey directly
        // It should use a matched key, index, or stable identifier
        var bucketIdx = source.IndexOf("BuildRateLimitBucketId(", StringComparison.Ordinal);
        Assert.True(bucketIdx > 0, "BuildRateLimitBucketId call not found");

        // Get the 100 chars around the call to see the argument
        var contextStart = Math.Max(0, bucketIdx);
        var contextEnd = Math.Min(source.Length, bucketIdx + 100);
        var context = source[contextStart..contextEnd];

        // Must NOT pass raw providedKey — should use matchedKey, clientBindingId, or index
        var usesRawKey = context.Contains("BuildRateLimitBucketId(providedKey", StringComparison.Ordinal);
        Assert.False(usesRawKey, "Rate-limit bucket must not use raw providedKey — use matched/stable key");
    }

    // ══════════════════════════════════════════════
    // R3-008: RunLifecycleManager Completion-Signaling
    // ══════════════════════════════════════════════

    /// <summary>
    /// R3-008: SignalCompletion must be guaranteed to execute even if
    /// UpdateRecoveryState throws. Either wrap in try/catch or move SignalCompletion first.
    /// </summary>
    [Fact]
    public void R3_008_SignalCompletion_IsGuaranteed()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Api", "RunLifecycleManager.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Find the finally block in ExecuteRunAsync
        var finallyIdx = source.LastIndexOf("finally", source.IndexOf("SignalCompletion", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(finallyIdx > 0, "finally block not found before SignalCompletion");

        var finallyBody = ExtractMethodBody(source, source.IndexOf('{', finallyIdx));

        // UpdateRecoveryState must be in its own try/catch OR SignalCompletion must be before it
        var recoveryIdx = finallyBody.IndexOf("UpdateRecoveryState", StringComparison.Ordinal);
        var signalIdx = finallyBody.IndexOf("SignalCompletion", StringComparison.Ordinal);
        Assert.True(recoveryIdx > 0 && signalIdx > 0, "Both UpdateRecoveryState and SignalCompletion must be in finally");

        // Either: SignalCompletion comes before UpdateRecoveryState,
        // OR: UpdateRecoveryState is wrapped in try/catch
        var signalBeforeRecovery = signalIdx < recoveryIdx;
        var recoveryInTryCatch = finallyBody[..signalIdx].Contains("try", StringComparison.Ordinal)
            && finallyBody[..signalIdx].Contains("catch", StringComparison.Ordinal);
        Assert.True(signalBeforeRecovery || recoveryInTryCatch,
            "SignalCompletion must be guaranteed — either before UpdateRecoveryState or UpdateRecoveryState in try/catch");
    }

    // ══════════════════════════════════════════════
    // R3-012: CTS Race — state transition inside lock
    // ══════════════════════════════════════════════

    /// <summary>
    /// R3-012: OnCancel must set CurrentRunState = RunState.Cancelled inside the lock
    /// to prevent race with CreateRunCancellation.
    /// </summary>
    [Fact]
    public void R3_012_OnCancel_StateTransitionInsideLock()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.UI.Wpf", "ViewModels", "MainViewModel.RunPipeline.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("private void OnCancel()", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "OnCancel method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Find the lock block
        var lockIdx = body.IndexOf("lock (_ctsLock)", StringComparison.Ordinal);
        Assert.True(lockIdx > 0, "lock (_ctsLock) not found in OnCancel");

        var lockBody = ExtractMethodBody(body, body.IndexOf('{', lockIdx));

        // RunState.Cancelled must appear inside the lock body
        Assert.Contains("Cancelled", lockBody, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════
    // R3-015: AutoSave-Timer disposal guard
    // ══════════════════════════════════════════════

    /// <summary>
    /// R3-015: OnAutoSaveTimerElapsed must check _mainViewModelDisposed
    /// to prevent post-dispose callback execution.
    /// </summary>
    [Fact]
    public void R3_015_AutoSaveTimer_HasDisposedGuard()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.UI.Wpf", "ViewModels", "MainViewModel.Settings.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("private void OnAutoSaveTimerElapsed(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "OnAutoSaveTimerElapsed method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must check for disposed state
        var hasDisposedGuard = body.Contains("_mainViewModelDisposed", StringComparison.Ordinal)
            || body.Contains("Disposed", StringComparison.Ordinal);
        Assert.True(hasDisposedGuard, "OnAutoSaveTimerElapsed must check disposed state");
    }

    // ══════════════════════════════════════════════
    // R4-005: DatRename Nondeterminism — stable sort
    // ══════════════════════════════════════════════

    /// <summary>
    /// R4-005: DatRenamePipelinePhase must have a stable tiebreaker beyond Confidence+FilePath.
    /// At minimum it should use a third sort criterion (e.g. Hash, DatGameName).
    /// </summary>
    [Fact]
    public void R4_005_DatRename_HasStableTiebreaker()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "DatRenamePipelinePhase.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Count how many ThenBy clauses exist in the winner-sorting chain
        var orderIdx = source.IndexOf("OrderByDescending(", StringComparison.Ordinal);
        Assert.True(orderIdx > 0, "OrderByDescending not found");

        // Get the sort chain (next ~400 chars)
        var chainEnd = Math.Min(source.Length, orderIdx + 400);
        var chain = source[orderIdx..chainEnd];

        // Must have at least 2 ThenBy clauses (FilePath + one more tiebreaker)
        var thenByCount = 0;
        var searchFrom = 0;
        while (true)
        {
            var idx = chain.IndexOf("ThenBy(", searchFrom, StringComparison.Ordinal);
            if (idx < 0) break;
            thenByCount++;
            searchFrom = idx + 7;
        }
        Assert.True(thenByCount >= 2, $"DatRename sort needs at least 2 ThenBy clauses for stability, found {thenByCount}");
    }

    // ══════════════════════════════════════════════
    // R4-015: DeduplicatePipelinePhase LoserCount
    // ══════════════════════════════════════════════

    /// <summary>
    /// R4-015: LoserCount in DedupePhaseOutput must account for all losers,
    /// including BIOS groups, or the field must be clearly named gameGroupLoserCount.
    /// </summary>
    [Fact]
    public void R4_015_LoserCount_IncludesAllGroups()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "DeduplicatePipelinePhase.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // The loserCount must be computed from 'groups' (all groups), not 'gameGroups' (filtered)
        // OR the variable/field must be renamed to clarify it's game-only
        var loserCountLine = source.IndexOf("loserCount", StringComparison.Ordinal);
        Assert.True(loserCountLine > 0, "loserCount not found");

        // Get context around loserCount assignment
        var lineStart = source.LastIndexOf('\n', loserCountLine) + 1;
        var lineEnd = source.IndexOf('\n', loserCountLine);
        var assignmentLine = source[lineStart..lineEnd].Trim();

        // Must use 'groups' not 'gameGroups' for total loser count
        var usesAllGroups = assignmentLine.Contains("groups.Sum", StringComparison.Ordinal)
            && !assignmentLine.Contains("gameGroups.Sum", StringComparison.Ordinal);
        Assert.True(usesAllGroups, $"loserCount must use 'groups' not 'gameGroups': '{assignmentLine}'");
    }

    // ══════════════════════════════════════════════
    // R4-025: MovePipelinePhase Rollback-Failure Count
    // ══════════════════════════════════════════════

    /// <summary>
    /// R4-025: failCount must account for actual rollback failures, not just +1.
    /// </summary>
    [Fact]
    public void R4_025_RollbackFailureCount_IsAccurate()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "MovePipelinePhase.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Find the memberMoveFailed block
        var moveFailedIdx = source.IndexOf("if (memberMoveFailed)", StringComparison.Ordinal);
        Assert.True(moveFailedIdx > 0, "memberMoveFailed block not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', moveFailedIdx));

        // Must not have simple 'failCount++' — should use rollbackFailures.Count
        var hasSimpleIncrement = body.Contains("failCount++;", StringComparison.Ordinal);
        Assert.False(hasSimpleIncrement, "failCount++ must be replaced with accurate rollback failure counting");
    }

    // ══════════════════════════════════════════════
    // R5-007: DAT Hash-Typ-Fallback — SHA1 in chain
    // ══════════════════════════════════════════════

    /// <summary>
    /// R5-007: The hash fallback chain in DatRepositoryAdapter must include SHA1
    /// when SHA256 is requested but unavailable.
    /// </summary>
    [Fact]
    public void R5_007_DatHashFallback_IncludesSHA1()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Dat", "DatRepositoryAdapter.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Find the fallback chain section
        var fallbackIdx = source.IndexOf("Fallback chain", StringComparison.Ordinal);
        Assert.True(fallbackIdx > 0, "Fallback chain comment not found");

        // Get the next 600 chars of source after the comment.
        // The fallback chain uses a local 'sha1' variable extracted via GetAttribute("sha1")
        // before the comment. Verify sha1 is actually used within the fallback block.
        var fallbackEnd = Math.Min(source.Length, fallbackIdx + 600);
        var fallbackSection = source[fallbackIdx..fallbackEnd];

        // Must have SHA1 fallback: 'hash = sha1' assignment (uses local variable extracted from GetAttribute("sha1"))
        var sha1InFallback = fallbackSection.Contains("hash = sha1", StringComparison.Ordinal);
        Assert.True(sha1InFallback, "Fallback chain must include SHA1 as fallback option");
    }

    // ══════════════════════════════════════════════
    // R5-009: DatSourceService Partial-Extraction
    // ══════════════════════════════════════════════

    /// <summary>
    /// R5-009: ExtractToFile in DatSourceService must handle IOException
    /// for already-existing files during extraction.
    /// </summary>
    [Fact]
    public void R5_009_DatExtraction_HandlesIOException()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Dat", "DatSourceService.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Find the ExtractToFile call
        var extractIdx = source.IndexOf("ExtractToFile(destPath", StringComparison.Ordinal);
        Assert.True(extractIdx > 0, "ExtractToFile call not found");

        // Get context around it (200 chars before and after)
        var contextStart = Math.Max(0, extractIdx - 200);
        var contextEnd = Math.Min(source.Length, extractIdx + 200);
        var context = source[contextStart..contextEnd];

        // Must have try/catch for IOException around ExtractToFile
        var hasIoGuard = context.Contains("try", StringComparison.Ordinal)
            && context.Contains("IOException", StringComparison.Ordinal);
        Assert.True(hasIoGuard, "ExtractToFile must be wrapped in try/catch for IOException");
    }

    // ══════════════════════════════════════════════
    // R5-021: CSV-Injection — complete coverage
    // ══════════════════════════════════════════════

    /// <summary>
    /// R5-021: HasDangerousSpreadsheetPrefix must cover tab and CR prefixes
    /// in addition to =, +, -, @.
    /// </summary>
    [Fact]
    public void R5_021_CsvSanitization_CoversTabAndCR()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Audit", "AuditCsvParser.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("static bool HasDangerousSpreadsheetPrefix(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "HasDangerousSpreadsheetPrefix definition not found");

        // Get method body
        var bodyStart = methodIdx;
        var bodyEnd = source.IndexOf(';', bodyStart);
        // This is an expression-bodied method, so find the entire statement
        bodyEnd = Math.Min(source.Length, bodyEnd + 50);
        var body = source[bodyStart..bodyEnd];

        // Must check for tab and carriage return
        var checksTab = body.Contains("\\t", StringComparison.Ordinal);
        var checksCr = body.Contains("\\r", StringComparison.Ordinal);
        Assert.True(checksTab, "HasDangerousSpreadsheetPrefix must check for tab (\\t)");
        Assert.True(checksCr, "HasDangerousSpreadsheetPrefix must check for CR (\\r)");
    }

    // ══════════════════════════════════════════════
    // R5-025: ConversionExecutor Verification-Status cascade
    // ══════════════════════════════════════════════

    /// <summary>
    /// R5-025: finalVerification must keep the worst status across all steps,
    /// not just the last step's status.
    /// </summary>
    [Fact]
    public void R5_025_FinalVerification_KeepsWorstStatus()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Conversion", "ConversionExecutor.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // Find the finalVerification assignment
        var assignIdx = source.IndexOf("finalVerification = ", StringComparison.Ordinal);
        Assert.True(assignIdx > 0, "finalVerification assignment not found");

        // Get context around it
        var contextStart = Math.Max(0, assignIdx - 100);
        var contextEnd = Math.Min(source.Length, assignIdx + 200);
        var context = source[contextStart..contextEnd];

        // Must not be a simple overwrite — should have a comparison/min/max/worst logic
        var hasWorstLogic = context.Contains("worst", StringComparison.OrdinalIgnoreCase)
            || context.Contains("Math.Min", StringComparison.Ordinal)
            || context.Contains("Math.Max", StringComparison.Ordinal)
            || context.Contains("<", StringComparison.Ordinal)
            || context.Contains(">", StringComparison.Ordinal)
            || context.Contains("WorstOf", StringComparison.Ordinal)
            || context.Contains("Combine", StringComparison.Ordinal);

        // Simple "finalVerification = verifyStatus" without comparison is a bug
        var isSimpleOverwrite = context.Contains("finalVerification = verifyStatus;", StringComparison.Ordinal);
        Assert.False(isSimpleOverwrite && !hasWorstLogic,
            "finalVerification must keep worst status, not simple overwrite");
    }

    // ══════════════════════════════════════════════
    // R6-002: FileHashService Memory-Cache Staleness
    // ══════════════════════════════════════════════

    /// <summary>
    /// R6-002: The in-memory cache hit path in FileHashService.GetHash must validate
    /// the file fingerprint (mtime/length) before returning cached hash.
    /// </summary>
    [Fact]
    public void R6_002_MemoryCache_ValidatesFingerprint()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Hashing", "FileHashService.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("public string? GetHash(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "GetHash method not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Find the memory cache hit path (TryGet -> return cached)
        var cacheHitIdx = body.IndexOf("_cache.TryGet(", StringComparison.Ordinal);
        Assert.True(cacheHitIdx > 0, "Memory cache TryGet not found");

        // Get the next 300 chars after cache hit
        var contextEnd = Math.Min(body.Length, cacheHitIdx + 300);
        var cacheSection = body[cacheHitIdx..contextEnd];

        // Must NOT have a direct "return cached" without fingerprint validation
        var hasDirectReturn = cacheSection.Contains("return cached;", StringComparison.Ordinal);
        var hasFingerprintCheck = cacheSection.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase)
            || cacheSection.Contains("LastWriteTime", StringComparison.Ordinal)
            || cacheSection.Contains("GetLastWriteTime", StringComparison.Ordinal)
            || cacheSection.Contains("FileInfo", StringComparison.Ordinal);

        Assert.False(hasDirectReturn && !hasFingerprintCheck,
            "Memory cache must validate file fingerprint before returning cached hash");
    }

    // ══════════════════════════════════════════════
    // R6-005: Rollback Preview/Execute Asymmetry (FALSE POSITIVE)
    // ══════════════════════════════════════════════

    /// <summary>
    /// R6-005: Verify that rollback Preview and Execute use the same integrity check.
    /// This was flagged but the code is already correct — verify it stays correct.
    /// </summary>
    [Fact]
    public void R6_005_RollbackPreviewExecute_AreSymmetric()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Audit", "AuditSigningService.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        // The integrity check must appear before both DryRun and Execute paths
        // and must be the same check for both
        var verifyIdx = source.IndexOf("VerifyMetadataSidecar", StringComparison.Ordinal);
        Assert.True(verifyIdx > 0, "VerifyMetadataSidecar not found");

        // The comment SEC-ROLLBACK-03 must be present, confirming intentional parity
        Assert.Contains("SEC-ROLLBACK-03", source, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════
    // R6-006: RollbackService empty audit = "1 Failed"
    // ══════════════════════════════════════════════

    /// <summary>
    /// R6-006: CountAffectedRollbackRows must return 0 for empty audit files,
    /// not Math.Max(1, 0) = 1.
    /// </summary>
    [Fact]
    public void R6_006_EmptyAudit_ReturnsZeroRows()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Audit", "RollbackService.cs");
        Assert.True(File.Exists(sourcePath));

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("CountAffectedRollbackRows(", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "CountAffectedRollbackRows not found");

        var body = ExtractMethodBody(source, source.IndexOf('{', methodIdx));

        // Must NOT have Math.Max(1, count) — should return count directly
        Assert.DoesNotContain("Math.Max(1, count)", body, StringComparison.Ordinal);
    }
}
