using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

/// <summary>
/// Targeted coverage tests for uncovered code paths in the Core Conversion layer.
///
/// Gaps addressed:
///   G-01  SourceIntegrityClassifier — missing extensions (.wux, .tgc, .dax, .zso, .jso)
///           and the IsArchiveExtension helper
///   G-02  ConversionConditionEvaluator — None / IsNKitSource / IsWadFile / IsCdiSource /
///           IsEncryptedPbp conditions; PS2 CD detector returning null (falls back to size);
///           NotSupportedException for unknown condition
///   G-03  ConversionGraph — wildcard source ('*'), archive extension with wildcard blocked,
///           expand command exception for lossy→lossy path
///   G-04  ConversionPlanner — no-target-defined, no-conversion-path, PlanBatch,
///           RiskReason variants (manual-only, unknown-source, manual-only:unknown-source),
///           PS2 CD detector returning null falls back to file size
///   G-05  ConversionPolicyEvaluator — Unknown integrity + all-lossless path → Safe
///   G-06  PolicyEngine — empty snapshot, required-extension-by-console (valid match),
///           compliant report (IsCompliant = true)
/// </summary>
public sealed class ConversionCoverageGapTests
{
    // =====================================================================
    // G-01  SourceIntegrityClassifier
    // =====================================================================

    [Theory]
    [InlineData(".wux", SourceIntegrity.Lossless)]
    [InlineData(".tgc", SourceIntegrity.Lossless)]
    public void SourceIntegrityClassifier_LosslessExtensions_WuxAndTgc(string ext, SourceIntegrity expected)
    {
        Assert.Equal(expected, SourceIntegrityClassifier.Classify(ext));
    }

    [Theory]
    [InlineData(".dax", SourceIntegrity.Lossy)]
    [InlineData(".zso", SourceIntegrity.Lossy)]
    [InlineData(".jso", SourceIntegrity.Lossy)]
    public void SourceIntegrityClassifier_LossyExtensions_DaxZsoJso(string ext, SourceIntegrity expected)
    {
        Assert.Equal(expected, SourceIntegrityClassifier.Classify(ext));
    }

    [Theory]
    [InlineData(".zip", true)]
    [InlineData(".7z", true)]
    [InlineData(".rar", true)]
    [InlineData(".tar", true)]
    [InlineData(".gz", true)]
    [InlineData(".tgz", true)]
    [InlineData(".bz2", true)]
    [InlineData(".xz", true)]
    [InlineData(".iso", false)]
    [InlineData(".chd", false)]
    [InlineData(".cso", false)]
    [InlineData("", false)]
    public void SourceIntegrityClassifier_IsArchiveExtension(string ext, bool expected)
    {
        Assert.Equal(expected, SourceIntegrityClassifier.IsArchiveExtension(ext));
    }

    [Fact]
    public void SourceIntegrityClassifier_IsArchiveExtension_CaseInsensitive()
    {
        Assert.True(SourceIntegrityClassifier.IsArchiveExtension(".ZIP"));
        Assert.True(SourceIntegrityClassifier.IsArchiveExtension(".7Z"));
        Assert.True(SourceIntegrityClassifier.IsArchiveExtension(".Zip"));
    }

    [Fact]
    public void SourceIntegrityClassifier_IsArchiveExtension_Null_ReturnsFalse()
    {
        Assert.False(SourceIntegrityClassifier.IsArchiveExtension(null!));
    }

    [Fact]
    public void SourceIntegrityClassifier_IsArchiveExtension_WhitespaceOnly_ReturnsFalse()
    {
        Assert.False(SourceIntegrityClassifier.IsArchiveExtension("   "));
    }

    // =====================================================================
    // G-02  ConversionConditionEvaluator
    // =====================================================================

    [Fact]
    public void ConversionConditionEvaluator_NoneCondition_AlwaysTrue()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L);
        Assert.True(ev.Evaluate(ConversionCondition.None, "game.iso"));
    }

    [Theory]
    [InlineData("game.nkit.iso", true)]
    [InlineData("game.NKIT.ISO", true)]
    [InlineData("game.iso", false)]
    [InlineData("game.chd", false)]
    public void ConversionConditionEvaluator_IsNKitSource(string fileName, bool expected)
    {
        var ev = new ConversionConditionEvaluator(_ => 0L);
        var result = ev.Evaluate(ConversionCondition.IsNKitSource, $@"C:\roms\{fileName}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("game.wad", true)]
    [InlineData("game.WAD", true)]
    [InlineData("game.iso", false)]
    [InlineData("game.wad.bak", false)]
    public void ConversionConditionEvaluator_IsWadFile(string fileName, bool expected)
    {
        var ev = new ConversionConditionEvaluator(_ => 0L);
        var result = ev.Evaluate(ConversionCondition.IsWadFile, $@"C:\roms\{fileName}");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("game.cdi", true)]
    [InlineData("game.CDI", true)]
    [InlineData("game.iso", false)]
    public void ConversionConditionEvaluator_IsCdiSource(string fileName, bool expected)
    {
        var ev = new ConversionConditionEvaluator(_ => 0L);
        var result = ev.Evaluate(ConversionCondition.IsCdiSource, $@"C:\roms\{fileName}");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConversionConditionEvaluator_IsEncryptedPbp_WithoutDetector_ReturnsFalse()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L, encryptedPbpDetector: null);
        Assert.False(ev.Evaluate(ConversionCondition.IsEncryptedPbp, @"C:\roms\game.pbp"));
    }

    [Fact]
    public void ConversionConditionEvaluator_IsEncryptedPbp_NonPbpExtension_ReturnsFalse()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L, encryptedPbpDetector: _ => true);
        Assert.False(ev.Evaluate(ConversionCondition.IsEncryptedPbp, @"C:\roms\game.iso"));
    }

    [Fact]
    public void ConversionConditionEvaluator_IsEncryptedPbp_DetectorReturnsTrue()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L, encryptedPbpDetector: _ => true);
        Assert.True(ev.Evaluate(ConversionCondition.IsEncryptedPbp, @"C:\roms\game.pbp"));
    }

    [Fact]
    public void ConversionConditionEvaluator_IsEncryptedPbp_DetectorReturnsFalse()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L, encryptedPbpDetector: _ => false);
        Assert.False(ev.Evaluate(ConversionCondition.IsEncryptedPbp, @"C:\roms\game.pbp"));
    }

    [Fact]
    public void ConversionConditionEvaluator_UnsupportedCondition_ThrowsNotSupportedException()
    {
        var ev = new ConversionConditionEvaluator(_ => 0L);
        Assert.Throws<NotSupportedException>(() =>
            ev.Evaluate((ConversionCondition)999, @"C:\roms\game.iso"));
    }

    [Fact]
    public void ConversionConditionEvaluator_Ps2CdDetector_ReturnsNull_FallsBackToFileSize_SmallFile_IsLessThan()
    {
        // PS2 CD detector returns null → undetectable → fall back to file size
        var smallSize = ConversionThresholds.CdImageThresholdBytes - 1;
        var ev = new ConversionConditionEvaluator(
            _ => smallSize,
            ps2CdDetector: _ => null);

        var result = ev.Evaluate(ConversionCondition.FileSizeLessThan700MB, @"C:\roms\game.iso", "PS2");

        Assert.True(result);
    }

    [Fact]
    public void ConversionConditionEvaluator_Ps2CdDetector_ReturnsNull_FallsBackToFileSize_LargeFile_IsGreaterEqual()
    {
        var largeSize = ConversionThresholds.CdImageThresholdBytes + 1;
        var ev = new ConversionConditionEvaluator(
            _ => largeSize,
            ps2CdDetector: _ => null);

        var result = ev.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, @"C:\roms\game.iso", "PS2");

        Assert.True(result);
    }

    [Fact]
    public void ConversionConditionEvaluator_NonPs2Console_IgnoresPs2CdDetector_UsesFileSize()
    {
        // For non-PS2 consoles the CD detector must not be consulted; file size governs.
        var largeSize = ConversionThresholds.CdImageThresholdBytes + 1;
        var detectorCalled = false;
        var ev = new ConversionConditionEvaluator(
            _ => largeSize,
            ps2CdDetector: _ => { detectorCalled = true; return true; });

        var result = ev.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, @"C:\roms\game.iso", "PS1");

        Assert.True(result);
        Assert.False(detectorCalled, "PS2 CD detector must not be consulted for non-PS2 consoles.");
    }

    // =====================================================================
    // G-03  ConversionGraph — wildcard, archive, expand
    // =====================================================================

    [Fact]
    public void ConversionGraph_WildcardSource_MatchesNonArchiveExtension()
    {
        var graph = new ConversionGraph([
            WildcardEdge("*", ".chd", "chdman", 1)
        ]);

        // Non-archive extension '.sfc' should match the wildcard edge
        var path = graph.FindPath(".sfc", ".chd", "SNES", _ => true);

        Assert.NotNull(path);
        Assert.Single(path!);
        Assert.Equal(".chd", path[0].TargetExtension);
    }

    [Fact]
    public void ConversionGraph_WildcardSource_BlocksArchiveExtension()
    {
        var graph = new ConversionGraph([
            WildcardEdge("*", ".chd", "chdman", 1)
        ]);

        // Archive extensions (.zip, .7z) must NOT match the wildcard edge
        Assert.Null(graph.FindPath(".zip", ".chd", "SNES", _ => true));
        Assert.Null(graph.FindPath(".7z", ".chd", "SNES", _ => true));
    }

    [Fact]
    public void ConversionGraph_LossySource_ExpandCommand_IsNotBlocked()
    {
        // 'expand' command must be allowed even for lossy sources (it recovers data)
        var graph = new ConversionGraph([
            Edge(".cso", ".iso", "ciso", 1, command: "expand", lossless: false)
        ]);

        var path = graph.FindPath(".cso", ".iso", "PSP", _ => true, SourceIntegrity.Lossy);

        Assert.NotNull(path);
        Assert.Single(path!);
    }

    [Fact]
    public void ConversionGraph_LossySource_NonExpandLossyStep_IsBlocked()
    {
        // A non-expand lossy step on a lossy source must be blocked (Lossy→Lossy)
        var graph = new ConversionGraph([
            Edge(".cso", ".zso", "ciso", 1, command: "recompress", lossless: false)
        ]);

        var path = graph.FindPath(".cso", ".zso", "PSP", _ => true, SourceIntegrity.Lossy);

        Assert.Null(path);
    }

    [Fact]
    public void ConversionGraph_NullOrWhitespaceExtensions_ReturnNull()
    {
        var graph = new ConversionGraph([Edge(".iso", ".chd", "chdman", 1)]);

        Assert.Null(graph.FindPath(null!, ".chd", "PS1", _ => true));
        Assert.Null(graph.FindPath("", ".chd", "PS1", _ => true));
        Assert.Null(graph.FindPath(".iso", null!, "PS1", _ => true));
        Assert.Null(graph.FindPath(".iso", "", "PS1", _ => true));
    }

    // =====================================================================
    // G-04  ConversionPlanner — missing paths, PlanBatch, RiskReason, PS2 null
    // =====================================================================

    [Fact]
    public void ConversionPlanner_NoTargetDefined_IsBlocked()
    {
        var registry = FakeRegistryWithNoTarget([], ConversionPolicy.Auto);
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Contains("no-target-defined", plan.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConversionPlanner_NoConversionPath_IsBlocked()
    {
        // Registry has a target but no capability that connects the source extension
        var registry = CreateRegistry([], ConversionPolicy.Auto, ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Equal("no-conversion-path", plan.SkipReason);
    }

    [Fact]
    public void ConversionPlanner_PlanBatch_Empty_ReturnsEmpty()
    {
        var registry = CreateRegistry([], ConversionPolicy.Auto, ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plans = planner.PlanBatch([]);

        Assert.Empty(plans);
    }

    [Fact]
    public void ConversionPlanner_PlanBatch_Null_ReturnsEmpty()
    {
        var registry = CreateRegistry([], ConversionPolicy.Auto, ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plans = planner.PlanBatch(null!);

        Assert.Empty(plans);
    }

    [Fact]
    public void ConversionPlanner_PlanBatch_MultipleFiles_ReturnsOnePlanEach()
    {
        var registry = CreateRegistry(
            [Cap(".iso", ".chd", "chdman", 1)],
            ConversionPolicy.Auto,
            ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plans = planner.PlanBatch([
            ("C:\\roms\\a.iso", "PS1", ".iso"),
            ("C:\\roms\\b.iso", "PS1", ".iso"),
            ("C:\\roms\\c.chd", "PS1", ".chd")  // already target
        ]);

        Assert.Equal(3, plans.Count);
        Assert.Single(plans[0].Steps);
        Assert.Single(plans[1].Steps);
        Assert.Equal("already-target-format", plans[2].SkipReason);
    }

    [Fact]
    public void ConversionPlanner_RiskReason_ManualOnly_LosslessSource()
    {
        var registry = CreateRegistry(
            [Cap(".iso", ".chd", "chdman", 1)],
            ConversionPolicy.ManualOnly,
            ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Equal(ConversionSafety.Risky, plan.Safety);
        Assert.Equal("manual-only", plan.RiskReason);
    }

    [Fact]
    public void ConversionPlanner_RiskReason_ManualOnly_UnknownSource()
    {
        var registry = CreateRegistry(
            [Cap(".unknown", ".chd", "chdman", 1)],
            ConversionPolicy.ManualOnly,
            ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        // ManualOnly policy → Risky; RiskReason reflects manual-only:unknown-source
        var plan = planner.Plan("C:\\roms\\game.unknown", "PS1", ".unknown");

        Assert.Equal(ConversionSafety.Risky, plan.Safety);
        Assert.Equal("manual-only:unknown-source", plan.RiskReason);
    }

    [Fact]
    public void ConversionPlanner_RiskReason_UnknownSource_AutoPolicy_LossyPath()
    {
        // Auto policy + Unknown source integrity + lossy conversion path → Blocked
        // (ConversionPolicyEvaluator blocks unknown+lossy)
        var registry = CreateRegistry(
            [Cap(".unknown", ".chd", "chdman", 1, lossless: false)],
            ConversionPolicy.Auto,
            ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plan = planner.Plan("C:\\roms\\game.unknown", "PS1", ".unknown");

        // Unknown integrity + lossy path → Blocked
        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Null(plan.RiskReason);
    }

    [Fact]
    public void ConversionPlanner_RiskReason_Null_WhenSafe()
    {
        var registry = CreateRegistry(
            [Cap(".iso", ".chd", "chdman", 1)],
            ConversionPolicy.Auto,
            ".chd");
        var planner = new ConversionPlanner(registry, _ => "C:\\tools\\ok.exe", _ => 1024);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Equal(ConversionSafety.Safe, plan.Safety);
        Assert.Null(plan.RiskReason);
    }

    [Fact]
    public void ConversionPlanner_Ps2CdDetector_Null_FallsBackToFileSize_LargeFile_SelectsCreatedvd()
    {
        var registry = CreateRegistry(
        [
            Cap(".iso", ".chd", "chdman", 0, command: "createcd", consoleKey: "PS2",
                condition: ConversionCondition.FileSizeLessThan700MB),
            Cap(".iso", ".chd", "chdman", 0, command: "createdvd", consoleKey: "PS2",
                condition: ConversionCondition.FileSizeGreaterEqual700MB)
        ],
        ConversionPolicy.Auto,
        ".chd");

        var largeSize = ConversionThresholds.CdImageThresholdBytes + 1;
        var planner = new ConversionPlanner(
            registry,
            _ => "C:\\tools\\ok.exe",
            _ => largeSize,
            ps2CdDetector: _ => null);   // null = undetectable, fall back to file size

        var plan = planner.Plan("C:\\roms\\game.iso", "PS2", ".iso");

        Assert.Single(plan.Steps);
        Assert.Equal("createdvd", plan.Steps[0].Capability.Command);
    }

    // =====================================================================
    // G-05  ConversionPolicyEvaluator — Unknown integrity + all-lossless → Safe
    // =====================================================================

    [Fact]
    public void ConversionPolicyEvaluator_UnknownIntegrity_AllLosslessPath_IsSafe()
    {
        var ev = new ConversionPolicyEvaluator();
        var losslessEdge = new ConversionCapability
        {
            SourceExtension = ".iso",
            TargetExtension = ".chd",
            Tool = new ToolRequirement { ToolName = "chdman" },
            Command = "convert",
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };

        var safety = ev.EvaluateSafety(ConversionPolicy.Auto, SourceIntegrity.Unknown, [losslessEdge], allToolsAvailable: true);

        // Unknown + all-lossless path should not be blocked or risky: falls through to Safe
        Assert.Equal(ConversionSafety.Safe, safety);
    }

    [Fact]
    public void ConversionPolicyEvaluator_ArchiveOnly_LosslessSource_WithLossyPath_IsRisky()
    {
        var ev = new ConversionPolicyEvaluator();
        var lossyEdge = new ConversionCapability
        {
            SourceExtension = ".iso",
            TargetExtension = ".cso",
            Tool = new ToolRequirement { ToolName = "ciso" },
            Command = "compress",
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 3,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };

        // ArchiveOnly policy is not ManualOnly → should follow the lossless-source+lossy-path → Risky rule
        var safety = ev.EvaluateSafety(ConversionPolicy.ArchiveOnly, SourceIntegrity.Lossless, [lossyEdge], allToolsAvailable: true);

        Assert.Equal(ConversionSafety.Risky, safety);
    }

    [Fact]
    public void ConversionPolicyEvaluator_GetEffectivePolicy_CaseInsensitive_ARCADE()
    {
        var ev = new ConversionPolicyEvaluator();
        Assert.Equal(ConversionPolicy.None, ev.GetEffectivePolicy("arcade", ConversionPolicy.Auto));
        Assert.Equal(ConversionPolicy.None, ev.GetEffectivePolicy("Arcade", ConversionPolicy.Auto));
    }

    // =====================================================================
    // G-06  PolicyEngine — empty snapshot, required-extension match, compliant
    // =====================================================================

    [Fact]
    public void PolicyEngine_EmptySnapshot_NoViolations_IsCompliant()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries = [],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 0 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy
        {
            Id = "strict",
            AllowedExtensions = [".chd"],
            PreferredRegions = ["EU"]
        };

        var report = engine.Validate(snapshot, policy);

        Assert.True(report.IsCompliant);
        Assert.Empty(report.Violations);
        Assert.Equal(0, report.Summary.Total);
    }

    [Fact]
    public void PolicyEngine_EmptyPolicy_AllRulesEmpty_IsCompliant()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries =
            [
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\PS1\game.iso",
                    FileName = "game.iso",
                    Extension = ".iso",
                    ConsoleKey = "PS1",
                    GameKey = "game",
                    Region = "US"
                }
            ],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 1 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy { Id = "open" };  // all rule sets empty

        var report = engine.Validate(snapshot, policy);

        Assert.True(report.IsCompliant);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void PolicyEngine_RequiredExtensionByConsole_MatchingEntry_NoViolation()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries =
            [
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\PS1\game.chd",
                    FileName = "game.chd",
                    Extension = ".chd",
                    ConsoleKey = "PS1",
                    GameKey = "game",
                    Region = "US"
                }
            ],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 1 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy
        {
            Id = "chd-only",
            RequiredExtensionsByConsole = new Dictionary<string, string[]>
            {
                ["PS1"] = [".chd"]
            }
        };

        var report = engine.Validate(snapshot, policy);

        Assert.True(report.IsCompliant);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void PolicyEngine_RequiredExtensionByConsole_NonMatchingEntry_ProducesViolation()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries =
            [
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\PS1\game.iso",
                    FileName = "game.iso",
                    Extension = ".iso",
                    ConsoleKey = "PS1",
                    GameKey = "game",
                    Region = "US"
                }
            ],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 1 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy
        {
            Id = "chd-only",
            RequiredExtensionsByConsole = new Dictionary<string, string[]>
            {
                ["PS1"] = [".chd"]
            }
        };

        var report = engine.Validate(snapshot, policy);

        Assert.False(report.IsCompliant);
        Assert.Single(report.Violations);
        Assert.Equal("required-extension-by-console", report.Violations[0].RuleId);
        Assert.Equal("error", report.Violations[0].Severity);
    }

    [Fact]
    public void PolicyEngine_RequiredExtensionByConsole_DifferentConsole_NoViolation()
    {
        // The required-extension rule is console-scoped; entries for other consoles are unaffected.
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries =
            [
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\SNES\game.sfc",
                    FileName = "game.sfc",
                    Extension = ".sfc",
                    ConsoleKey = "SNES",
                    GameKey = "game",
                    Region = "US"
                }
            ],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 1 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy
        {
            Id = "ps1-chd",
            RequiredExtensionsByConsole = new Dictionary<string, string[]>
            {
                ["PS1"] = [".chd"]   // only PS1 is constrained
            }
        };

        var report = engine.Validate(snapshot, policy);

        Assert.True(report.IsCompliant);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void PolicyEngine_Validate_PolicyIdFallback_WhenIdIsWhitespace()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Entries = [],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary()
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy { Id = "   ", Name = "   " };

        var report = engine.Validate(snapshot, policy);

        Assert.Equal("unnamed-policy", report.PolicyId);
        Assert.Equal("unnamed-policy", report.PolicyName);
    }

    [Fact]
    public void PolicyEngine_Validate_PolicyNameFallsBackToId_WhenNameIsWhitespace()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Entries = [],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary()
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy { Id = "my-policy", Name = "   " };

        var report = engine.Validate(snapshot, policy);

        Assert.Equal("my-policy", report.PolicyId);
        Assert.Equal("my-policy", report.PolicyName);
    }

    [Fact]
    public void PolicyEngine_MultipleViolationsSameRule_AllRecorded()
    {
        var engine = new Romulus.Core.Policy.PolicyEngine();
        var snapshot = new Romulus.Contracts.Models.LibrarySnapshot
        {
            GeneratedUtc = DateTime.UtcNow,
            Roots = [@"C:\roms"],
            Entries =
            [
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\PS1\game1.iso",
                    FileName = "game1.iso",
                    Extension = ".iso",
                    ConsoleKey = "PS1",
                    GameKey = "game1",
                    Region = "US"
                },
                new Romulus.Contracts.Models.LibrarySnapshotEntry
                {
                    Path = @"C:\roms\PS1\game2.iso",
                    FileName = "game2.iso",
                    Extension = ".iso",
                    ConsoleKey = "PS1",
                    GameKey = "game2",
                    Region = "EU"
                }
            ],
            Summary = new Romulus.Contracts.Models.LibrarySnapshotSummary { TotalEntries = 2 }
        };
        var policy = new Romulus.Contracts.Models.LibraryPolicy
        {
            Id = "strict",
            AllowedExtensions = [".chd"]  // both entries violate this
        };

        var report = engine.Validate(snapshot, policy);

        Assert.False(report.IsCompliant);
        Assert.Equal(2, report.Violations.Length);
        Assert.All(report.Violations, v => Assert.Equal("allowed-extensions", v.RuleId));
        Assert.Equal(2, report.Summary.ByRule["allowed-extensions"]);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static ConversionCapability Edge(
        string source,
        string target,
        string tool,
        int cost,
        string command = "convert",
        bool lossless = true,
        SourceIntegrity resultIntegrity = SourceIntegrity.Lossless)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = command,
            ResultIntegrity = resultIntegrity,
            Lossless = lossless,
            Cost = cost,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };
    }

    private static ConversionCapability WildcardEdge(
        string source,
        string target,
        string tool,
        int cost)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = "convert",
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = cost,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };
    }

    private static ConversionCapability Cap(
        string source,
        string target,
        string tool,
        int cost,
        string command = "convert",
        string consoleKey = "PS1",
        ConversionCondition condition = ConversionCondition.None,
        bool lossless = true)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = command,
            ApplicableConsoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { consoleKey },
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = lossless,
            Cost = cost,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = condition
        };
    }

    private static IConversionRegistry CreateRegistry(
        IReadOnlyList<ConversionCapability> capabilities,
        ConversionPolicy policy,
        string? preferredTarget,
        IReadOnlyList<string>? alternativeTargets = null)
    {
        return new FakeRegistry(capabilities, policy, preferredTarget, alternativeTargets ?? []);
    }

    private static IConversionRegistry FakeRegistryWithNoTarget(
        IReadOnlyList<ConversionCapability> capabilities,
        ConversionPolicy policy)
    {
        return new FakeRegistry(capabilities, policy, null, []);
    }

    private sealed class FakeRegistry(
        IReadOnlyList<ConversionCapability> capabilities,
        ConversionPolicy policy,
        string? preferredTarget,
        IReadOnlyList<string> alternatives) : IConversionRegistry
    {
        public IReadOnlyList<ConversionCapability> GetCapabilities() => capabilities;
        public ConversionPolicy GetPolicy(string consoleKey) => policy;
        public string? GetPreferredTarget(string consoleKey) => preferredTarget;
        public IReadOnlyList<string> GetAlternativeTargets(string consoleKey) => alternatives;
    }
}
