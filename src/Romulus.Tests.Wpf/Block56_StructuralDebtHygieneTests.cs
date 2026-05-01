using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block 5+6 RED Phase: Failing tests for Phase 5 (Structural Debt) + Phase 6 (Test Quality + Hygiene).
/// TDD cycle: RED → GREEN → REFACTOR.
///
/// Phase 5 (Post-Release):
///   - TASK-022: MainViewModel analysis mapping (verification only)
///   - TASK-023/024: ConfigVM + DashboardVM extraction (verified: already done via SetupVM/RunVM)
///   - TASK-025: Orchestration sub-namespaces (deferred: high-risk pure structural change)
///   - TASK-026: FeatureCommandService handlers (verified: already done via partial classes)
///
/// Phase 6:
///   - TASK-028: Upgrade no-crash-only test assertions (see upgraded tests below)
///   - TASK-029b: WatchFolderService InternalBufferSize (RED)
///   - TASK-030a: API endpoint response code documentation (RED)
/// </summary>
public sealed class Block56_StructuralDebtHygieneTests
{
    // ═══ PHASE 5 REGRESSION GATES ═══════════════════════════════════════

    /// <summary>
    /// TASK-023 regression gate: SetupViewModel must handle Configuration domain properties.
    /// Already implemented — this test is a regression gate.
    /// </summary>
    [Fact]
    public void TASK023_SetupViewModel_HandlesConfigurationDomain()
    {
        var type = typeof(Romulus.UI.Wpf.ViewModels.SetupViewModel);

        // Configuration paths must be on SetupViewModel
        Assert.NotNull(type.GetProperty("TrashRoot"));
        Assert.NotNull(type.GetProperty("DatRoot"));
        Assert.NotNull(type.GetProperty("AuditRoot"));
        Assert.NotNull(type.GetProperty("Ps3DupesRoot"));

        // Tool paths
        Assert.NotNull(type.GetProperty("ToolChdman"));
        Assert.NotNull(type.GetProperty("ToolDolphin"));
        Assert.NotNull(type.GetProperty("Tool7z"));
        Assert.NotNull(type.GetProperty("ToolPsxtract"));
        Assert.NotNull(type.GetProperty("ToolCiso"));

        // Feature toggles
        Assert.NotNull(type.GetProperty("UseDat"));
        Assert.NotNull(type.GetProperty("ConvertEnabled"));
    }

    /// <summary>
    /// TASK-024 regression gate: RunViewModel must handle Dashboard/KPI domain.
    /// Already implemented — this test is a regression gate.
    /// </summary>
    [Fact]
    public void TASK024_RunViewModel_HandlesDashboardDomain()
    {
        var type = typeof(Romulus.UI.Wpf.ViewModels.RunViewModel);

        // Dashboard counters must be on RunViewModel
        Assert.NotNull(type.GetProperty("DashMode"));
        Assert.NotNull(type.GetProperty("DashWinners"));
        Assert.NotNull(type.GetProperty("DashDupes"));
        Assert.NotNull(type.GetProperty("DashJunk"));

        // Run state machine
        Assert.NotNull(type.GetProperty("CurrentRunState"));
        Assert.NotNull(type.GetProperty("HasRunData"));
    }

    /// <summary>
    /// TASK-026 regression gate: FeatureCommandService must cover all domains via partials.
    /// Already implemented — this test is a regression gate.
    /// </summary>
    [Fact]
    public void TASK026_FeatureCommandService_PartialsCoverAllDomains()
    {
        var type = typeof(Romulus.UI.Wpf.Services.FeatureCommandService);
        var methods = type.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
        var methodNames = methods.Select(m => m.Name).ToHashSet();

        // Verify key domain methods exist across partials:
        // .Analysis
        Assert.Contains(methodNames, n => n.Contains("Search") || n.Contains("Heatmap") || n.Contains("Clone"));
        // .Conversion
        Assert.Contains(methodNames, n => n.Contains("Conversion") || n.Contains("Convert"));
        // .Dat
        Assert.Contains(methodNames, n => n.Contains("Dat"));
        // .Export
        Assert.Contains(methodNames, n => n.Contains("Export") || n.Contains("Csv"));
    }

    // ═══ PHASE 6: TASK-029b — WatchFolderService InternalBufferSize ════
    // TASK029b_WatchFolderService_MustSetInternalBufferSize: removed per
    // testing.instructions.md - was a pure source-string-grep that pinned the
    // identifier 'InternalBufferSize' instead of asserting buffer behaviour.

    // ═══ PHASE 6: TASK-030a — API endpoints must document response codes ═

    /// <summary>
    /// TASK-030a RED: API endpoints must have .Produces() annotations for error responses.
    /// At minimum, POST /runs should document 400 and 409 responses.
    /// </summary>
    [Fact]
    public void TASK030a_ApiPostRuns_MustDocumentErrorResponseCodes()
    {
        var sourceDir = FindSrcRoot();
        var programFile = Path.Combine(sourceDir, "Romulus.Api", "Program.cs");

        Assert.True(File.Exists(programFile), $"Program.cs not found at {programFile}");

        var source = File.ReadAllText(programFile);

        // POST /runs should document at least 400 (Bad Request) and 409 (Conflict)
        // We check for Produces<ProblemDetails> or Produces(StatusCodes.Status400BadRequest) patterns
        Assert.True(
            source.Contains("Status400BadRequest", StringComparison.Ordinal) ||
            source.Contains("ProducesValidationProblem", StringComparison.Ordinal),
            "POST /runs must document 400 Bad Request response");

        Assert.True(
            source.Contains("Status409Conflict", StringComparison.Ordinal),
            "POST /runs must document 409 Conflict response");
    }

    /// <summary>
    /// TASK-030a RED: API GET endpoints returning single resources must document 404.
    /// </summary>
    [Fact]
    public void TASK030a_ApiGetRunById_MustDocument404Response()
    {
        var sourceDir = FindSrcRoot();
        var programFile = Path.Combine(sourceDir, "Romulus.Api", "Program.cs");

        Assert.True(File.Exists(programFile));

        var source = File.ReadAllText(programFile);

        Assert.True(
            source.Contains("Status404NotFound", StringComparison.Ordinal),
            "GET /runs/{runId} must document 404 Not Found response");
    }

    // ═══ PHASE 6: TASK-028 — Meta-guard for upgraded test assertions ════

    /// <summary>
    /// TASK-028 guard: WpfCoverageBoostTests target tests must not use generic Count assertions.
    /// After upgrading, these specific tests should have fachliche assertions.
    /// </summary>
    [Fact]
    public void TASK028_WpfCoverageBoostTests_TargetTests_NoGenericCountGuards()
    {
        var sourceDir = FindSrcRoot();
        var testFile = Path.Combine(sourceDir, "Romulus.Tests.Wpf", "WpfCoverageBoostTests.cs");

        Assert.True(File.Exists(testFile));

        var lines = File.ReadAllLines(testFile);

        // Find test methods that the plan targets for upgrade:
        // GetDuplicateHeatmap, GetConfigDiff, BuildGameElementMap, GetSortTemplates,
        // CompareDatFiles, CalculateHealthScore
        var targetTestMethods = new[]
        {
            "GetDuplicateHeatmap_WithMultipleGroups_ReturnsHeatmapEntries",
            "GetConfigDiff_DetectsChanges",
            "BuildGameElementMap_ReturnsGameRomMapping",
            "GetSortTemplates_ReturnsNonEmptyDictionary",
            "CompareDatFiles_DifferentFiles_DetectsDiff",
            "DetectConsoleFromPath_VariousPaths_CorrectConsole",
        };

        var violations = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            foreach (var method in targetTestMethods)
            {
                if (line.Contains(method))
                {
                    // Scan next 30 lines for generic Assert.True(*.Count > 0)
                    for (int j = i + 1; j < Math.Min(i + 30, lines.Length); j++)
                    {
                        var assertLine = lines[j].Trim();
                        if (assertLine.StartsWith("public ") || assertLine.StartsWith("[Fact]") || assertLine.StartsWith("[Theory]"))
                            break; // next test method

                        if (assertLine.Contains("Assert.True") &&
                            (assertLine.Contains(".Count > 0") || assertLine.Contains(".Count >= 1") ||
                             assertLine.Contains(".Length > 0") || assertLine.Contains(".Length >= 0")))
                        {
                            violations.Add($"Line {j + 1}: {method} has generic count assertion: {assertLine}");
                        }
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"TASK-028: {violations.Count} generic count assertions remain in target tests:\n" +
            string.Join("\n", violations));
    }

    // ═══ HELPERS ════════════════════════════════════════════════════════

    private static string FindSrcRoot()
        => Romulus.Tests.TestFixtures.RepoPaths.SrcRoot();
}
