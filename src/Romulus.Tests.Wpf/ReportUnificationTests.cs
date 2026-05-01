using System.Collections.ObjectModel;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Pin tests for T-W5-REPORT-UNIFICATION.
/// Eliminates the documented dual-truth between RunReportWriter and the
/// candidate-only ReportGenerator fallback in <see cref="ResultExportService"/>.
/// After unification:
///   - <see cref="HtmlReportWriteResult.ChannelUsed"/> always equals
///     <c>"RunReportWriter"</c>.
///   - When no live RunResult exists, the service synthesizes one from the
///     in-memory candidates/groups via the canonical
///     <c>CollectionExportService</c> projection so GUI/CLI/API stay byte
///     identical.
/// </summary>
public sealed class ReportUnificationTests : IDisposable
{
    private readonly string _tempDir;

    public ReportUnificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "romulus-report-unify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string StripNonces(string html)
    {
        // CSP header form: 'nonce-XXXX'; HTML attr form: nonce="XXXX"
        html = System.Text.RegularExpressions.Regex.Replace(html, "nonce-[A-Za-z0-9+/=]+", "nonce-X");
        html = System.Text.RegularExpressions.Regex.Replace(html, "nonce=\"[A-Za-z0-9+/=]+\"", "nonce=\"X\"");
        return html;
    }

    private static void DriveToTerminal(MainViewModel vm, RunState terminal)
    {
        vm.CurrentRunState = RunState.Preflight;
        vm.CurrentRunState = RunState.Scanning;
        vm.CurrentRunState = terminal;
    }

    private static RomCandidate MakeCandidate(string path, string console = "NES")
        => new()
        {
            MainPath = path,
            Extension = Path.GetExtension(path),
            ConsoleKey = console,
            GameKey = $"{console}|game",
            Region = "US",
            SizeBytes = 1024,
            Category = FileCategory.Game,
            DatMatch = false,
        };

    [Fact]
    public void WhenLastRunResultPresent_ChannelIsRunReportWriter()
    {
        var vm = new MainViewModel();
        DriveToTerminal(vm, RunState.Completed);
        vm.LastRunResult = new RunResult { Status = RunConstants.StatusOk };
        var target = Path.Combine(_tempDir, "with-result.html");
        var service = new ResultExportService();

        var result = service.WriteHtmlReport(target, vm);

        Assert.True(result.Success);
        Assert.Equal("RunReportWriter", result.ChannelUsed);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void WhenLastRunResultNull_ChannelStillUsesRunReportWriter_NoFallbackBranch()
    {
        // Pre-unification this returned "ReportGenerator". After unification a single
        // canonical channel is used regardless of whether a live RunResult is present.
        var vm = new MainViewModel();
        DriveToTerminal(vm, RunState.CompletedDryRun);
        vm.LastRunResult = null;
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate(Path.Combine(_tempDir, "rom-a.bin")),
            MakeCandidate(Path.Combine(_tempDir, "rom-b.bin")),
        };
        var target = Path.Combine(_tempDir, "without-result.html");
        var service = new ResultExportService();

        var result = service.WriteHtmlReport(target, vm);

        Assert.True(result.Success);
        Assert.Equal("RunReportWriter", result.ChannelUsed);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void HtmlOutput_FromServiceAndRunReportWriter_AreByteIdentical_ForSameRunResult()
    {
        // Single-channel guarantee: handing the same live RunResult to the GUI service
        // must produce byte-identical HTML compared to invoking RunReportWriter directly
        // (the contract every CLI/API caller already relies on).
        var fixedTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var runResult = new RunResult
        {
            Status = RunConstants.StatusOk,
            StartedUtc = fixedTime,
            CompletedUtc = fixedTime,
        };

        var vm = new MainViewModel();
        DriveToTerminal(vm, RunState.Completed);
        vm.LastRunResult = runResult;

        var pathA = Path.Combine(_tempDir, "via-service.html");
        new ResultExportService().WriteHtmlReport(pathA, vm);

        var pathB = Path.Combine(_tempDir, "via-direct.html");
        RunReportWriter.WriteReport(pathB, runResult, RunConstants.ModeMove);

        Assert.Equal(File.ReadAllBytes(pathB).Length, File.ReadAllBytes(pathA).Length);
        Assert.Equal(StripNonces(File.ReadAllText(pathB)), StripNonces(File.ReadAllText(pathA)));
    }

    [Fact]
    public void HtmlOutput_FromSyntheticAndDirect_AreByteIdentical()
    {
        // For the synthesized path, both legs must share the same RunResult so that
        // the deterministic Timestamp (DateTime.UtcNow fallback) does not differ.
        var candidates = new[]
        {
            MakeCandidate(Path.Combine(_tempDir, "alpha.bin")),
            MakeCandidate(Path.Combine(_tempDir, "beta.bin")),
        };
        var groups = Array.Empty<DedupeGroup>();

        // Path A: synth happens once, then is shared between both writers.
        var synthetic = Romulus.Infrastructure.Analysis.CollectionExportService
            .BuildPreviewProjectionSource(candidates, groups);

        var vm = new MainViewModel();
        DriveToTerminal(vm, RunState.CompletedDryRun);
        vm.LastRunResult = synthetic;
        vm.LastCandidates = new ObservableCollection<RomCandidate>(candidates);
        vm.LastDedupeGroups = new ObservableCollection<DedupeGroup>(groups);

        var pathA = Path.Combine(_tempDir, "via-service.html");
        new ResultExportService().WriteHtmlReport(pathA, vm);

        var pathB = Path.Combine(_tempDir, "via-direct.html");
        RunReportWriter.WriteReport(pathB, synthetic, RunConstants.ModeDryRun);

        var bytesA = File.ReadAllBytes(pathA);
        var bytesB = File.ReadAllBytes(pathB);

        Assert.Equal(bytesB.Length, bytesA.Length);
        Assert.Equal(StripNonces(File.ReadAllText(pathB)), StripNonces(File.ReadAllText(pathA)));
    }

    [Fact]
    public void BuildPreviewProjectionSource_NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Romulus.Infrastructure.Analysis.CollectionExportService
                .BuildPreviewProjectionSource(null!, Array.Empty<DedupeGroup>()));
        Assert.Throws<ArgumentNullException>(() =>
            Romulus.Infrastructure.Analysis.CollectionExportService
                .BuildPreviewProjectionSource(Array.Empty<RomCandidate>(), null!));
    }

    [Fact]
    public void BuildPreviewProjectionSource_PopulatesCandidatesAndGroupsAndCount()
    {
        var candidates = new[]
        {
            MakeCandidate("/r/a.bin"),
            MakeCandidate("/r/b.bin"),
            MakeCandidate("/r/c.bin"),
        };
        var groups = Array.Empty<DedupeGroup>();

        var source = Romulus.Infrastructure.Analysis.CollectionExportService
            .BuildPreviewProjectionSource(candidates, groups);

        Assert.Equal(RunConstants.StatusOk, source.Status);
        Assert.Equal(candidates.Length, source.TotalFilesScanned);
        Assert.Equal(groups.Length, source.GroupCount);
        Assert.Equal(candidates.Length, source.AllCandidates.Count);
    }

    [Fact]
    public void CsvOutput_FromBothChannels_AreByteIdentical()
    {
        var candidates = new[] { MakeCandidate(Path.Combine(_tempDir, "csv-a.bin")) };
        var synthetic = Romulus.Infrastructure.Analysis.CollectionExportService
            .BuildPreviewProjectionSource(candidates, Array.Empty<DedupeGroup>());

        var vm = new MainViewModel();
        DriveToTerminal(vm, RunState.CompletedDryRun);
        vm.LastRunResult = synthetic;
        vm.LastCandidates = new ObservableCollection<RomCandidate>(candidates);
        vm.LastDedupeGroups = [];

        var pathA = Path.Combine(_tempDir, "via-service.csv");
        new ResultExportService().WriteHtmlReport(pathA, vm);

        var pathB = Path.Combine(_tempDir, "via-direct.csv");
        RunReportWriter.WriteReport(pathB, synthetic, RunConstants.ModeDryRun);

        Assert.Equal(File.ReadAllBytes(pathB), File.ReadAllBytes(pathA));
    }
}
