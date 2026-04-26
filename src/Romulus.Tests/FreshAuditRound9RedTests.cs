using System.Reflection;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Deduplication;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// RED-phase tests for Round 9 audit findings.
/// Each test targets a real bug, invariant violation, or hygiene gap.
/// </summary>
public sealed class FreshAuditRound9RedTests
{
    // ═══════════════════════════════════════════════════════════════════
    // F1: SEC – MapConfigurationError must NOT leak raw ex.Message to API
    // ConfigurationValidationException.Message may contain filesystem paths
    // or internal details → must be sanitized before returning to client
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F1_MapConfigurationError_MustNotReturnRawExceptionMessage()
    {
        // Arrange: exception with a sensitive filesystem path in message
        var sensitiveMessage = @"Path 'C:\Users\admin\secret\roms' is not accessible";
        var ex = new ConfigurationValidationException(
            ConfigurationErrorCode.InvalidPath, sensitiveMessage);

        // Act
        var (code, message) = InvokeMapConfigurationError(ex, "RUN");

        // Assert: returned message must NOT contain the raw sensitive path
        Assert.DoesNotContain(@"C:\Users\admin\secret\roms", message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.Equals(message, sensitiveMessage, StringComparison.Ordinal),
            "MapConfigurationError must not return the raw exception message to API clients.");
    }

    [Fact]
    public void F1_MapConfigurationError_MustReturnSanitizedMessage_ForAllErrorCodes()
    {
        var codes = Enum.GetValues<ConfigurationErrorCode>();
        foreach (var errorCode in codes)
        {
            var ex = new ConfigurationValidationException(errorCode,
                $"Internal detail for {errorCode}: path=C:\\internal\\data");

            var (code, message) = InvokeMapConfigurationError(ex, "RUN");

            Assert.DoesNotContain("C:\\internal\\data", message, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(code), $"Error code must not be empty for {errorCode}");
            Assert.False(string.IsNullOrWhiteSpace(message), $"Message must not be empty for {errorCode}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // F2: HYGIENE – DatCatalogViewModel bare catch must log exception
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F2_DatCatalogViewModel_DownloadCatch_MustLogException()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.UI.Wpf",
            "ViewModels", "DatCatalogViewModel.cs"));

        // The bare catch { failed++; } must be replaced with explicit exception logging
        Assert.DoesNotContain("catch { failed++; }", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F3: HYGIENE – FeatureService.Infra bare catch must log
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F3_FeatureServiceInfra_HighContrast_MustNotSilentlySwallow()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.UI.Wpf",
            "Services", "FeatureService.Infra.cs"));

        // Bare catch { return false; } swallows registry errors silently
        Assert.DoesNotContain("catch { return false; }", source, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F4: ARCHITECTURE – PhaseMetricsCollector.Export bypasses IFileSystem
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F4_PhaseMetricsCollector_MustNotUseDirectFileWriteAllText()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure",
            "Metrics", "PhaseMetricsCollector.cs"));

        // Extract only the PhaseMetricsCollector class body (before MetricsFileWriter)
        var classEnd = source.IndexOf("internal static class MetricsFileWriter", StringComparison.Ordinal);
        var collectorSource = classEnd > 0 ? source[..classEnd] : source;

        // Direct File.WriteAllText bypasses IFileSystem abstraction
        Assert.DoesNotContain("File.WriteAllText(", collectorSource, StringComparison.Ordinal);
    }

    [Fact]
    public void F4_PhaseMetricsCollector_MustNotUseDirectFileMove()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure",
            "Metrics", "PhaseMetricsCollector.cs"));

        // Extract only the PhaseMetricsCollector class body (before MetricsFileWriter)
        var classEnd = source.IndexOf("internal static class MetricsFileWriter", StringComparison.Ordinal);
        var collectorSource = classEnd > 0 ? source[..classEnd] : source;

        Assert.DoesNotContain("File.Move(", collectorSource, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F5: CORRECTNESS – CrossRootDeduplicator score path must be
    // deterministic and NOT diverge from DeduplicationEngine
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F5_CrossRootDeduplicator_MustMatchDeduplicationEngine_WhenScoresPreset()
    {
        // Arrange: files with preset scores that differ from computed ones
        // CrossRootDeduplicator uses preset scores (file.RegionScore != 0)
        // while DeduplicationEngine uses its own scoring cascade.
        // These must NOT diverge.
        var files = new List<CrossRootFile>
        {
            new()
            {
                Path = @"C:\root1\Super Mario Bros (USA).nes",
                Root = @"C:\root1",
                Hash = "abc123",
                Extension = ".nes",
                SizeBytes = 1024,
                Region = "US",
                // Preset a RegionScore that may differ from computed
                RegionScore = 100,
                FormatScore = 90,
                VersionScore = 50,
            },
            new()
            {
                Path = @"C:\root2\Super Mario Bros (Europe).nes",
                Root = @"C:\root2",
                Hash = "abc123",
                Extension = ".nes",
                SizeBytes = 1024,
                Region = "EU",
                // Preset different scores
                RegionScore = 200,
                FormatScore = 90,
                VersionScore = 50,
            }
        };

        var group = new CrossRootDuplicateGroup { Hash = "abc123", Files = files };

        // Use same regions as the default
        var regions = new[] { "EU", "US", "WORLD", "JP" };

        // Act via CrossRootDeduplicator
        var advice = CrossRootDeduplicator.GetMergeAdvice(group, regions);

        // Act via DeduplicationEngine directly with same preset scores
        var candidates = files.Select(f => new RomCandidate
        {
            MainPath = f.Path,
            GameKey = Core.GameKeys.GameKeyNormalizer.Normalize(Path.GetFileName(f.Path)),
            Region = f.Region ?? "UNKNOWN",
            RegionScore = f.RegionScore,
            FormatScore = f.FormatScore,
            VersionScore = f.VersionScore,
            SizeTieBreakScore = f.SizeTieBreakScore,
            SizeBytes = f.SizeBytes,
            Extension = f.Extension,
        }).ToList();
        var engineWinner = DeduplicationEngine.SelectWinner(candidates);

        // Assert: both must pick the same winner
        Assert.NotNull(advice.Keep);
        Assert.NotNull(engineWinner);
        Assert.Equal(
            Path.GetFileName(engineWinner.MainPath),
            Path.GetFileName(advice.Keep.Path),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void F5_CrossRootDeduplicator_MustNotRecomputeScores_WhenAlreadySet()
    {
        // Verify that CrossRootDeduplicator does NOT silently override preset scores
        // with recomputed values (which could diverge from the score source).
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure",
            "Deduplication", "CrossRootDeduplicator.cs"));

        // F5 invariant: preset scores must be preserved. Acceptable patterns are either
        //   "file.RegionScore != 0 ? file.RegionScore : ..."  (preserve when non-zero)
        // or
        //   "recomputePresetScores && file.RegionScore == 0 ? ... : file.RegionScore" (gated re-compute)
        // Either way, the source must demonstrably guard against unconditional overwrites
        // by referencing each preset score field inside a conditional expression.
        static bool HasGuard(string src, string field)
            => src.Contains($"file.{field} != 0", StringComparison.Ordinal)
            || src.Contains($"file.{field} == 0", StringComparison.Ordinal);

        Assert.True(HasGuard(source, "RegionScore"),
            "CrossRootDeduplicator must guard preset RegionScore against unconditional re-compute.");
        Assert.True(HasGuard(source, "FormatScore"),
            "CrossRootDeduplicator must guard preset FormatScore against unconditional re-compute.");
        Assert.True(HasGuard(source, "VersionScore"),
            "CrossRootDeduplicator must guard preset VersionScore against unconditional re-compute.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static (string Code, string Message) InvokeMapConfigurationError(
        ConfigurationValidationException ex, string prefix)
    {
        // Use reflection to call the internal static method
        var programType = typeof(Program);
        var method = programType.GetMethod("MapConfigurationError",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (method is null)
            throw new InvalidOperationException("MapConfigurationError not found on Program.");

        var result = method.Invoke(null, [ex, prefix]);
        var tuple = ((string Code, string Message))result!;
        return tuple;
    }

    private static string FindRepoFile(params string[] segments)
        => Romulus.Tests.TestFixtures.RepoPaths.RepoFile(segments);
}
