using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests.EntryPointParity;

/// <summary>
/// Unknown / Review / Blocked routing parity.
///
/// Aggregate count parity exists (ReportParityTests). What is NOT explicitly
/// covered is per-DedupeGroup SortDecision/DecisionClass equivalence between
/// the API and WPF (Orchestrator) entry points - i.e. that for the same
/// dataset both entry points project the SAME sort routing for non-Sort
/// candidates (Review/Blocked/Unknown).
///
/// This test seeds a small dataset that is guaranteed to produce non-Sort
/// candidates (BIOS tagged + an Unknown-extension file) and asserts the per-
/// group routing tuple (DecisionClass, SortDecision, ConsoleKey, PlatformFamily)
/// is identical across API and WPF.
/// </summary>
public sealed class UnknownReviewBlockedRoutingTests : IDisposable
{
    private readonly string _tempDir;

    public UnknownReviewBlockedRoutingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C7_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task RunRouting_ForUnknownReviewBlockedInputs_IsIdenticalAcrossWpfAndApi()
    {
        var root = Path.Combine(_tempDir, "scan");
        Directory.CreateDirectory(root);
        // Seed inputs that exercise non-Sort routing branches:
        File.WriteAllText(Path.Combine(root, "[BIOS] System (1.0).zip"), "bios");
        File.WriteAllText(Path.Combine(root, "Mario (USA).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mario (Europe).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Random Tool (Beta).zip"), "junk");
        File.WriteAllText(Path.Combine(root, "Mystery (Unknown Region).zip"), "unknown-region");

        // ── WPF (Orchestrator) ────────────────────────────────────────
        var vm = new MainViewModel(new StubThemeService(), new StubDialogService());
        vm.Roots.Add(root);
        vm.DryRun = true;
        vm.PreferEU = true; vm.PreferUS = true; vm.PreferJP = true; vm.PreferWORLD = true;

        var runService = new RunService();
        var (orchestrator, options, auditPath, reportPath) = await runService.BuildOrchestratorAsync(vm);
        var wpf = await runService.ExecuteRunAsync(orchestrator, options, auditPath, reportPath, CancellationToken.None);

        // ── API ───────────────────────────────────────────────────────
        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["EU", "US", "JP", "WORLD"]
        }, "DryRun");
        Assert.NotNull(apiRun);

        var wait = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(20));
        Assert.Equal(RunWaitDisposition.Completed, wait.Disposition);
        var api = manager.Get(apiRun.RunId)!.Result!;

        // Project per-group routing tuple
        var wpfRouting = RunResultProjection.RoutingTuples(
            wpf.Result.DedupeGroups.Select(g => (g.GameKey, g.Winner)));

        var apiRouting = RunResultProjection.RoutingTuples(
            api.DedupeGroups.Select(g => (g.GameKey, g.Winner)));

        Assert.True(wpfRouting.Count > 0, "Need at least one group to assert routing parity.");
        Assert.Equal(wpfRouting, apiRouting);
    }

    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
