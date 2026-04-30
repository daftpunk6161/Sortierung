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
    // R10-041 / R10-040 / R10-011 / R10-072 / R10-062 / R11-010 / R11-003 /
    // R11-004 / R10-064 / R10-063: source-grep tests removed per
    // testing.instructions.md - all of them only pinned literal identifier
    // strings (e.g. "255", "OnlyOnFaulted", "fields.Add") instead of asserting
    // behaviour. The actual safety properties are exercised by the matching
    // behavioural test suites (CsvParser, RollbackResult, AuditSigningService,
    // ApiAutomationService, DeduplicationEngine).
    // =========================================================================

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string FindSrcRoot()
        => Romulus.Tests.TestFixtures.RepoPaths.SrcRoot();

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
