using System.Text.Json;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;
using Xunit.Sdk;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

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
        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"],
            ReportPath = cliReportPath
        };

        var (cliExitCode, cliStdout, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        using var cliJson = ParseCliSummaryJson(cliStdout, cliStderr);

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

        var waitResult = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(20));
        var apiCompleted = manager.Get(apiRun.RunId);

        Assert.Equal(0, cliExitCode);
        Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
        Assert.NotNull(apiCompleted);
        Assert.NotNull(apiCompleted!.Result);
        Assert.Equal("completed", apiCompleted.Status);

        Assert.Equal(wpfExecution.Result.TotalFilesScanned, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(wpfExecution.Result.GroupCount, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(wpfExecution.Result.WinnerCount, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(wpfExecution.Result.LoserCount, cliJson.RootElement.GetProperty("Dupes").GetInt32());

        Assert.Equal(wpfExecution.Result.TotalFilesScanned, apiCompleted.Result!.TotalFiles);
        Assert.Equal(wpfExecution.Result.GroupCount, apiCompleted.Result.Groups);
        Assert.Equal(wpfExecution.Result.WinnerCount, apiCompleted.Result.Winners);
        Assert.Equal(wpfExecution.Result.LoserCount, apiCompleted.Result.Losers);

        Assert.True(File.Exists(cliReportPath));
        Assert.Contains($"[Report] {Path.GetFullPath(cliReportPath)}", cliStderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Report requested but not written", cliStderr, StringComparison.OrdinalIgnoreCase);

        Assert.False(string.IsNullOrWhiteSpace(wpfExecution.ReportPath));
        Assert.True(File.Exists(wpfExecution.ReportPath));

        Assert.True(apiCompleted.Result.DurationMs >= 0);
    }

    [Fact]
    public async Task DryRun_RunProjection_Parity_AcrossCliApiAndWpfDashboard()
    {
        var root = Path.Combine(_tempDir, "projection_parity");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Proto (Beta).zip"), "junk");

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
        var projection = Romulus.Infrastructure.Orchestration.RunProjectionFactory.Create(wpfExecution.Result);

        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["EU", "US", "WORLD", "JP"]
        };
        var (cliExitCode, cliStdout, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        using var cliJson = ParseCliSummaryJson(cliStdout, cliStderr);

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["EU", "US", "WORLD", "JP"]
        }, "DryRun");

        Assert.NotNull(apiRun);
        var waitResult = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(20));
        var apiCompleted = manager.Get(apiRun.RunId);

        Assert.Equal(0, cliExitCode);
        Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
        Assert.NotNull(apiCompleted?.Result);

        // CLI parity vs central projection
        Assert.Equal(projection.TotalFiles, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(projection.Candidates, cliJson.RootElement.GetProperty("Candidates").GetInt32());
        Assert.Equal(projection.Groups, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(projection.Keep, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(projection.Dupes, cliJson.RootElement.GetProperty("Dupes").GetInt32());
        Assert.Equal(projection.Games, cliJson.RootElement.GetProperty("Games").GetInt32());
        Assert.Equal(projection.Junk, cliJson.RootElement.GetProperty("Junk").GetInt32());
        Assert.Equal(projection.Bios, cliJson.RootElement.GetProperty("Bios").GetInt32());
        Assert.Equal(projection.DatMatches, cliJson.RootElement.GetProperty("DatMatches").GetInt32());
        Assert.Equal(projection.HealthScore, cliJson.RootElement.GetProperty("HealthScore").GetInt32());

        // API parity vs central projection
        var api = apiCompleted!.Result!;
        Assert.Equal(projection.TotalFiles, api.TotalFiles);
        Assert.Equal(projection.Candidates, api.Candidates);
        Assert.Equal(projection.Groups, api.Groups);
        Assert.Equal(projection.Keep, api.Winners);
        Assert.Equal(projection.Dupes, api.Losers);
        Assert.Equal(projection.Games, api.Games);
        Assert.Equal(projection.Junk, api.Junk);
        Assert.Equal(projection.Bios, api.Bios);
        Assert.Equal(projection.DatMatches, api.DatMatches);
        Assert.Equal(projection.HealthScore, api.HealthScore);

        // WPF dashboard parity vs central projection
        vm.ApplyRunResult(wpfExecution.Result);
        Assert.StartsWith(projection.Keep.ToString(), vm.DashWinners, StringComparison.Ordinal);
        Assert.StartsWith(projection.Dupes.ToString(), vm.DashDupes, StringComparison.Ordinal);
        Assert.StartsWith(projection.Junk.ToString(), vm.DashJunk, StringComparison.Ordinal);
        Assert.StartsWith(projection.Games.ToString(), vm.DashGames, StringComparison.Ordinal);
        Assert.StartsWith(projection.DatMatches.ToString(), vm.DashDatHits, StringComparison.Ordinal);
        Assert.StartsWith($"{projection.HealthScore}%", vm.HealthScore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRun_ThreeWayEntryPointParity_UsesExactlySameCoreCounters_Issue9()
    {
        var root = Path.Combine(_tempDir, "explicit_parity");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Title (USA).zip"), "1");
        File.WriteAllText(Path.Combine(root, "Title (Europe).zip"), "2");
        File.WriteAllText(Path.Combine(root, "Title (Beta).zip"), "3");

        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["EU"]
        };
        var (cliExitCode, cliStdout, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        using var cliJson = ParseCliSummaryJson(cliStdout, cliStderr);

        var vm = CreateViewModel();
        vm.Roots.Add(root);
        vm.DryRun = true;
        vm.PreferEU = true;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;

        var runService = new RunService();
        var (orchestrator, options, auditPath, reportPath) = runService.BuildOrchestrator(vm);
        var wpfExecution = runService.ExecuteRun(orchestrator, options, auditPath, reportPath, CancellationToken.None);

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["EU"]
        }, "DryRun");

        Assert.NotNull(apiRun);
        var waitResult = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(20));
        var apiCompleted = manager.Get(apiRun.RunId);

        Assert.Equal(0, cliExitCode);
        Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
        Assert.NotNull(apiCompleted?.Result);

        var cliTotal = cliJson.RootElement.GetProperty("TotalFiles").GetInt32();
        var cliGroups = cliJson.RootElement.GetProperty("Groups").GetInt32();
        var cliKeep = cliJson.RootElement.GetProperty("Keep").GetInt32();
        var cliDupes = cliJson.RootElement.GetProperty("Dupes").GetInt32();

        Assert.Equal(cliTotal, wpfExecution.Result.TotalFilesScanned);
        Assert.Equal(cliGroups, wpfExecution.Result.GroupCount);
        Assert.Equal(cliKeep, wpfExecution.Result.WinnerCount);
        Assert.Equal(cliDupes, wpfExecution.Result.LoserCount);

        Assert.Equal(cliTotal, apiCompleted!.Result!.TotalFiles);
        Assert.Equal(cliGroups, apiCompleted.Result.Groups);
        Assert.Equal(cliKeep, apiCompleted.Result.Winners);
        Assert.Equal(cliDupes, apiCompleted.Result.Losers);

        var cliIdentity = BuildCliIdentity(cliJson.RootElement);
        var wpfIdentity = BuildWpfIdentity(wpfExecution.Result.DedupeGroups);
        var apiIdentity = BuildApiIdentity(apiCompleted.Result.DedupeGroups);

        AssertIdentityEqual("WPF vs CLI", wpfIdentity, cliIdentity);
        AssertIdentityEqual("WPF vs API", wpfIdentity, apiIdentity);
    }

    [Fact]
    public void CliRun_WhenReportPathIsInvalid_ReturnsPreflightExitCodeAndNoFakeReportPath()
    {
        var root = Path.Combine(_tempDir, "invalid-report-root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu");

        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"],
            ReportPath = Path.Combine(_tempDir, "bad\0report.html")
        };

        var (cliExitCode, _, cliStderr) = RunCliWithCapturedConsole(cliOptions);

        Assert.Equal(3, cliExitCode);
        Assert.Contains("reportPath is invalid", cliStderr, StringComparison.OrdinalIgnoreCase);
        // Ensure no "[Report] <path>" line was emitted by CLI (only progress messages are OK)
        Assert.DoesNotContain("[Report] " + cliOptions.ReportPath, cliStderr, StringComparison.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCliWithCapturedConsole(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            try
            {
                CliProgram.SetConsoleOverrides(stdout, stderr);
                var exitCode = CliProgram.RunForTests(options);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                CliProgram.SetConsoleOverrides(null, null);
            }
        }
    }

    private static JsonDocument ParseCliSummaryJson(string stdout, string? stderr = null)
    {
        var text = string.IsNullOrWhiteSpace(stdout) ? (stderr ?? string.Empty) : stdout;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return JsonDocument.Parse(text[start..(end + 1)]);

        return JsonDocument.Parse(text);
    }

    private static string[] BuildCliIdentity(JsonElement root)
    {
        var results = root.GetProperty("Results");
        var rows = new List<string>();

        foreach (var group in results.EnumerateArray())
        {
            var gameKey = (group.GetProperty("GameKey").GetString() ?? string.Empty).Trim().ToLowerInvariant();
            var winner = CanonicalPath(group.GetProperty("Winner").GetString() ?? string.Empty);
            var losers = group.GetProperty("Losers")
                .EnumerateArray()
                .Select(l => CanonicalPath(l.GetString() ?? string.Empty))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            rows.Add($"{gameKey}::{winner}::{string.Join("|", losers)}");
        }

        return rows.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] BuildWpfIdentity(IReadOnlyList<Romulus.Contracts.Models.DedupeGroup> groups)
    {
        return groups
            .Select(group =>
            {
                var winner = CanonicalPath(group.Winner.MainPath);
                var losers = group.Losers
                    .Select(l => CanonicalPath(l.MainPath))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

                return $"{group.GameKey.Trim().ToLowerInvariant()}::{winner}::{string.Join("|", losers)}";
            })
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildApiIdentity(IReadOnlyList<ApiDedupeGroup> groups)
    {
        return groups
            .Select(group =>
            {
                var winner = CanonicalPath(group.Winner.MainPath);
                var losers = group.Losers
                    .Select(l => CanonicalPath(l.MainPath))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

                return $"{group.GameKey.Trim().ToLowerInvariant()}::{winner}::{string.Join("|", losers)}";
            })
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CanonicalPath(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

    private static void AssertIdentityEqual(string label, string[] expected, string[] actual)
    {
        if (expected.Length != actual.Length)
            throw new XunitException($"{label}: length mismatch. Expected={expected.Length}, Actual={actual.Length}\nExpected:\n{string.Join("\n", expected)}\nActual:\n{string.Join("\n", actual)}");

        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                throw new XunitException($"{label}: mismatch at index {i}.\nExpected: {expected[i]}\nActual:   {actual[i]}\nExpectedLength={expected[i].Length}, ActualLength={actual[i].Length}");
            }
        }
    }

    private static JsonDocument ParseCliSummaryJson(string stdout)
    {
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        Assert.True(start >= 0 && end > start, $"CLI stdout did not contain a JSON object. Output: {stdout}");

        var jsonPayload = stdout[start..(end + 1)];
        return JsonDocument.Parse(jsonPayload);
    }

    private static MainViewModel CreateViewModel()
        => new(new StubThemeService(), new StubDialogService());

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
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
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
