using System;
using System.Collections.Generic;
using System.IO;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 4 — T-W4-AUDIT-VIEWER-UI pin tests.
/// Acceptance gates from plan.yaml:
///   * Read-only, kein Write-Pfad in der View.
///   * Rollback nur ueber bestaetigten Confirm-Token.
///   * Nutzung nur ueber IAuditViewerBackingService — keine Duplikation
///     von AuditCsvParser / AuditSigningService in der UI.
///   * Sidecar-Verifikation sichtbar (IsSidecarValid via Backing-Service).
/// </summary>
public sealed class Wave4AuditViewerUiTests
{
    private static AuditViewerViewModel BuildVm(
        out StubBackingService stub,
        out StubDialogService dialog,
        Func<string, bool>? rollback = null)
    {
        stub = new StubBackingService();
        dialog = new StubDialogService();
        return new AuditViewerViewModel(stub, dialog, new StubLocalizationService(), rollback);
    }

    [Fact]
    public void Refresh_LoadsRunsViaBackingService_NotByReParsingCsv()
    {
        var vm = BuildVm(out var stub, out _);
        vm.AuditRoot = stub.RootPath;
        vm.RefreshCommand.Execute(null);

        Assert.Equal(2, vm.Runs.Count);
        Assert.Equal(1, stub.ListRunsCallCount);
        Assert.Equal("a.csv", vm.Runs[0].FileName);
    }

    [Fact]
    public void SelectRun_LoadsRowsAndSidecarFromBackingService()
    {
        var vm = BuildVm(out var stub, out _);
        vm.AuditRoot = stub.RootPath;
        vm.RefreshCommand.Execute(null);

        vm.SelectedRun = vm.Runs[0];

        Assert.Equal(1, stub.ReadRunRowsCallCount);
        Assert.Equal(1, stub.ReadSidecarCallCount);
        Assert.Single(vm.SelectedRunRows);
        Assert.NotNull(vm.SelectedSidecar);
        Assert.True(vm.SelectedSidecar!.IsSignatureValid);
    }

    [Fact]
    public void RollbackCommand_RequiresDangerConfirmToken_BeforeInvokingCallback()
    {
        var rolledBack = new List<string>();
        var vm = BuildVm(out var stub, out var dialog, p => { rolledBack.Add(p); return true; });
        vm.AuditRoot = stub.RootPath;
        vm.RefreshCommand.Execute(null);
        vm.SelectedRun = vm.Runs[0];

        // 1) User abbricht den DangerConfirm-Dialog -> kein Rollback
        dialog.DangerConfirmReturn = false;
        vm.RollbackCommand.Execute(null);
        Assert.Empty(rolledBack);
        Assert.Equal(1, dialog.DangerConfirmCallCount);

        // 2) User bestaetigt -> Callback wird mit dem Pfad aufgerufen
        dialog.DangerConfirmReturn = true;
        vm.RollbackCommand.Execute(null);
        Assert.Single(rolledBack);
        Assert.Equal(stub.Runs[0].AuditCsvPath, rolledBack[0]);
    }

    [Fact]
    public void RollbackCommand_DisabledWhenNoRunSelected()
    {
        var vm = BuildVm(out var stub, out _, p => true);
        vm.AuditRoot = stub.RootPath;
        vm.RefreshCommand.Execute(null);

        Assert.False(vm.RollbackCommand.CanExecute(null));
        vm.SelectedRun = vm.Runs[0];
        Assert.True(vm.RollbackCommand.CanExecute(null));
    }

    [Fact]
    public void RollbackCommand_DisabledWhenNoCallbackProvided()
    {
        // No rollback callback => UI may show the button greyed out;
        // the danger dialog must NEVER fire without a callback.
        var vm = BuildVm(out var stub, out var dialog, rollback: null);
        vm.AuditRoot = stub.RootPath;
        vm.RefreshCommand.Execute(null);
        vm.SelectedRun = vm.Runs[0];

        Assert.False(vm.RollbackCommand.CanExecute(null));
        vm.RollbackCommand.Execute(null);
        Assert.Equal(0, dialog.DangerConfirmCallCount);
    }

    [Fact]
    public void ViewModel_DoesNotDuplicateAuditCsvOrSigningLogic()
    {
        // Source-pin: VM relies solely on IAuditViewerBackingService and never
        // touches AuditCsvParser/AuditCsvStore/AuditSigningService directly.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var vmSrc = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Romulus.UI.Wpf", "ViewModels", "AuditViewerViewModel.cs"));
        // Strip XML-doc comments so the prose mention of legacy types in the
        // <summary> block does not trigger the pin.
        var codeOnly = System.Text.RegularExpressions.Regex.Replace(
            vmSrc, @"^\s*///.*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.DoesNotContain("AuditCsvParser", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditCsvStore", codeOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditSigningService", codeOnly, StringComparison.Ordinal);
        Assert.Contains("IAuditViewerBackingService", codeOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void View_HasNoWritePathOrCsvParsingCode()
    {
        // Source-pin: The XAML view must stay read-only — no buttons
        // wired to write commands beyond RefreshCommand and RollbackCommand
        // (which itself goes through DangerConfirm + delegated callback).
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var xamlPath = Path.Combine(
            dir!.FullName, "src", "Romulus.UI.Wpf", "Views", "AuditViewerView.xaml");
        Assert.True(File.Exists(xamlPath), "AuditViewerView.xaml must exist");
        var xaml = File.ReadAllText(xamlPath);

        // Allowed write surfaces: RefreshCommand, RollbackCommand. Nothing else
        // may invoke a write/edit/delete command on the audit data.
        Assert.DoesNotContain("EditCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditCsvParser", xaml, StringComparison.Ordinal);

        // Pin: uses the backing-service-driven RefreshCommand
        Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    }

    // ── Stubs ───────────────────────────────────────────────────────────

    private sealed class StubBackingService : IAuditViewerBackingService
    {
        public string RootPath { get; } = "C:/audit";
        public IReadOnlyList<AuditRunSummary> Runs { get; } = new[]
        {
            new AuditRunSummary("C:/audit/a.csv", "a.csv", "run-a",
                DateTimeOffset.UtcNow, 100, 1, true, true),
            new AuditRunSummary("C:/audit/b.csv", "b.csv", "run-b",
                DateTimeOffset.UtcNow.AddMinutes(-5), 50, 1, false, false),
        };

        public int ListRunsCallCount { get; private set; }
        public int ReadRunRowsCallCount { get; private set; }
        public int ReadSidecarCallCount { get; private set; }

        public IReadOnlyList<AuditRunSummary> ListRuns(string auditRoot, AuditRunFilter? filter = null, AuditPage? page = null)
        {
            ListRunsCallCount++;
            return Runs;
        }

        public AuditRowPage ReadRunRows(string auditCsvPath, AuditRunFilter? filter = null, AuditPage? page = null)
        {
            ReadRunRowsCallCount++;
            var rows = new[]
            {
                new AuditRowView(1, "C:/roms", "old.rom", "new.rom", "Move", "Game", "deadbeef", "winner", "2026-04-30T08:00:00Z"),
            };
            return new AuditRowPage(rows, 1, 1, 0, 100);
        }

        public AuditSidecarInfo? ReadSidecar(string auditCsvPath)
        {
            ReadSidecarCallCount++;
            return new AuditSidecarInfo(
                auditCsvPath + ".meta.json",
                1,
                IsSignatureValid: true,
                new Dictionary<string, string> { ["runId"] = "run-a" });
        }
    }

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
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string this[string key] { get => key; set { _ = value; } }
        public string CurrentLocale => "de";
        public IReadOnlyList<string> AvailableLocales => new[] { "de" };
        public void SetLocale(string locale) { _ = locale; PropertyChanged?.Invoke(this, new("Item[]")); }
        public string Format(string key, params object[] args) => string.Format(key, args);
    }
}
