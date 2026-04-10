using System.IO;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class DatAuditViewModelTests
{
    [Fact]
    public void ExportCsvCommand_UsesInjectedDialogService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_DatAuditExport_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var exportPath = Path.Combine(tempDir, "dat-audit.csv");

        try
        {
            var dialog = new RecordingDialogService
            {
                SaveFileResult = exportPath
            };

            var vm = new DatAuditViewModel(dialog: dialog);
            vm.LoadResult(new DatAuditResult(
                Entries:
                [
                    new DatAuditEntry(
                        FilePath: @"C:\roms\game.nes",
                        Hash: "ABC123",
                        Status: DatAuditStatus.HaveWrongName,
                        DatGameName: "Game",
                        DatRomFileName: "Game (USA).nes",
                        ConsoleKey: "NES",
                        Confidence: 98)
                ],
                HaveCount: 0,
                HaveWrongNameCount: 1,
                MissCount: 0,
                UnknownCount: 0,
                AmbiguousCount: 0));

            vm.ExportCsvCommand.Execute(null);

            Assert.Equal(1, dialog.SaveFileCallCount);
            Assert.True(File.Exists(exportPath), "ExportCsvCommand must write the selected file.");

            var csv = File.ReadAllText(exportPath);
            Assert.Contains("FilePath,Hash,Status,DatGameName,DatRomFileName,ConsoleKey,Confidence", csv, StringComparison.Ordinal);
            Assert.Contains(@"C:\roms\game.nes", csv, StringComparison.Ordinal);
            Assert.Contains("HaveWrongName", csv, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExportCsvCommand_BlocksProtectedOutputPath()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
            return;

        var dialog = new RecordingDialogService
        {
            SaveFileResult = Path.Combine(windowsDir, "dat-audit.csv")
        };

        var vm = new DatAuditViewModel(dialog: dialog);
        vm.LoadResult(new DatAuditResult(
            Entries:
            [
                new DatAuditEntry(
                    FilePath: @"C:\roms\game.nes",
                    Hash: "ABC123",
                    Status: DatAuditStatus.Have,
                    DatGameName: "Game",
                    DatRomFileName: "Game.nes",
                    ConsoleKey: "NES",
                    Confidence: 100)
            ],
            HaveCount: 1,
            HaveWrongNameCount: 0,
            MissCount: 0,
            UnknownCount: 0,
            AmbiguousCount: 0));

        vm.ExportCsvCommand.Execute(null);

        Assert.Single(dialog.ErrorCalls);
        Assert.Contains("blockiert", dialog.ErrorCalls[0], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public string? SaveFileResult { get; init; }
        public int SaveFileCallCount { get; private set; }
        public List<string> ErrorCalls { get; } = [];

        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;

        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null)
        {
            SaveFileCallCount++;
            return SaveFileResult;
        }

        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") => ErrorCalls.Add(message);
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Cancel;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => false;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries) => false;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => false;
    }
}
