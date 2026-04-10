using System.Reflection;
using System.Text.Json;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

public sealed class KpiChannelParityBacklogTests : IDisposable
{
    private readonly string _tempDir;

    public KpiChannelParityBacklogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"kpi_parity_{Guid.NewGuid():N}");
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
    public async Task OpenApi_ApiRunResultSchema_ContainsAllApiRunResultProperties_Issue9()
    {
        using var spec = JsonDocument.Parse(await OpenApiTestHelper.FetchOpenApiJsonAsync());
        var root = spec.RootElement;

        var properties = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ApiRunResult")
            .GetProperty("properties");

        var expectedJsonNames = typeof(ApiRunResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToArray();

        foreach (var jsonName in expectedJsonNames)
            Assert.True(properties.TryGetProperty(jsonName, out _), $"OpenAPI schema missing property '{jsonName}'.");
    }

    [Fact]
    public async Task DryRun_AllKpis_AreParityAligned_AcrossCliApiAndReportSummary_Issue9()
    {
        var root = Path.Combine(_tempDir, "kpis");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Demo (Beta).zip"), "beta");

        // WPF/Orchestrator baseline
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
        var projection = RunProjectionFactory.Create(wpfExecution.Result);
        var reportSummary = RunReportWriter.BuildSummary(wpfExecution.Result, "DryRun");

        // CLI JSON
        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "DryRun",
            PreferRegions = ["US", "EU", "JP", "WORLD"]
        };
        var (cliExitCode, cliStdout, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        using var cliJson = ParseCliSummaryJson(cliStdout, cliStderr);

        // API result
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
        Assert.NotNull(apiCompleted?.Result);

        // CLI parity (all KPI fields exposed by CLI summary)
        Assert.Equal(projection.TotalFiles, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(projection.Candidates, cliJson.RootElement.GetProperty("Candidates").GetInt32());
        Assert.Equal(projection.Groups, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(projection.Keep, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(projection.Keep, cliJson.RootElement.GetProperty("Winners").GetInt32());
        Assert.Equal(projection.Dupes, cliJson.RootElement.GetProperty("Dupes").GetInt32());
        Assert.Equal(projection.Dupes, cliJson.RootElement.GetProperty("Losers").GetInt32());
        Assert.Equal(projection.Games, cliJson.RootElement.GetProperty("Games").GetInt32());
        Assert.Equal(projection.Junk, cliJson.RootElement.GetProperty("Junk").GetInt32());
        Assert.Equal(projection.Bios, cliJson.RootElement.GetProperty("Bios").GetInt32());
        Assert.Equal(projection.DatMatches, cliJson.RootElement.GetProperty("DatMatches").GetInt32());
        Assert.Equal(projection.HealthScore, cliJson.RootElement.GetProperty("HealthScore").GetInt32());
        Assert.Equal(projection.ConvertedCount, cliJson.RootElement.GetProperty("ConvertedCount").GetInt32());
        Assert.Equal(projection.ConvertErrorCount, cliJson.RootElement.GetProperty("ConvertErrorCount").GetInt32());
        Assert.Equal(projection.ConvertSkippedCount, cliJson.RootElement.GetProperty("ConvertSkippedCount").GetInt32());
        Assert.Equal(projection.ConvertBlockedCount, cliJson.RootElement.GetProperty("ConvertBlockedCount").GetInt32());
        Assert.Equal(projection.ConvertReviewCount, cliJson.RootElement.GetProperty("ConvertReviewCount").GetInt32());
        Assert.Equal(projection.ConvertSavedBytes, cliJson.RootElement.GetProperty("ConvertSavedBytes").GetInt64());
        Assert.Equal(projection.JunkRemovedCount, cliJson.RootElement.GetProperty("JunkRemovedCount").GetInt32());
        Assert.Equal(projection.MoveCount, cliJson.RootElement.GetProperty("MoveCount").GetInt32());
        Assert.Equal(projection.SkipCount, cliJson.RootElement.GetProperty("SkipCount").GetInt32());
        Assert.Equal(projection.JunkFailCount, cliJson.RootElement.GetProperty("JunkFailCount").GetInt32());
        Assert.Equal(projection.ConsoleSortMoved, cliJson.RootElement.GetProperty("ConsoleSortMoved").GetInt32());
        Assert.Equal(projection.ConsoleSortFailed, cliJson.RootElement.GetProperty("ConsoleSortFailed").GetInt32());
        Assert.Equal(projection.FailCount, cliJson.RootElement.GetProperty("FailCount").GetInt32());
        Assert.Equal(projection.SavedBytes, cliJson.RootElement.GetProperty("SavedBytes").GetInt64());
        Assert.True(cliJson.RootElement.GetProperty("DurationMs").GetInt64() >= 0);

        // API parity
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
        Assert.Equal(projection.ConvertedCount, api.ConvertedCount);
        Assert.Equal(projection.ConvertErrorCount, api.ConvertErrorCount);
        Assert.Equal(projection.ConvertSkippedCount, api.ConvertSkippedCount);
        Assert.Equal(projection.ConvertBlockedCount, api.ConvertBlockedCount);
        Assert.Equal(projection.ConvertReviewCount, api.ConvertReviewCount);
        Assert.Equal(projection.ConvertSavedBytes, api.ConvertSavedBytes);
        Assert.Equal(projection.JunkRemovedCount, api.JunkRemovedCount);
        Assert.Equal(projection.MoveCount, api.MoveCount);
        Assert.Equal(projection.SkipCount, api.SkipCount);
        Assert.Equal(projection.JunkFailCount, api.JunkFailCount);
        Assert.Equal(projection.ConsoleSortMoved, api.ConsoleSortMoved);
        Assert.Equal(projection.ConsoleSortFailed, api.ConsoleSortFailed);
        Assert.Equal(projection.FailCount, api.FailCount);
        Assert.Equal(projection.SavedBytes, api.SavedBytes);
        Assert.True(api.DurationMs >= 0);

        // Report parity (raw KPI fields + established summary mappings)
        Assert.Equal(projection.TotalFiles, reportSummary.TotalFiles);
        Assert.Equal(projection.Candidates, reportSummary.Candidates);
        Assert.Equal(projection.Groups, reportSummary.GroupCount);
        Assert.Equal(projection.Keep, reportSummary.KeepCount);
        Assert.Equal(projection.Dupes, reportSummary.DupesCount);
        Assert.Equal(projection.Games, reportSummary.GamesCount);
        Assert.Equal(projection.Junk, reportSummary.JunkCount);
        Assert.Equal(projection.Bios, reportSummary.BiosCount);
        Assert.Equal(projection.DatMatches, reportSummary.DatMatches);
        Assert.Equal(projection.HealthScore, reportSummary.HealthScore);
        Assert.Equal(projection.ConvertedCount, reportSummary.ConvertedCount);
        Assert.Equal(projection.ConvertErrorCount, reportSummary.ConvertErrorCount);
        Assert.Equal(projection.ConvertSkippedCount, reportSummary.ConvertSkippedCount);
        Assert.Equal(projection.ConvertBlockedCount, reportSummary.ConvertBlockedCount);
        Assert.Equal(projection.ConvertReviewCount, reportSummary.ConvertReviewCount);
        Assert.Equal(projection.ConvertSavedBytes, reportSummary.ConvertSavedBytes);
        Assert.Equal(projection.JunkRemovedCount, reportSummary.JunkRemovedCount);
        Assert.Equal(projection.SkipCount, reportSummary.SkipCount);
        Assert.Equal(projection.JunkFailCount, reportSummary.JunkFailCount);
        Assert.Equal(projection.ConsoleSortMoved, reportSummary.ConsoleSortMoved);
        Assert.Equal(projection.ConsoleSortFailed, reportSummary.ConsoleSortFailed);
        Assert.Equal(projection.FailCount, reportSummary.FailCount);
        Assert.Equal(projection.SavedBytes, reportSummary.SavedBytes);
        Assert.True(reportSummary.Duration.TotalMilliseconds >= 0);

        // Existing report semantics are retained in DryRun.
        Assert.Equal(projection.Dupes, reportSummary.MoveCount);
        Assert.Equal(projection.ConvertSkippedCount + projection.SkipCount, reportSummary.SkippedCount);
        Assert.Equal(projection.FailCount + projection.JunkFailCount + projection.ConsoleSortFailed, reportSummary.ErrorCount);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCliWithCapturedConsole(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
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
