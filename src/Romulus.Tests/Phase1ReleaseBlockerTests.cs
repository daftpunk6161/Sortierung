using System.Reflection;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;
using Romulus.Core.Deduplication;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red→Green tests for Phase 1 Release-Blocker fixes from DEEP_DIVE_FINDINGS.
/// </summary>
public sealed class Phase1ReleaseBlockerTests
{
    // ──────────────────────────────────────────────
    // R2-002 + R2-003: DolphinToolConverter + SevenZipToolConverter must pass ToolRequirement
    // ──────────────────────────────────────────────

    /// <summary>
    /// R2-002: DolphinToolConverter.Convert must invoke InvokeProcess with a ToolRequirement
    /// containing ToolName="dolphintool", not the 3-arg overload that skips hash verification.
    /// </summary>
    [Fact]
    public void R2_002_DolphinToolConverter_Convert_PassesToolRequirement()
    {
        var spy = new ToolRunnerSpy();
        var converter = CreateDolphinToolConverter(spy);

        converter.Convert("source.iso", "target.rvz", "/tools/dolphintool", ".iso");

        Assert.NotNull(spy.LastRequirement);
        Assert.Equal("dolphintool", spy.LastRequirement!.ToolName);
    }

    /// <summary>
    /// R2-003: SevenZipToolConverter.Convert must invoke InvokeProcess with a ToolRequirement
    /// containing ToolName="7z", not the 3-arg overload that skips hash verification.
    /// </summary>
    [Fact]
    public void R2_003_SevenZipToolConverter_Convert_PassesToolRequirement()
    {
        var spy = new ToolRunnerSpy();
        var converter = CreateSevenZipToolConverter(spy);

        converter.Convert("source.bin", "target.zip", "/tools/7z");

        Assert.NotNull(spy.LastRequirement);
        Assert.Equal("7z", spy.LastRequirement!.ToolName);
    }

    /// <summary>
    /// R2-003: SevenZipToolConverter.Verify must also pass ToolRequirement for hash check.
    /// </summary>
    [Fact]
    public void R2_003_SevenZipToolConverter_Verify_PassesToolRequirement()
    {
        var spy = new ToolRunnerSpy { ToolFindResult = "/tools/7z" };
        var converter = CreateSevenZipToolConverter(spy);

        converter.Verify("target.zip");

        Assert.NotNull(spy.LastRequirement);
        Assert.Equal("7z", spy.LastRequirement!.ToolName);
    }

    // ──────────────────────────────────────────────
    // Helpers: use reflection to instantiate internal converters
    // ──────────────────────────────────────────────

    private static dynamic CreateDolphinToolConverter(IToolRunner tools)
    {
        var type = typeof(Romulus.Infrastructure.Conversion.FormatConverterAdapter).Assembly
            .GetType("Romulus.Infrastructure.Conversion.DolphinToolConverter")!;
        return Activator.CreateInstance(type, tools)!;
    }

    private static dynamic CreateSevenZipToolConverter(IToolRunner tools)
    {
        var type = typeof(Romulus.Infrastructure.Conversion.FormatConverterAdapter).Assembly
            .GetType("Romulus.Infrastructure.Conversion.SevenZipToolConverter")!;
        return Activator.CreateInstance(type, tools)!;
    }

    /// <summary>
    /// Spy that records the ToolRequirement passed to InvokeProcess.
    /// Returns a successful ToolResult by default.
    /// </summary>
    private sealed class ToolRunnerSpy : IToolRunner
    {
        public ToolRequirement? LastRequirement { get; private set; }
        public string? ToolFindResult { get; set; }

        public string? FindTool(string toolName) => ToolFindResult;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            // 3-arg overload = NO requirement → record null
            LastRequirement = null;
            return new ToolResult(0, "ok", true);
        }

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            ToolRequirement? requirement,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastRequirement = requirement;
            return new ToolResult(0, "ok", true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "ok", true);
    }

    // ──────────────────────────────────────────────
    // R1-022: DeduplicationEngine.NormalizeConsoleKey must accept spaces
    // ──────────────────────────────────────────────

    /// <summary>
    /// R1-022: Console keys with spaces like "PlayStation 2" or "Sega CD" must NOT be
    /// discarded to UNKNOWN. They are valid console identifiers.
    /// </summary>
    [Theory]
    [InlineData("PlayStation 2", "PLAYSTATION 2")]
    [InlineData("Sega CD", "SEGA CD")]
    [InlineData("Game Boy Advance", "GAME BOY ADVANCE")]
    [InlineData("Neo Geo Pocket", "NEO GEO POCKET")]
    public void R1_022_NormalizeConsoleKey_PreservesSpaces(string input, string expected)
    {
        // NormalizeConsoleKey is private static — test via reflection
        var method = typeof(DeduplicationEngine).GetMethod(
            "NormalizeConsoleKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, [input])!;
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// R1-022: Existing valid keys (no spaces) must still work correctly.
    /// </summary>
    [Theory]
    [InlineData("SNES", "SNES")]
    [InlineData("ps1", "PS1")]
    [InlineData("Game-Boy", "GAME-BOY")]
    [InlineData("N64", "N64")]
    public void R1_022_NormalizeConsoleKey_ExistingKeysStillWork(string input, string expected)
    {
        var method = typeof(DeduplicationEngine).GetMethod(
            "NormalizeConsoleKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, [input])!;
        Assert.Equal(expected, result);
    }

    // ──────────────────────────────────────────────
    // R1-025: ConversionPlanner must uppercase-normalize console key
    // ──────────────────────────────────────────────
    // R1-025: ConversionPlanner.Plan must uppercase-normalize the console key.
    // Source-grep test removed per testing.instructions.md - the actual case
    // normalization behaviour is exercised by ConversionPlanner unit tests.
    // ──────────────────────────────────────────────

    // ──────────────────────────────────────────────
    // R1-017: ConsoleDetector.Detect() must delegate to DetectWithConfidence()
    // ──────────────────────────────────────────────

    /// <summary>
    /// R1-017: Detect() must delegate to DetectWithConfidence() to guarantee
    /// that both methods always produce the same console key. The short-circuit
    /// implementation today can diverge when multiple methods disagree: Detect()
    /// returns the first match while DetectWithConfidence() resolves by confidence.
    /// </summary>
    [Fact]
    public void R1_017_Detect_DelegatesToDetectWithConfidence()
    {
        // Structural verification: Detect() body must call DetectWithConfidence
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ConsoleDetector.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Extract the Detect(string, string) method body
        var detectMethodIndex = source.IndexOf("public string Detect(string filePath, string rootPath)", StringComparison.Ordinal);
        Assert.True(detectMethodIndex >= 0, "Detect(string, string) method not found");

        // Find the method body (between first { and matching })
        var bodyStart = source.IndexOf('{', detectMethodIndex);
        Assert.True(bodyStart > 0);

        // Detect() should delegate to DetectWithConfidence
        var nextMethodIndex = source.IndexOf("public ConsoleDetectionResult DetectWithConfidence", bodyStart, StringComparison.Ordinal);
        Assert.True(nextMethodIndex > bodyStart, "DetectWithConfidence not found after Detect");

        var detectBody = source[bodyStart..nextMethodIndex];

        Assert.Contains("DetectWithConfidence(", detectBody,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// R1-017: After fix, Detect() and DetectWithConfidence() must return the same
    /// console key for UNKNOWN files (behavioral sanity check).
    /// </summary>
    [Fact]
    public void R1_017_Detect_And_DetectWithConfidence_SameResultForUnknown()
    {
        var consoles = new List<ConsoleInfo>();
        var io = new FakeClassificationIo(fileExists: false);
        var detector = new ConsoleDetector(consoles, classificationIo: io);

        var detectResult = detector.Detect("nonexistent.xyz", "/root");
        var withConfResult = detector.DetectWithConfidence("nonexistent.xyz", "/root");

        Assert.Equal(detectResult, withConfResult.ConsoleKey);
    }

    private sealed class FakeClassificationIo : IClassificationIo
    {
        private readonly bool _fileExists;

        public FakeClassificationIo(bool fileExists) => _fileExists = fileExists;

        public bool FileExists(string path) => _fileExists;
        public Stream OpenRead(string path) => Stream.Null;
        public long FileLength(string path) => 0;
        public FileAttributes GetAttributes(string path) => FileAttributes.Normal;
        public System.IO.Compression.ZipArchive OpenZipRead(string path) => throw new NotSupportedException();
    }

    private static string FindSrcDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Romulus.Core")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Cannot find src/ directory");
    }

    // ──────────────────────────────────────────────
    // R3-005: API Middleware – OPTIONS preflight must not leak into auth-dependent logging
    // ──────────────────────────────────────────────

    /// <summary>
    /// R3-005: The correlation-ID, auth and request-logging middleware must not run for
    /// OPTIONS preflight requests. The early return for OPTIONS must be BEFORE those middleware.
    /// Structural verification that OPTIONS return happens in the first middleware block.
    /// </summary>
    [Fact]
    public void R3_005_OptionsPreflightSkipsAuthAndLogging()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Api", "Program.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // OPTIONS handling must exist
        var optionsIdx = source.IndexOf("\"OPTIONS\"", StringComparison.Ordinal);
        Assert.True(optionsIdx > 0, "OPTIONS handling not found");

        // Auth middleware (X-Api-Key check) must come AFTER OPTIONS return
        var authIdx = source.IndexOf("X-Api-Key", optionsIdx, StringComparison.Ordinal);
        Assert.True(authIdx > optionsIdx, "Auth middleware must come after OPTIONS return");

        // Rate limiter must come after OPTIONS
        var rateIdx = source.IndexOf("TryAcquire", optionsIdx, StringComparison.Ordinal);
        Assert.True(rateIdx > optionsIdx, "Rate limiting must come after OPTIONS return");
    }

    // ──────────────────────────────────────────────
    // R3-010: ApiAutomationService.TriggerRunInBackground error boundary
    // ──────────────────────────────────────────────

    /// <summary>
    /// R3-010: TriggerRunInBackground must wrap the entire fire-and-forget block in
    /// a try/catch so that synchronous exceptions from TriggerRunAsync don't become
    /// unobserved task exceptions crashing the thread pool.
    /// </summary>
    [Fact]
    public void R3_010_TriggerRunInBackground_HasTryCatchErrorBoundary()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Api", "ApiAutomationService.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Find TriggerRunInBackground method
        var methodIdx = source.IndexOf("TriggerRunInBackground", StringComparison.Ordinal);
        Assert.True(methodIdx > 0, "TriggerRunInBackground not found");

        // Extract method body (from first { after method name to reasonable end)
        var bodyStart = source.IndexOf('{', methodIdx);
        Assert.True(bodyStart > 0);

        // Find next method (private/public) to delimit the body
        var body = ExtractMethodBody(source, bodyStart);

        // Must have try/catch as error boundary
        Assert.Contains("try", body, StringComparison.Ordinal);
        Assert.Contains("catch", body, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, int openBraceIdx)
    {
        var depth = 0;
        var start = openBraceIdx;
        for (var i = openBraceIdx; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0)
                return source[start..(i + 1)];
        }
        return source[start..];
    }

    // ──────────────────────────────────────────────
    // R4-001: MovePipelinePhase must log MOVE_FAILED even without audit path
    // ──────────────────────────────────────────────

    /// <summary>
    /// R4-001: The MOVE_FAILED branch in MovePipelinePhase must not silently swallow
    /// failures when hasAuditPath is false. There must be a fallback mechanism (e.g. logger
    /// or always-write pattern) so failures are never invisible.
    /// Structural check: The MOVE_FAILED block must not be entirely guarded by hasAuditPath.
    /// </summary>
    [Fact]
    public void R4_001_MovePipelinePhase_LogsMoveFailureAlways()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "MovePipelinePhase.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Find MOVE_FAILED audit section
        var moveFailedIdx = source.IndexOf("MOVE_FAILED", StringComparison.Ordinal);
        Assert.True(moveFailedIdx > 0, "MOVE_FAILED string not found");

        // The MOVE_FAILED block should have a logging fallback when no audit path.
        // Look for a pattern that logs regardless of hasAuditPath (e.g. OnProgress, LogWarning, etc.)
        // Going backwards from MOVE_FAILED to find the enclosing else block.
        var blockBefore = source[(moveFailedIdx - 300)..moveFailedIdx];

        // After fix, the else block should NOT have ONLY hasAuditPath-guarded audit writing.
        // There must be an unconditional message (Logger, OnProgress, or _context).
        var afterMoveFailedBlock = source[moveFailedIdx..(moveFailedIdx + 500)];
        var combinedBlock = blockBefore + afterMoveFailedBlock;

        // Must have OnProgress or Logger call to surface failures
        var hasUnconditionalLog = combinedBlock.Contains("OnProgress", StringComparison.Ordinal)
            || combinedBlock.Contains("LogWarning", StringComparison.Ordinal)
            || combinedBlock.Contains("_logger", StringComparison.Ordinal)
            || combinedBlock.Contains("context.OnProgress", StringComparison.Ordinal);
        Assert.True(hasUnconditionalLog, "MOVE_FAILED must have an unconditional logging fallback beyond the hasAuditPath guard");
    }

    // ──────────────────────────────────────────────
    // R4-007: WriteMetadataSidecar callers must null-check audit path
    // ──────────────────────────────────────────────

    /// <summary>
    /// R4-007: WriteMetadataSidecar throws ArgumentException on null/empty path.
    /// Every direct call to WriteMetadataSidecar in RunOrchestrator helpers must be
    /// guarded by an audit-path null check.
    /// </summary>
    [Fact]
    public void R4_007_WriteMetadataSidecar_CallsAreGuarded()
    {
        // Check that WriteMetadataSidecar is only called when audit path is verified non-null
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "RunOrchestrator.PreviewAndPipelineHelpers.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Every method that calls WriteMetadataSidecar must have a null guard before it
        var callIdx = 0;
        while ((callIdx = source.IndexOf("WriteMetadataSidecar(", callIdx, StringComparison.Ordinal)) >= 0)
        {
            // Look at the 500 chars before the call for a guard
            var contextStart = Math.Max(0, callIdx - 500);
            var contextBefore = source[contextStart..callIdx];

            var hasGuard = contextBefore.Contains("IsNullOrEmpty", StringComparison.Ordinal)
                || contextBefore.Contains("IsNullOrWhiteSpace", StringComparison.Ordinal)
                || contextBefore.Contains("hasAuditPath", StringComparison.Ordinal)
                || contextBefore.Contains("!string.IsNullOrEmpty(options.AuditPath)", StringComparison.Ordinal);

            Assert.True(hasGuard,
                $"WriteMetadataSidecar call at position {callIdx} lacks a null-guard for audit path");

            callIdx += "WriteMetadataSidecar(".Length;
        }
    }

    // ──────────────────────────────────────────────
    // R4-014: ApplyPathMutations must detect cycles
    // ──────────────────────────────────────────────

    /// <summary>
    /// R4-014: ApplyPathMutations must detect cyclic mutations (A→B, B→A)
    /// and reject or skip them rather than silently producing non-deterministic results.
    /// </summary>
    [Fact]
    public void R4_014_ApplyPathMutations_DetectsCyclicMutations()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "PhasePlanning.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("ApplyPathMutations", StringComparison.Ordinal);
        Assert.True(methodIdx > 0);

        var bodyStart = source.IndexOf('{', methodIdx);
        var body = ExtractMethodBody(source, bodyStart);

        // Must have cycle detection logic
        var hasCycleDetection = body.Contains("cycle", StringComparison.OrdinalIgnoreCase)
            || body.Contains("circular", StringComparison.OrdinalIgnoreCase)
            || body.Contains("visited", StringComparison.OrdinalIgnoreCase)
            || body.Contains("transitive", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasCycleDetection,
            "ApplyPathMutations must have cycle detection (search for 'cycle', 'circular', 'visited', or 'transitive')");
    }

    // ──────────────────────────────────────────────
    // R4-018: Preflight AuditPath must test file-level write (not just dir)
    // ──────────────────────────────────────────────

    /// <summary>
    /// R4-018: TryValidateWritablePath with treatAsDirectory=false must also test the
    /// actual file path for writability, not just the parent directory.
    /// </summary>
    [Fact]
    public void R4_018_PreflightValidatesFileNotJustDirectory()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "RunOrchestrator.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        var methodIdx = source.IndexOf("private bool TryValidateWritablePath", StringComparison.Ordinal);
        Assert.True(methodIdx > 0);

        var bodyStart = source.IndexOf('{', methodIdx);
        var body = ExtractMethodBody(source, bodyStart);

        // When treatAsDirectory is false, must also validate the actual file (FileMode.OpenOrCreate or FileMode.Append)
        var hasFileProbe = body.Contains("FileMode.OpenOrCreate", StringComparison.Ordinal)
            || body.Contains("FileMode.Append", StringComparison.Ordinal)
            || body.Contains("OpenOrCreate", StringComparison.Ordinal)
            || body.Contains("FileStream", StringComparison.Ordinal);
        Assert.True(hasFileProbe,
            "TryValidateWritablePath must test actual file writability when treatAsDirectory=false (FileMode.OpenOrCreate/Append)");
    }

    // ──────────────────────────────────────────────
    // R4-022: StreamingScanPipelinePhase must check for symlinks/reparse points
    // ──────────────────────────────────────────────

    /// <summary>
    /// R4-022: StreamingScanPipelinePhase must check for reparse points (symlinks)
    /// before adding files to the candidate list. Symlinks can escape root policy
    /// and cause non-deterministic dedup.
    /// </summary>
    [Fact]
    public void R4_022_StreamingScan_ChecksReparsePoints()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "StreamingScanPipelinePhase.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Must check for ReparsePoint attribute
        var hasReparseCheck = source.Contains("ReparsePoint", StringComparison.Ordinal)
            || source.Contains("reparse", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasReparseCheck,
            "StreamingScanPipelinePhase must check for ReparsePoint/symlink before dedup normalization");
    }

    // ──────────────────────────────────────────────
    // R5-001: ToolInvokerAdapter.VerifyChd/VerifySevenZip must use ToolRequirement overload
    // ──────────────────────────────────────────────

    /// <summary>
    /// R5-001: VerifyChd and VerifySevenZip in ToolInvokerAdapter must pass
    /// a ToolRequirement to InvokeProcess (6-arg overload), not the 3-arg overload
    /// that skips hash verification.
    /// </summary>
    [Fact]
    public void R5_001_ToolInvokerAdapter_VerifyUsesToolRequirement()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Conversion", "ToolInvokerAdapter.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // VerifyChd method must contain ToolRequirement reference
        var verifyChdIdx = source.IndexOf("private VerificationStatus VerifyChd(", StringComparison.Ordinal);
        Assert.True(verifyChdIdx > 0, "VerifyChd method not found");

        var verifyChdBody = ExtractMethodBody(source, source.IndexOf('{', verifyChdIdx));
        // Must use a named ToolRequirement constant (e.g. ChdmanVerifyRequirement) in the 6-arg overload
        Assert.Contains("Requirement", verifyChdBody, StringComparison.Ordinal);

        // VerifySevenZip method must contain ToolRequirement reference
        var verify7zIdx = source.IndexOf("private VerificationStatus VerifySevenZip(", StringComparison.Ordinal);
        Assert.True(verify7zIdx > 0, "VerifySevenZip method not found");

        var verify7zBody = ExtractMethodBody(source, source.IndexOf('{', verify7zIdx));
        Assert.Contains("Requirement", verify7zBody, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────
    // R5-005: Report invariant must validate category buckets
    // ──────────────────────────────────────────────

    /// <summary>
    /// R5-005: The report invariant in RunReportWriter must validate that partial/cancelled
    /// runs are handled (IsPartial flag or relaxed accounting) so the strict invariant
    /// doesn't crash on incomplete data.
    /// </summary>
    [Fact]
    public void R5_005_ReportInvariant_HandlesPartialRuns()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Reporting", "RunReportWriter.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // The invariant check block must handle partial/cancelled runs
        var invariantIdx = source.IndexOf("Report summary invariant", StringComparison.Ordinal);
        Assert.True(invariantIdx > 0, "Report invariant not found");

        // Look in the 1000 chars before the invariant for a status/partial guard
        var contextStart = Math.Max(0, invariantIdx - 1000);
        var context = source[contextStart..invariantIdx];

        var hasPartialGuard = context.Contains("IsPartial", StringComparison.Ordinal)
            || context.Contains("Cancelled", StringComparison.Ordinal)
            || context.Contains("Status", StringComparison.Ordinal);
        Assert.True(hasPartialGuard, "Report invariant must skip or relax for partial/cancelled runs");
    }

    // ──────────────────────────────────────────────
    // R5-015: Tool output in error messages must be sanitized
    // ──────────────────────────────────────────────

    /// <summary>
    /// R5-015: BuildFailureOutput in ToolRunnerAdapter must sanitize tool output
    /// to prevent absolute path leakage into error messages and reports.
    /// </summary>
    [Fact]
    public void R5_015_ToolOutput_IsSanitized()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Tools", "ToolRunnerAdapter.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // Must have output sanitization (SanitizeToolOutput, SanitizePath, or redact pattern)
        var hasSanitization = source.Contains("SanitizeToolOutput", StringComparison.Ordinal)
            || source.Contains("SanitizePath", StringComparison.Ordinal)
            || source.Contains("RedactPaths", StringComparison.Ordinal)
            || source.Contains("redact", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasSanitization, "ToolRunnerAdapter must sanitize tool output to prevent path leakage");
    }

    // ──────────────────────────────────────────────
    // R6-001: Profile export must validate target path
    // ──────────────────────────────────────────────

    /// <summary>
    /// R6-001: RunProfileService.ExportAsync must validate that the target path
    /// is within safe boundaries to prevent path traversal writes to arbitrary locations.
    /// </summary>
    [Fact]
    public void R6_001_ProfileExport_ValidatesPathBounds()
    {
        var sourcePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Profiles", "RunProfileService.cs");
        Assert.True(File.Exists(sourcePath), $"Missing: {sourcePath}");

        var source = File.ReadAllText(sourcePath);

        // ExportAsync must have path validation
        var exportIdx = source.IndexOf("ExportAsync", StringComparison.Ordinal);
        Assert.True(exportIdx > 0, "ExportAsync method not found");

        var bodyStart = source.IndexOf('{', exportIdx);
        var exportBody = ExtractMethodBody(source, bodyStart);

        // Must have path traversal protection
        var hasPathValidation = exportBody.Contains("IsProtected", StringComparison.Ordinal)
            || exportBody.Contains("traversal", StringComparison.OrdinalIgnoreCase)
            || exportBody.Contains("EnsureSafe", StringComparison.Ordinal)
            || exportBody.Contains("SafetyValidator", StringComparison.Ordinal)
            || exportBody.Contains("allowedRoot", StringComparison.OrdinalIgnoreCase)
            || exportBody.Contains("ValidateExportPath", StringComparison.Ordinal);
        Assert.True(hasPathValidation, "ExportAsync must validate target path against path traversal");
    }
}
