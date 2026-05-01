using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W4-REVIEW-INBOX (wave 5) — Red/Green pin tests.
///
/// Pflichtinvarianten:
///   * Drei Lanes: Safe / Review / Blocked.
///     Safe = Sort + DatVerified, Review = Review + Unknown, Blocked = Blocked.
///   * Projektion ist deterministisch (gleicher Input -> gleiche Lanes).
///   * Bulk-Aktionen mit destruktivem Effekt (Quarantine) muessen den
///     IDialogService.DangerConfirm-Token-Gate passieren.
///   * Decision-Explainer-Sprung pro Zeile via Callback (kein direktes Wissen
///     ueber DecisionExplainerProjection im VM).
/// </summary>
public class Wave4ReviewInboxTests
{
    private static RomCandidate Candidate(string file, SortDecision decision, string console = "NES")
        => new()
        {
            MainPath = Path.Combine("C:", "roms", file),
            ConsoleKey = console,
            SortDecision = decision,
            ClassificationReasonCode = decision.ToString(),
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Probable,
                Reasoning = $"reason-{file}"
            }
        };

    private static IReadOnlyList<RomCandidate> Sample() => new[]
    {
        Candidate("a.nes", SortDecision.Sort),
        Candidate("b.nes", SortDecision.DatVerified),
        Candidate("c.nes", SortDecision.Review),
        Candidate("d.nes", SortDecision.Unknown),
        Candidate("e.nes", SortDecision.Blocked),
    };

    [Fact]
    public void Project_ProducesThreeLanes_SafeReviewBlocked()
    {
        var lanes = ReviewInboxProjection.Project(Sample());

        Assert.Equal(2, lanes.Safe.Count);
        Assert.Contains(lanes.Safe, x => x.FileName == "a.nes");
        Assert.Contains(lanes.Safe, x => x.FileName == "b.nes");

        Assert.Equal(2, lanes.Review.Count);
        Assert.Contains(lanes.Review, x => x.FileName == "c.nes");
        Assert.Contains(lanes.Review, x => x.FileName == "d.nes");

        Assert.Single(lanes.Blocked);
        Assert.Equal("e.nes", lanes.Blocked[0].FileName);
    }

    [Fact]
    public void Project_IsDeterministic_SameInputSameLanes()
    {
        var a = ReviewInboxProjection.Project(Sample());
        var b = ReviewInboxProjection.Project(Sample());

        Assert.Equal(
            a.Safe.Select(x => x.FileName).ToArray(),
            b.Safe.Select(x => x.FileName).ToArray());
        Assert.Equal(
            a.Review.Select(x => x.FileName).ToArray(),
            b.Review.Select(x => x.FileName).ToArray());
        Assert.Equal(
            a.Blocked.Select(x => x.FileName).ToArray(),
            b.Blocked.Select(x => x.FileName).ToArray());
    }

    [Fact]
    public void BulkQuarantine_RequiresDangerConfirmToken_BeforeInvokingCallback()
    {
        var dialog = new StubDialogService { DangerConfirmReturn = false };
        var quarantined = new List<string>();
        var vm = new ReviewInboxViewModel(
            dialog,
            new StubLocalizationService(),
            quarantineCallback: items => quarantined.AddRange(items.Select(i => i.FileName)),
            openExplainerCallback: null);

        vm.Load(Sample());
        // mark all review items
        foreach (var r in vm.Review) r.IsSelected = true;
        vm.QuarantineSelectedReviewCommand.Execute(null);

        Assert.Empty(quarantined);
        Assert.Equal(1, dialog.DangerConfirmCallCount);

        dialog.DangerConfirmReturn = true;
        vm.QuarantineSelectedReviewCommand.Execute(null);
        Assert.Equal(2, quarantined.Count);
        Assert.Contains("c.nes", quarantined);
        Assert.Contains("d.nes", quarantined);
    }

    [Fact]
    public void OpenExplainer_PerRow_InvokesCallbackWithIdentifiers()
    {
        var opened = new List<(string Console, string File)>();
        var vm = new ReviewInboxViewModel(
            new StubDialogService(),
            new StubLocalizationService(),
            quarantineCallback: null,
            openExplainerCallback: row => opened.Add((row.ConsoleKey, row.FileName)));

        vm.Load(Sample());
        vm.OpenExplainerCommand.Execute(vm.Review[0]);

        Assert.Single(opened);
        Assert.Equal("NES", opened[0].Console);
        Assert.Equal("c.nes", opened[0].File);
    }

    [Fact]
    public void OpenExplainerCommand_DisabledWhenNoCallback()
    {
        var vm = new ReviewInboxViewModel(
            new StubDialogService(),
            new StubLocalizationService(),
            quarantineCallback: null,
            openExplainerCallback: null);

        vm.Load(Sample());
        Assert.False(vm.OpenExplainerCommand.CanExecute(vm.Review[0]));
    }

    [Fact]
    public void QuarantineCommand_DisabledWhenNoCallback()
    {
        var vm = new ReviewInboxViewModel(
            new StubDialogService(),
            new StubLocalizationService(),
            quarantineCallback: null,
            openExplainerCallback: null);

        vm.Load(Sample());
        foreach (var r in vm.Review) r.IsSelected = true;
        Assert.False(vm.QuarantineSelectedReviewCommand.CanExecute(null));
    }

    [Fact]
    public void Projection_DoesNotDuplicateScoringOrSafetyLogic()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Romulus.UI.Wpf", "Services", "ReviewInboxProjection.cs"));
        var codeOnly = System.Text.RegularExpressions.Regex.Replace(
            src, @"^\s*///.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        // Must not re-implement scoring or DAT logic; should re-use SafetyLaneProjection
        // (Safe lane = approved-to-sort, Review/Blocked re-uses canonical routing).
        Assert.DoesNotContain("FormatScore", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("VersionScore", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectWinner", codeOnly, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Test stubs
    // ------------------------------------------------------------------

    private sealed class StubDialogService : IDialogService
    {
        public bool DangerConfirmReturn { get; set; }
        public int DangerConfirmCallCount { get; private set; }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "")
        {
            DangerConfirmCallCount++;
            return DangerConfirmReturn;
        }
        public string? BrowseFolder(string title = "") => null;
        public string? BrowseFile(string title = "", string filter = "") => null;
        public string? SaveFile(string title = "", string filter = "", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "") => false;
        public void Info(string message, string title = "") { }
        public void Error(string message, string title = "") { }
        public ConfirmResult YesNoCancel(string message, string title = "") => ConfirmResult.Cancel;
        public string ShowInputBox(string prompt, string title = "", string defaultValue = "") => "";
        public void ShowText(string title, string content) { }
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => false;
        public bool ConfirmDatRenamePreview(IReadOnlyList<Romulus.Contracts.Models.DatAuditEntry> renameProposals) => false;
    }

    private sealed class StubLocalizationService : ILocalizationService
    {
        public string this[string key]
        {
            get => key;
            set { /* WPF TwoWay no-op */ }
        }
        public string CurrentLocale => "de";
        public IReadOnlyList<string> AvailableLocales => new[] { "de" };
        public string Format(string key, params object[] args) => string.Format(this[key], args);
        public void SetLocale(string locale) { }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }
}
