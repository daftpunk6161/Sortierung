using System.IO;
using System.Linq;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 2 — T-W2-AUDIT-MORATORIUM-DECREE pin tests.
/// Acceptance gates from docs/plan/strategic-reduction-2026/plan.yaml:
///   * Genau eine Quelle (AGENTS.md) deklariert das Moratorium mit
///     Geltungszeitraum, Verbots-/Erlaubnis-Scope, Sanktion und Owner.
///   * PR-Template enthaelt einen sichtbaren Moratorium-Check mit Verweis
///     auf den AGENTS.md-Abschnitt.
///   * Es entstehen KEINE neuen audit-moratorium.md / .claude/rules/
///     Dateien — die Wucherung, die der Plan verhindern soll.
/// </summary>
public sealed class Wave2AuditMoratoriumTests
{
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

    private static string ReadAgentsMd()
        => File.ReadAllText(Path.Combine(RepoRoot().FullName, "AGENTS.md"));

    private static string ReadPrTemplate()
        => File.ReadAllText(Path.Combine(RepoRoot().FullName, ".github", "PULL_REQUEST_TEMPLATE.md"));

    [Fact]
    public void AgentsMd_DeclaresMoratoriumWithRequiredFields()
    {
        var src = ReadAgentsMd();
        Assert.Contains("## Audit-Moratorium", src, System.StringComparison.Ordinal);
        // Geltungszeitraum: urspruenglich befristet bis 2026-05-28, mit Solo-Mode (2026-05-04)
        // dauerhaft fortgesetzt. Beide Marker muessen sichtbar bleiben.
        Assert.Contains("2026-05-28", src, System.StringComparison.Ordinal);
        Assert.Contains("2026-05-04", src, System.StringComparison.Ordinal);
        // Scope-Stichworte
        Assert.Contains("Verboten", src, System.StringComparison.Ordinal);
        Assert.Contains("Erlaubt", src, System.StringComparison.Ordinal);
        // Sanktion
        Assert.Contains("Sanktion", src, System.StringComparison.Ordinal);
        // Owner
        Assert.Contains("Owner", src, System.StringComparison.Ordinal);
        Assert.Contains("daftpunk6161", src, System.StringComparison.Ordinal);
        // Bezug zum Plan
        Assert.Contains("strategic-reduction-2026", src, System.StringComparison.Ordinal);
    }

    [Fact]
    public void PrTemplate_ExistsAndReferencesMoratoriumSection()
    {
        var src = ReadPrTemplate();
        Assert.Contains("Audit-Moratorium", src, System.StringComparison.Ordinal);
        // Verweis auf AGENTS.md (Single Source)
        Assert.Contains("AGENTS.md", src, System.StringComparison.Ordinal);
        // Mindestens eine Checkbox zur Selbstdeklaration
        Assert.Contains("- [ ]", src, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NoParallelMoratoriumDocumentExists()
    {
        var root = RepoRoot().FullName;
        // Wucherungs-Schutz: keine eigene audit-moratorium.md im docs- oder
        // .claude/rules/-Bereich. Single source of truth ist AGENTS.md.
        var forbidden = new[]
        {
            Path.Combine(root, "docs", "audit-moratorium.md"),
            Path.Combine(root, "docs", "AUDIT-MORATORIUM.md"),
            Path.Combine(root, ".claude", "rules", "audit-moratorium.instructions.md"),
            Path.Combine(root, ".claude", "rules", "moratorium.instructions.md"),
            Path.Combine(root, "docs", "plan", "strategic-reduction-2026", "audit-moratorium.md"),
        };
        foreach (var path in forbidden)
        {
            Assert.False(File.Exists(path),
                $"Verbotene parallele Moratorium-Datei existiert: {path}. Single source ist AGENTS.md.");
        }
    }

    [Fact]
    public void AgentsMd_MoratoriumSectionListsConcreteForbiddenAndAllowedItems()
    {
        var src = ReadAgentsMd();
        // Verbots-Liste muss konkret sein — sonst ist es nur Lippenbekenntnis.
        Assert.Contains("Deep-Dive", src, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Findings-Tracker", src, System.StringComparison.OrdinalIgnoreCase);
        // Erlaubnis-Liste muss P1-Sicherheit/Datenintegritaet als Ausnahme nennen.
        Assert.Contains("P1", src, System.StringComparison.Ordinal);
        // Pin/Regression-Tests bleiben erlaubt
        Assert.Contains("Pin-Test", src, System.StringComparison.Ordinal);
    }
}
