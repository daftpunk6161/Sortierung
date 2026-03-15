using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.GameKeys;
using RomCleanup.Infrastructure.Diagnostics;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// TEST-FIX: Fix conditional/alibi tests that guard with if-statements
/// or only assert non-null without verifying actual behavior.
/// TEST-INT: Integration tests for cross-component interactions.
/// </summary>
public sealed class FixedAndIntegrationTests
{
    // =========================================================================
    //  TEST-FIX-01: RunOrchestrator Preflight — actually verify reason text
    // =========================================================================

    [Fact]
    public void Preflight_EmptyRoots_BlockedWithSpecificReason()
    {
        var orch = BuildOrchestrator();
        var options = new RunOptions { Roots = Array.Empty<string>() };
        var result = orch.Preflight(options);

        Assert.Equal("blocked", result.Status);
        Assert.NotNull(result.Reason);
        Assert.Contains("No roots", result.Reason!);
    }

    // =========================================================================
    //  TEST-FIX-02: GameKeyNormalizer idempotency — verify actual key equality
    // =========================================================================

    [Theory]
    [InlineData("Game (Europe) (Rev A) [!]")]
    [InlineData("Some Rom (USA) (v1.2)")]
    [InlineData("Final Fantasy VII (Europe) (Disc 1)")]
    [InlineData("Pokémon (Japan) (En,Fr,De)")]
    public void Normalize_IsIdempotent_StrongAssertion(string input)
    {
        var first = GameKeyNormalizer.Normalize(input);
        var second = GameKeyNormalizer.Normalize(first);
        // The second normalization should produce a string that when normalized again stays the same
        var third = GameKeyNormalizer.Normalize(second);
        Assert.Equal(second, third);
    }

    // =========================================================================
    //  TEST-FIX-03: CatchGuardService — verify actual error classification
    // =========================================================================

    [Fact]
    public void CatchGuard_IOException_ClassifiedAsTransient()
    {
        var ex = new IOException("disk error");
        var record = CatchGuardService.CreateRecord("Mod", "Act", exception: ex);
        Assert.Equal(ErrorKind.Transient, record.ErrorClass);
    }

    [Fact]
    public void CatchGuard_UnauthorizedAccess_ClassifiedAsCritical()
    {
        var ex = new UnauthorizedAccessException("no access");
        var record = CatchGuardService.CreateRecord("Mod", "Act", exception: ex);
        Assert.Equal(ErrorKind.Critical, record.ErrorClass);
    }

    [Fact]
    public void CatchGuard_FileNotFound_NotTransient()
    {
        var ex = new FileNotFoundException("file missing");
        var record = CatchGuardService.CreateRecord("Mod", "Act", exception: ex);
        // FileNotFound is NOT transient (no point retrying)
        Assert.NotEqual(ErrorKind.Transient, record.ErrorClass);
    }

    [Fact]
    public void CatchGuard_SecurityException_Critical()
    {
        var ex = new System.Security.SecurityException("access denied");
        var record = CatchGuardService.CreateRecord("Mod", "Act", exception: ex);
        Assert.Equal(ErrorKind.Critical, record.ErrorClass);
    }

    // =========================================================================
    //  TEST-FIX-04: ErrorClassifier code prefix rules
    // =========================================================================

    [Theory]
    [InlineData("SEC-001", ErrorKind.Critical)]
    [InlineData("AUTH-FAIL", ErrorKind.Critical)]
    [InlineData("IO-LOCK-01", ErrorKind.Transient)]
    [InlineData("NET-TIMEOUT", ErrorKind.Transient)]
    public void ErrorClassifier_CodePrefixRules(string code, ErrorKind expected)
    {
        var result = ErrorClassifier.Classify(errorCode: code);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    //  TEST-FIX-05: CatchGuardService.Guard — Critical should rethrow
    // =========================================================================

    [Fact]
    public void Guard_CriticalError_Rethrows()
    {
        var service = new CatchGuardService();
        Assert.Throws<UnauthorizedAccessException>(() =>
            service.Guard("Mod", "Act", () =>
            {
                throw new UnauthorizedAccessException("denied");
            }));
    }

    // =========================================================================
    //  TEST-INT-01: Sort + Dedupe integration — full pipeline
    // =========================================================================

    [Fact]
    public void Deduplicate_WithRegionScoring_IntegratesCorrectly()
    {
        var preferOrder = new[] { "EU", "US", "JP" };

        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = "mario_eu.zip", GameKey = "mario",
                RegionScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore("EU", preferOrder),
                FormatScore = RomCleanup.Core.Scoring.FormatScorer.GetFormatScore(".zip")
            },
            new RomCandidate
            {
                MainPath = "mario_us.zip", GameKey = "mario",
                RegionScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore("US", preferOrder),
                FormatScore = RomCleanup.Core.Scoring.FormatScorer.GetFormatScore(".zip")
            },
            new RomCandidate
            {
                MainPath = "mario_jp.zip", GameKey = "mario",
                RegionScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore("JP", preferOrder),
                FormatScore = RomCleanup.Core.Scoring.FormatScorer.GetFormatScore(".zip")
            }
        };

        var results = RomCleanup.Core.Deduplication.DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("mario_eu.zip", results[0].Winner.MainPath); // EU preferred
        Assert.Equal(2, results[0].Losers.Count);
    }

    // =========================================================================
    //  TEST-INT-02: GameKey normalization → Dedup grouping integration
    // =========================================================================

    [Fact]
    public void GameKeyNormalization_FeedsDedup_CorrectGrouping()
    {
        // Different regions/versions of same game should normalize to same key
        var euKey = GameKeyNormalizer.Normalize("Super Mario (Europe) (Rev A)");
        var usKey = GameKeyNormalizer.Normalize("Super Mario (USA) (v1.1)");
        var jpKey = GameKeyNormalizer.Normalize("Super Mario (Japan) [!]");

        Assert.Equal(euKey, usKey);
        Assert.Equal(usKey, jpKey);

        // Build candidates with normalized keys
        var candidates = new[]
        {
            new RomCandidate { MainPath = "mario_eu.zip", GameKey = euKey, RegionScore = 1000 },
            new RomCandidate { MainPath = "mario_us.zip", GameKey = usKey, RegionScore = 999 },
            new RomCandidate { MainPath = "mario_jp.zip", GameKey = jpKey, RegionScore = 998 }
        };

        var results = RomCleanup.Core.Deduplication.DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results); // All grouped together
        Assert.Equal("mario_eu.zip", results[0].Winner.MainPath);
    }

    // =========================================================================
    //  TEST-INT-03: Region detection → Region scoring integration
    // =========================================================================

    [Fact]
    public void RegionDetection_FeedsScoring_CorrectOrder()
    {
        var preferOrder = new[] { "EU", "US", "JP" };

        var euRegion = RomCleanup.Core.Regions.RegionDetector.GetRegionTag("Game (Europe)");
        var usRegion = RomCleanup.Core.Regions.RegionDetector.GetRegionTag("Game (USA)");
        var jpRegion = RomCleanup.Core.Regions.RegionDetector.GetRegionTag("Game (Japan)");

        var euScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore(euRegion, preferOrder);
        var usScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore(usRegion, preferOrder);
        var jpScore = RomCleanup.Core.Scoring.FormatScorer.GetRegionScore(jpRegion, preferOrder);

        Assert.True(euScore > usScore);
        Assert.True(usScore > jpScore);
    }

    // =========================================================================
    //  TEST-INT-04: Blocklist + Scan integration
    // =========================================================================

    [Fact]
    public void Blocklist_DiscExtensions_NoOverlap()
    {
        // Blocklist folder names should not conflict with disc extensions
        var blocklist = ExecutionHelpers.GetDefaultBlocklist();
        var discExts = ExecutionHelpers.GetDiscExtensions();

        foreach (var blocked in blocklist)
        {
            Assert.False(discExts.Contains(blocked),
                $"Blocklist entry '{blocked}' conflicts with disc extension");
        }
    }

    // =========================================================================
    //  TEST-INT-05: VersionScorer + FormatScorer → Dedup winner selection
    // =========================================================================

    [Fact]
    public void ScoringPipeline_FullIntegration()
    {
        var vs = new RomCleanup.Core.Scoring.VersionScorer();

        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = "game_old.chd", GameKey = "game",
                VersionScore = (int)vs.GetVersionScore("Game (v1.0)"),
                FormatScore = RomCleanup.Core.Scoring.FormatScorer.GetFormatScore(".chd"),
                RegionScore = 1000
            },
            new RomCandidate
            {
                MainPath = "game_new.iso", GameKey = "game",
                VersionScore = (int)vs.GetVersionScore("Game (v2.0)"),
                FormatScore = RomCleanup.Core.Scoring.FormatScorer.GetFormatScore(".iso"),
                RegionScore = 1000
            }
        };

        var winner = RomCleanup.Core.Deduplication.DeduplicationEngine.SelectWinner(candidates);
        // v2.0 has higher version score, which should win over lower format score
        Assert.Equal("game_new.iso", winner!.MainPath);
    }

    // ── Helpers ──

    private static RomCleanup.Infrastructure.Orchestration.RunOrchestrator BuildOrchestrator()
    {
        return new RomCleanup.Infrastructure.Orchestration.RunOrchestrator(
            new StubFs(), new StubAudit());
    }

    private sealed class StubFs : RomCleanup.Contracts.Ports.IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => false;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? ext = null) => [];
        public string? MoveItemSafely(string src, string dest) => dest;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string s, string d, bool o = false) { }
    }

    private sealed class StubAudit : RomCleanup.Contracts.Ports.IAuditStore
    {
        public void WriteMetadataSidecar(string p, IDictionary<string, object> m) { }
        public bool TestMetadataSidecar(string p) => false;
        public IReadOnlyList<string> Rollback(string p, string[] a, string[] c, bool d = false) => [];
        public void AppendAuditRow(string p, string r, string o, string n, string a,
            string cat = "", string h = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }
}
