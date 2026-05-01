using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-7 Snapshot-Pin: jeder Test-File, der <c>Path.GetTempPath()</c> oder
/// <c>Path.GetTempFileName()</c> nutzt, soll seine temporaeren Artefakte ueber
/// <c>IDisposable</c> / <c>IAsyncDisposable</c> / <c>IClassFixture</c> sauber
/// abraeumen. Bestand zum Zeitpunkt der Pin-Einfuehrung ist eingefroren; neue
/// Verstoesse brechen den Build.
///
/// Wenn ein File aus der Allowlist konvertiert wurde (oder geloescht), darf es
/// hier entfernt werden. Wenn ein neuer Verstoss legitim ist (sehr selten),
/// muss er hier explizit dokumentiert ergaenzt werden - kein Auto-Update.
/// </summary>
public sealed class TempFileCleanupConventionTests
{
    // Allowlist: Bestand am 2026-05-01 nach F-2/F-3/F-4 + Hang-Fixes.
    // Quelle: docs/audits/critic-findings (artifacts/critic-findings.md F-7).
    // Reduktion erlaubt + erwuenscht. Erweiterung durch CI-Roten-Test blockiert.
    private static readonly string[] LegacyAllowlist =
    [
        "AllowedRootPathPolicyCoverageTests.cs",
        "ApiIntegrationTests.cs",
        "ApiProductizationIntegrationTests.cs",
        "ApiTestFactory.cs",
        "ApiValidationIntegrationTests.cs",
        "AppStoragePathResolverTests.cs",
        "ArchiveHashServiceZipBombTests.cs",
        "AuditCDRedTests.cs",
        "AuditSigningServiceCoverageTests.cs",
        "ClassificationP2RegressionTests.cs",
        "CliApiParityAuditTests.cs",
        "CliOutputWriterCoverageTests.cs",
        "CollectionCompareNormalizationCoverageTests.cs",
        "ConsoleDetectorTests.cs",
        "Conversion/ConversionExecutorHardeningTests.cs",
        "Conversion/FormatConverterArchiveSecurityTests.cs",
        "Conversion/Phase4ConversionInvariantTests.cs",
        "Conversion/ToolInvokerAdapterHardeningTests.cs",
        "ConversionSafetyAdvisorTests.cs",
        "CoverageGapBatchTests.cs",
        "Crc32Tests.cs",
        "DatAuditViewModelTests.cs",
        "DatCatalogViewModelCoverageTests.cs",
        "DatRenamePipelinePhaseIssue9RedTests.cs",
        "DetectionPipelineTests.cs",
        "FormatConverterAdapterTests.cs",
        "GuiViewModelTests.AccessibilityAndRedTests.cs",
        "GuiViewModelTests.cs",
        "HeaderSecurityServiceDelegationTests.cs",
        "HygieneBlockTests.cs",
        "HygieneCleanupRegressionTests.cs",
        "MainViewModelRootDropTests.cs",
        "PathAndStemHelperCoverageTests.cs",
        "Phase4FixTests.cs",
        "Phase5AuditRefactorTests.cs",
        "Phase5BPipelineInvariantTests.cs",
        "Phase5CEntryPointParityTests.cs",
        "Phase5DDeterminismTests.cs",
        "Phase6QualityAssuranceTests.cs",
        "PhaseMetricsCollectorTests.cs",
        "PipelineAndConversionCoverageTests.cs",
        "ReportGeneratorTests.cs",
        "ReportPathResolverTests.cs",
        "RunManagerTests.cs",
        "RunOptionsBuilderCoverageTests.cs",
        "SafetyValidatorTests.cs",
        "UncPathTests.cs",
        "Wave2AuditViewerApiTests.cs",
        "Wave4TelemetryOptInTests.cs",
    ];

    private static readonly Regex TempUsageRegex = new(
        @"Path\.GetTempPath\(|Path\.GetTempFileName\(",
        RegexOptions.Compiled);

    private static readonly Regex DisposableRegex = new(
        @":\s*IDisposable|,\s*IDisposable|:\s*IAsyncDisposable|,\s*IAsyncDisposable|IClassFixture<",
        RegexOptions.Compiled);

    [Fact]
    public void TestFiles_UsingTempPaths_MustImplementIDisposable_OrBeAllowlisted()
    {
        var testRoot = LocateTestProjectRoot();
        var allowlist = LegacyAllowlist
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var violations = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(full =>
            {
                var rel = Path.GetRelativePath(testRoot, full);
                return new { Full = full, Relative = rel };
            })
            .Where(f =>
            {
                // Self-exclude: dieser Pin-Test selbst, plus generated obj/bin.
                if (f.Relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase)) return false;
                if (f.Relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase)) return false;
                if (f.Relative.EndsWith("TempFileCleanupConventionTests.cs", System.StringComparison.OrdinalIgnoreCase)) return false;

                var text = File.ReadAllText(f.Full);
                return TempUsageRegex.IsMatch(text) && !DisposableRegex.IsMatch(text);
            })
            .Select(f => f.Relative)
            .ToList();

        var newViolations = violations
            .Where(v => !allowlist.Contains(v))
            .OrderBy(v => v, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(
            newViolations.Count == 0,
            $"Neue Test-Files nutzen Path.GetTempPath()/GetTempFileName(), implementieren aber " +
            $"weder IDisposable/IAsyncDisposable noch IClassFixture<>. Bitte einen sauberen " +
            $"try/finally-Cleanup-Lifecycle einfuehren ODER (nur in begruendeten Ausnahmen) " +
            $"die Allowlist in TempFileCleanupConventionTests aktualisieren.\n\n" +
            $"Neue Verstoesse:\n  - " + string.Join("\n  - ", newViolations));
    }

    [Fact]
    public void Allowlist_ContainsOnlyExistingFiles_AndIsNotStale()
    {
        // Diese Assertion sorgt dafuer, dass die Allowlist nicht still
        // verrottet, sobald ein File geloescht oder renamed wird, oder ein
        // bereits konvertiertes File noch in der Allowlist steht. Ein roter
        // Test fordert hier eine Allowlist-Pflege, kein Verhaltens-Bypass.
        var testRoot = LocateTestProjectRoot();
        var allowlist = LegacyAllowlist
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var existingViolations = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(full =>
            {
                var rel = Path.GetRelativePath(testRoot, full);
                return new { Full = full, Relative = rel };
            })
            .Where(f =>
            {
                if (f.Relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase)) return false;
                if (f.Relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", System.StringComparison.OrdinalIgnoreCase)) return false;
                if (f.Relative.EndsWith("TempFileCleanupConventionTests.cs", System.StringComparison.OrdinalIgnoreCase)) return false;
                var text = File.ReadAllText(f.Full);
                return TempUsageRegex.IsMatch(text) && !DisposableRegex.IsMatch(text);
            })
            .Select(f => f.Relative)
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var staleEntries = allowlist
            .Where(entry => !existingViolations.Contains(entry))
            .OrderBy(e => e, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(
            staleEntries.Count == 0,
            $"Allowlist enthaelt Eintraege, die nicht mehr in Verstoss sind " +
            $"(File geloescht, renamed oder bereits konvertiert). Bitte aus " +
            $"LegacyAllowlist entfernen, damit die Konvention monoton enger wird.\n\n" +
            $"Veraltete Eintraege:\n  - " + string.Join("\n  - ", staleEntries));
    }

    private static string LocateTestProjectRoot()
    {
        // Walk hoch von AppContext.BaseDirectory bis Romulus.Tests.csproj sichtbar.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Romulus.Tests.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Romulus.Tests project root nicht gefunden.");
    }
}
