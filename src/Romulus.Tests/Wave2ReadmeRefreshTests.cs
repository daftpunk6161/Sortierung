using System.IO;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 2 — T-W2-README-REFRESH pin tests.
/// Acceptance gates from docs/plan/strategic-reduction-2026/plan.yaml:
///   * README darf keine in Welle 1 gestrichenen Feature-Namen mehr nennen.
///   * README darf keine Avalonia-GUI mehr als aktive zweite GUI bewerben.
///   * README darf keinen "Top-N USPs" Marketing-Listenstil mehr verwenden.
///   * competitive-analysis.md darf nicht behaupten "Romulus ist besser als X".
///   * Beide Dokumente nennen Persona, sechs Hauptaktionen und drei USPs (Audit,
///     Rollback, deterministisches Cleanup) explizit.
/// </summary>
public sealed class Wave2ReadmeRefreshTests
{
    private static readonly string[] CulledFeatureMentions =
    [
        "Frontend Export",
        "Frontend-Export 11",
        "ScreenScraper",
        "RetroAchievement",
        "ROM-Patching",
        "ROM Patching",
        "MAME-Set-Building",
        "MAME Set-Building",
        "In-Browser Play",
        "In-Browser-Play",
        "Plugin/Marketplace",
        "Marketplace",
    ];

    private static readonly string[] MarketingSuperlatives =
    [
        "Top-5 USPs",
        "Top-10 USPs",
        "Top-N USPs",
        "Your Collection, Perfected",
        "schweizer Taschenmesser",
        "Goldstandard",
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null
               && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!;
    }

    private static string ReadReadme()
        => File.ReadAllText(Path.Combine(RepoRoot().FullName, "README.md"));

    private static string ReadCompetitiveAnalysis()
        => File.ReadAllText(Path.Combine(RepoRoot().FullName,
            "docs", "product", "competitive-analysis.md"));

    [Fact]
    public void Readme_DoesNotMentionCulledFeatures()
    {
        var src = ReadReadme();
        // Cull-Feature-Namen duerfen nur im "Was Romulus bewusst nicht macht"
        // Block vorkommen. Pin-Test schneidet den Block raus und prueft den Rest.
        var marker = "Was Romulus bewusst nicht macht";
        var cutoff = src.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.True(cutoff > 0, "README muss einen 'Was Romulus bewusst nicht macht'-Abschnitt haben.");
        var head = src[..cutoff];

        foreach (var symbol in CulledFeatureMentions)
        {
            Assert.False(head.Contains(symbol, System.StringComparison.OrdinalIgnoreCase),
                $"README erwaehnt vor dem Cull-Block noch das gestrichene Feature '{symbol}'.");
        }
    }

    [Fact]
    public void Readme_DoesNotAdvertiseSecondGui()
    {
        var src = ReadReadme();
        // "Avalonia" darf nur im Hinweis-Block erwaehnt werden, der die
        // Archivierung dokumentiert. Sonstige Werbung ist verboten.
        Assert.DoesNotContain("Avalonia 11", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Avalonia, Standard", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WPF Legacy", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Romulus.UI.Avalonia", src, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_NamesPersonaAndSixActionsAndThreeUsps()
    {
        var src = ReadReadme();
        Assert.Contains("Sammler", src, System.StringComparison.Ordinal);
        Assert.Contains("Sechs Hauptaktionen", src, System.StringComparison.Ordinal);
        Assert.Contains("Drei USPs", src, System.StringComparison.Ordinal);
        Assert.Contains("Audit-Trail", src, System.StringComparison.Ordinal);
        Assert.Contains("Rollback", src, System.StringComparison.Ordinal);
        Assert.Contains("Deterministisches Cleanup", src, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DoesNotUseMarketingTopNStyle()
    {
        var src = ReadReadme();
        foreach (var phrase in MarketingSuperlatives)
        {
            Assert.False(src.Contains(phrase, System.StringComparison.OrdinalIgnoreCase),
                $"README enthaelt verbotene Marketing-Phrase '{phrase}'.");
        }
    }

    [Fact]
    public void CompetitiveAnalysis_DoesNotClaimSuperiority()
    {
        var src = ReadCompetitiveAnalysis();
        // Keine "Romulus ist besser als"-Behauptungen.
        Assert.DoesNotContain("Romulus ist besser", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kein anderes Tool", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("einzige ROM-Management-Tool", src, System.StringComparison.OrdinalIgnoreCase);
        // Top-N USPs Stil ist verboten (war: "Top-5 USPs:")
        Assert.DoesNotContain("Top-5 USPs", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Top-10 USPs", src, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompetitiveAnalysis_NamesComplementaryStanceAndCullList()
    {
        var src = ReadCompetitiveAnalysis();
        Assert.Contains("anderes Problem", src, System.StringComparison.Ordinal);
        Assert.Contains("feature-cull-list.md", src, System.StringComparison.Ordinal);
        // Persona + drei USPs muessen auch hier sichtbar sein.
        Assert.Contains("Sammler", src, System.StringComparison.Ordinal);
        Assert.Contains("Audit", src, System.StringComparison.Ordinal);
        Assert.Contains("Rollback", src, System.StringComparison.Ordinal);
        Assert.Contains("deterministisch", src, System.StringComparison.OrdinalIgnoreCase);
    }
}
