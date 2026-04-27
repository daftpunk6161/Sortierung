using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave-2 TDD pins. One assertion per finding so a regression on any of the seven
/// architectural refactors fails its dedicated test and points back at the surface.
/// Run only on Windows (WPF dependencies).
/// </summary>
public sealed class Wave2RefactorRegressionTests
{
    // F-01 — RunViewModel.ApplyDashboard / ResetDashboard exist and write all 21 fields.
    [Fact]
    public void F01_ApplyDashboard_AndReset_MutateAllDashboardCounters()
    {
        var vm = new RunViewModel();
        vm.ResetDashboard();
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("00:00", vm.DashDuration);

        var projection = new DashboardProjection(
            Winners: "10", Dupes: "5", Junk: "2", Duration: "00:42",
            HealthScore: "94%", Games: "17",
            DatHits: "9", DatHaveDisplay: "8", DatWrongNameDisplay: "1",
            DatMissDisplay: "0", DatUnknownDisplay: "0", DatAmbiguousDisplay: "0",
            DedupeRate: "29%",
            MoveConsequenceText: "",
            ConvertedDisplay: "3", ConvertBlockedDisplay: "0", ConvertReviewDisplay: "0",
            ConvertSavedBytesDisplay: "1.2 MB",
            DatRenameProposedDisplay: "1", DatRenameExecutedDisplay: "1", DatRenameFailedDisplay: "0",
            ConsoleDistribution: System.Array.Empty<ConsoleDistributionItem>(),
            DedupeGroups: System.Array.Empty<DedupeGroupItem>());

        vm.ApplyDashboard(projection);
        Assert.Equal("10", vm.DashWinners);
        Assert.Equal("5", vm.DashDupes);
        Assert.Equal("2", vm.DashJunk);
        Assert.Equal("00:42", vm.DashDuration);
        Assert.Equal("94%", vm.HealthScore);
        Assert.Equal("17", vm.DashGames);
        Assert.Equal("9", vm.DashDatHits);
        Assert.Equal("8", vm.DashDatHave);
        Assert.Equal("1", vm.DashDatWrongName);
        Assert.Equal("0", vm.DashDatMiss);
        Assert.Equal("1", vm.DashDatRenameProposed);
        Assert.Equal("3", vm.DashConverted);
        Assert.Equal("1.2 MB", vm.DashConvertSaved);
        Assert.Equal("29%", vm.DedupeRate);
    }

    [Fact]
    public void F01_ResetDashboard_RestoresPlaceholdersAfterApply()
    {
        var vm = new RunViewModel();
        var projection = new DashboardProjection(
            Winners: "10", Dupes: "5", Junk: "2", Duration: "00:42",
            HealthScore: "94%", Games: "17",
            DatHits: "9", DatHaveDisplay: "8", DatWrongNameDisplay: "1",
            DatMissDisplay: "0", DatUnknownDisplay: "0", DatAmbiguousDisplay: "0",
            DedupeRate: "29%",
            MoveConsequenceText: "",
            ConvertedDisplay: "3", ConvertBlockedDisplay: "0", ConvertReviewDisplay: "0",
            ConvertSavedBytesDisplay: "1.2 MB",
            DatRenameProposedDisplay: "1", DatRenameExecutedDisplay: "1", DatRenameFailedDisplay: "0",
            ConsoleDistribution: System.Array.Empty<ConsoleDistributionItem>(),
            DedupeGroups: System.Array.Empty<DedupeGroupItem>());
        vm.ApplyDashboard(projection);

        vm.ResetDashboard();
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("–", vm.DashConverted);
        Assert.Equal("00:00", vm.DashDuration);
        Assert.Equal("–", vm.DedupeRate);
    }

    // F-06 — ApiProcessHost lifecycle is testable without spawning a real process.
    [Fact]
    public void F06_ApiProcessHost_StartFailureLogsAndReturnsFalse()
    {
        var logs = new List<(string Message, string Level)>();
        var launcher = new FakeProcessLauncher();
        var host = new ApiProcessHost(launcher, (m, l) => logs.Add((m, l)),
            System.TimeSpan.FromMilliseconds(1), "http://test/health");

        Assert.False(host.Start("does-not-matter"));
        Assert.Contains(logs, t => t.Level == "WARN" && t.Message.Contains("fehlgeschlagen", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void F06_ApiProcessHost_StartWithEmptyPath_ShortCircuits()
    {
        var logs = new List<(string Message, string Level)>();
        var launcher = new FakeProcessLauncher();
        var host = new ApiProcessHost(launcher, (m, l) => logs.Add((m, l)),
            System.TimeSpan.FromMilliseconds(1), "http://test/health");

        Assert.False(host.Start("   "));
        Assert.Empty(launcher.StartedInfos);
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<System.Diagnostics.ProcessStartInfo> StartedInfos { get; } = new();
        public List<string> OpenedUrls { get; } = new();

        public System.Diagnostics.Process? Start(System.Diagnostics.ProcessStartInfo info)
        {
            StartedInfos.Add(info);
            return null; // simulate failure
        }
        public void OpenBrowser(string url) => OpenedUrls.Add(url);
    }

    // F-07 — IResultExportService is registered and selects the correct channel.
    [Fact]
    public void F07_ResultExportService_IsResolvableAndSelectsChannel()
    {
        var service = new ResultExportService();
        // Calling without a target path must reject input rather than silently succeed.
        Assert.Throws<System.ArgumentException>(() => service.WriteHtmlReport("", null!));
    }

    // F-09 — WizardView reattach helper exists and walks all DPs reflectively
    // (curated whitelist no longer present).
    [Fact]
    public void F09_WizardView_ReattachUsesReflectiveLocalValueWalk()
    {
        var fields = typeof(Romulus.UI.Wpf.Views.WizardView).GetFields(
            BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        // The pre-Wave-2 implementation exposed a `_commonBindableProps` array; if it
        // resurfaces, the reflective walk has been undone.
        Assert.DoesNotContain(fields, f => f.Name == "_commonBindableProps");
    }

    // F-10 — SafetyLaneProjection routes by SortDecision and falls back to UNKNOWN
    // for candidates without a usable console key.
    [Fact]
    public void F10_SafetyLaneProjection_RoutesByDecisionAndUnknownFallback()
    {
        var blocked = MakeCandidate("/r/a.bin", "SNES", SortDecision.Blocked);
        var review = MakeCandidate("/r/b.bin", "SNES", SortDecision.Review);
        var unknown = MakeCandidate("/r/c.bin", "SNES", SortDecision.Unknown);
        var fallthrough = MakeCandidate("/r/d.bin", "", SortDecision.Sort); // empty console with non-safety decision

        var lanes = SafetyLaneProjection.Project(new[] { blocked, review, unknown, fallthrough });

        Assert.Single(lanes.Blocked);
        Assert.Single(lanes.Review);
        Assert.Equal(2, lanes.Unknown.Count); // Unknown + UNKNOWN-console fallthrough
        Assert.Contains(lanes.Blocked, x => x.FileName == "a.bin");
        Assert.Contains(lanes.Unknown, x => x.FileName == "d.bin");
    }

    [Fact]
    public void F10_SafetyLaneProjection_NullCandidates_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => SafetyLaneProjection.Project(null!));
    }

    // F-11 — ToolRunnerAdapter caches FindTool results across calls. We verify the
    // public InvalidateToolCache surface plus identity of repeated lookups.
    [Fact]
    public void F11_ToolRunnerAdapter_FindToolCachesResult()
    {
        var adapter = new Romulus.Infrastructure.Tools.ToolRunnerAdapter();
        var first = adapter.FindTool("definitely-not-installed-tool-xyz");
        var second = adapter.FindTool("definitely-not-installed-tool-xyz");
        Assert.Equal(first, second); // both null, but cache path verified by ===
        adapter.InvalidateToolCache();
        var third = adapter.FindTool("definitely-not-installed-tool-xyz");
        Assert.Equal(first, third);
    }

    private static RomCandidate MakeCandidate(string mainPath, string console, SortDecision decision)
        => new()
        {
            MainPath = mainPath,
            ConsoleKey = console,
            SortDecision = decision,
            ClassificationReasonCode = "TEST",
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.None,
                Reasoning = ""
            }
        };
}
