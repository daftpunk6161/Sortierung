using System.Text.Json;
using RomCleanup.Api;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;
using CliProgram = RomCleanup.CLI.Program;

namespace RomCleanup.Tests;

public sealed class ReportParityTests : IDisposable
{
    private readonly string _tempDir;

    public ReportParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"report_parity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best effort cleanup for temp artifacts.
        }
    }

    [Fact]
    public async Task DryRun_ReportParity_AcrossCliWpfAndApi_UsesConsistentCountsAndRealReportPaths()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Other (Japan).zip"), "jp");

        var cliReportPath = Path.Combine(_tempDir, "cli-report.html");
        var cliOptions = new CliProgram.CliOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"],
            ReportPath = cliReportPath
        };

        var (cliExitCode, cliStdout, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        using var cliJson = JsonDocument.Parse(cliStdout);

        var vm = CreateViewModel();
        vm.Roots.Add(root);
        vm.DryRun = true;
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = true;

        var runService = new RunService();
        var (orchestrator, options, auditPath, reportPath) = runService.BuildOrchestrator(vm);
        var wpfExecution = runService.ExecuteRun(orchestrator, options, auditPath, reportPath, CancellationToken.None);

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"]
        }, "DryRun");

        Assert.NotNull(apiRun);

        var waitResult = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(5));
        var apiCompleted = manager.Get(apiRun.RunId);

        Assert.Equal(0, cliExitCode);
        Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
        Assert.NotNull(apiCompleted);
        Assert.NotNull(apiCompleted!.Result);
        Assert.Equal("completed", apiCompleted.Status);

        Assert.Equal(wpfExecution.Result.TotalFilesScanned, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(wpfExecution.Result.GroupCount, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(wpfExecution.Result.WinnerCount, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(wpfExecution.Result.LoserCount, cliJson.RootElement.GetProperty("Move").GetInt32());

        Assert.Equal(wpfExecution.Result.TotalFilesScanned, apiCompleted.Result!.TotalFiles);
        Assert.Equal(wpfExecution.Result.GroupCount, apiCompleted.Result.Groups);
        Assert.Equal(wpfExecution.Result.WinnerCount, apiCompleted.Result.Keep);
        Assert.Equal(wpfExecution.Result.LoserCount, apiCompleted.Result.Move);

        Assert.True(File.Exists(cliReportPath));
        Assert.Contains($"[Report] {Path.GetFullPath(cliReportPath)}", cliStderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Report requested but not written", cliStderr, StringComparison.OrdinalIgnoreCase);

        Assert.False(string.IsNullOrWhiteSpace(wpfExecution.ReportPath));
        Assert.True(File.Exists(wpfExecution.ReportPath));

        Assert.False(string.IsNullOrWhiteSpace(apiCompleted.Result.ReportPath));
        Assert.True(File.Exists(apiCompleted.Result.ReportPath));
    }

    [Fact]
    public void CliRun_WhenReportCreationFails_LogsWarningInsteadOfFakeReportPath()
    {
        var root = Path.Combine(_tempDir, "invalid-report-root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu");

        var cliOptions = new CliProgram.CliOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"],
            ReportPath = Path.Combine(_tempDir, "bad\0report.html")
        };

        var (cliExitCode, _, cliStderr) = RunCliWithCapturedConsole(cliOptions);

        Assert.Equal(0, cliExitCode);
        Assert.Contains("Report requested but not written", cliStderr, StringComparison.OrdinalIgnoreCase);
        // Ensure no "[Report] <path>" line was emitted by CLI (only progress messages are OK)
        Assert.DoesNotContain("[Report] " + cliOptions.ReportPath, cliStderr, StringComparison.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCliWithCapturedConsole(CliProgram.CliOptions options)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = CliProgram.RunForTests(options);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static MainViewModel CreateViewModel()
        => new(new StubThemeService(), new StubDialogService());

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
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
    }
}