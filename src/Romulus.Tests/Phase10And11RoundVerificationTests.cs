using System.Text.RegularExpressions;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Verification and regression tests for Round 10 + Round 11 audit findings.
/// Covers RunLifecycleManager atomicity (R10-014), Docker security (R10-033),
/// fixdat name validation (R10-041), and structural checks for R10/R11 findings.
/// </summary>
public sealed class Phase10And11RoundVerificationTests
{
    // =========================================================================
    // R10-014: RunLifecycleManager _activeRunId/_activeTask under _activeLock
    // =========================================================================

    [Fact]
    public void R10_014_ActiveRunId_And_ActiveTask_SetUnderLock()
    {
        // Structural test: verify that _activeRunId and _activeTask
        // are only assigned inside lock(_activeLock) blocks
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Api", "RunLifecycleManager.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var lockDepth = 0; // tracks nesting depth inside lock(_activeLock)
        var braceCounterActive = false;
        var braceDepthAtLockStart = 0;
        var totalBraceDepth = 0;
        var unsafeAssignments = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // Track entering lock(_activeLock) — next '{' enters the locked block
            if (trimmed.Contains("lock (_activeLock)"))
            {
                braceCounterActive = true;
                braceDepthAtLockStart = totalBraceDepth;
            }

            // Count braces on this line
            foreach (var c in line)
            {
                if (c == '{')
                {
                    totalBraceDepth++;
                    if (braceCounterActive && totalBraceDepth == braceDepthAtLockStart + 1)
                    {
                        lockDepth++;
                        braceCounterActive = false;
                    }
                }
                else if (c == '}')
                {
                    if (lockDepth > 0 && totalBraceDepth == braceDepthAtLockStart + 1)
                        lockDepth--;
                    totalBraceDepth--;
                }
            }

            // Check for _activeRunId or _activeTask WRITE assignments
            if (Regex.IsMatch(trimmed, @"_activeRunId\s*=") || Regex.IsMatch(trimmed, @"_activeTask\s*="))
            {
                // Skip read snapshots like "var id = _activeRunId;"
                if (Regex.IsMatch(trimmed, @"var\s+\w+\s*=\s*_active"))
                    continue;
                // Skip comparisons like "if (_activeRunId == run.RunId)"
                if (Regex.IsMatch(trimmed, @"_activeRunId\s*=="))
                    continue;

                if (lockDepth == 0)
                    unsafeAssignments.Add($"L{i + 1}: {trimmed}");
            }
        }

        Assert.True(unsafeAssignments.Count == 0,
            $"Found _activeRunId/_activeTask assignments outside lock(_activeLock):\n" +
            string.Join("\n", unsafeAssignments));
    }

    // =========================================================================
    // R10-033: Docker container runs as non-root user
    // =========================================================================

    [Fact]
    public void R10_033_Dockerfile_HasUserDirectiveBeforeEntrypoint()
    {
        var repoRoot = FindRepoRoot();
        var dockerfilePath = Path.Combine(repoRoot, "deploy", "docker", "api", "Dockerfile");
        Assert.True(File.Exists(dockerfilePath), $"Dockerfile not found: {dockerfilePath}");

        var lines = File.ReadAllLines(dockerfilePath);
        int userLine = -1;
        int entrypointLine = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("USER ", StringComparison.OrdinalIgnoreCase))
                userLine = i;
            if (lines[i].TrimStart().StartsWith("ENTRYPOINT", StringComparison.OrdinalIgnoreCase))
                entrypointLine = i;
        }

        Assert.True(userLine >= 0, "Dockerfile must contain a USER directive");
        Assert.True(entrypointLine >= 0, "Dockerfile must contain an ENTRYPOINT directive");
        Assert.True(userLine < entrypointLine,
            $"USER directive (line {userLine + 1}) must appear before ENTRYPOINT (line {entrypointLine + 1})");
    }

    [Fact]
    public void R10_033_Dockerfile_NonRootUser()
    {
        var repoRoot = FindRepoRoot();
        var dockerfilePath = Path.Combine(repoRoot, "deploy", "docker", "api", "Dockerfile");
        var content = File.ReadAllText(dockerfilePath);

        // Must not run as root — USER directive should specify non-root user
        var match = Regex.Match(content, @"^USER\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Dockerfile must have a USER directive");
        Assert.NotEqual("root", match.Groups[1].Value.ToLowerInvariant());
    }

    // =========================================================================
    // R10-041: /runs/{runId}/fixdat name parameter length capped
    // =========================================================================

    [Fact]
    public void R10_041_FixDat_NameLengthCap_InSource()
    {
        // Structural: ProgramHelpers.cs must cap name to 255 chars
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Api", "ProgramHelpers.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // Must contain a length check before using name for DAT generation
        Assert.Contains("255", source);
        Assert.True(
            source.Contains("trimmedName.Length > 255") || source.Contains(".Length > 255"),
            "ProgramHelpers must cap fixdat name parameter length");
    }

    // =========================================================================
    // R10-040: Profile ID already validated by regex
    // =========================================================================

    [Fact]
    public void R10_040_ProfileIdValidator_BlocksPathTraversal()
    {
        var srcRoot = FindSrcRoot();
        var validatorFiles = Directory.GetFiles(srcRoot, "RunProfileValidator.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(validatorFiles);

        var source = File.ReadAllText(validatorFiles[0]);
        // Must contain regex that blocks path traversal chars
        Assert.True(
            source.Contains("[A-Za-z0-9._-]") || source.Contains(@"[A-Za-z0-9._\-]"),
            "RunProfileValidator must have character-class regex blocking path traversal");
    }

    // =========================================================================
    // R10-011: CTS disposal pattern is safe (lock + try-catch)
    // =========================================================================

    [Fact]
    public void R10_011_CtsDisposal_HasLockAndTryCatch()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.UI.Wpf", "ViewModels", "MainViewModel.RunPipeline.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        Assert.Contains("lock (_ctsLock)", source);
        Assert.Contains("oldCts?.Dispose()", source);
        Assert.Contains("ObjectDisposedException", source);
    }

    // =========================================================================
    // R10-072: CsvSafe checks if already quoted before wrapping
    // =========================================================================

    [Fact]
    public void R10_072_CsvSafe_NoDoubleQuoting()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Infrastructure", "Reporting", "ReportGenerator.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // Must check if already quoted
        Assert.Contains("StartsWith('\"')", source);
    }

    // =========================================================================
    // R10-062: HMAC key write uses atomic temp+rename
    // =========================================================================

    [Fact]
    public void R10_062_HmacKey_AtomicWrite()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Infrastructure", "Audit", "AuditSigningService.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        Assert.Contains(".tmp", source);
        Assert.Contains("File.Move(", source);
    }

    // =========================================================================
    // R11-010: ConvertOnly reset in CompleteRun
    // =========================================================================

    [Fact]
    public void R11_010_ConvertOnly_ResetInCompleteRun()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.UI.Wpf", "ViewModels", "MainViewModel.RunPipeline.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // CompleteRun must reset ConvertOnly
        var completeRunIndex = source.IndexOf("void CompleteRun(", StringComparison.Ordinal);
        Assert.True(completeRunIndex >= 0, "CompleteRun method must exist");

        var afterMethod = source[completeRunIndex..];
        var methodEnd = afterMethod.IndexOf("\n    }", StringComparison.Ordinal);
        if (methodEnd < 0) methodEnd = afterMethod.Length;
        var methodBody = afterMethod[..methodEnd];

        Assert.Contains("ConvertOnly = false", methodBody);
    }

    // =========================================================================
    // R11-003: DeduplicationEngine unknown category returns safe fallback
    // =========================================================================

    [Fact]
    public void R11_003_CategoryRank_UnknownCategory_ReturnsFallback()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Core", "Deduplication", "DeduplicationEngine.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // GetCategoryRank should have TryGetValue with fallback
        Assert.Contains("TryGetValue(", source);
        // Must have a default return for unknown categories (: 0 or Int32.MaxValue)
        Assert.True(
            source.Contains("? rank : 0") || source.Contains("? rank : int.MaxValue"),
            "GetCategoryRank must have safe fallback for unknown categories");
    }

    // =========================================================================
    // R11-004: ApiAutomationService exception handling
    // =========================================================================

    [Fact]
    public void R11_004_ApiAutomation_TaskExceptionHandled()
    {
        var srcRoot = FindSrcRoot();
        var files = Directory.GetFiles(srcRoot, "ApiAutomationService.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        var source = File.ReadAllText(files[0]);

        // Must have ContinueWith for fault handling
        Assert.Contains("OnlyOnFaulted", source);
        Assert.Contains("_lastError", source);
    }

    // =========================================================================
    // R10-064: Rollback result has separate counters
    // =========================================================================

    [Fact]
    public void R10_064_RollbackResult_HasGranularCounters()
    {
        var srcRoot = FindSrcRoot();
        // AuditRollbackResult is defined in ServiceModels.cs (Contracts)
        var filePath = Path.Combine(srcRoot, "Romulus.Contracts", "Models", "ServiceModels.cs");
        Assert.True(File.Exists(filePath), $"ServiceModels.cs not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // Must have separate counters for different failure categories
        Assert.Contains("SkippedMissingDest", source);
        Assert.Contains("SkippedCollision", source);
        Assert.Contains("Failed", source);
        Assert.Contains("RolledBack", source);
    }

    // =========================================================================
    // R10-063: CSV parser processes all fields (no silent skip)
    // =========================================================================

    [Fact]
    public void R10_063_CsvParser_AddsAllFields()
    {
        var srcRoot = FindSrcRoot();
        var filePath = Path.Combine(srcRoot, "Romulus.Infrastructure", "Audit", "AuditCsvParser.cs");
        Assert.True(File.Exists(filePath), $"File not found: {filePath}");

        var source = File.ReadAllText(filePath);

        // Parser must add final field after loop
        Assert.Contains("fields.Add(current.ToString())", source);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string FindSrcRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "Romulus.Core")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not find src/ root from " + AppContext.BaseDirectory);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}
