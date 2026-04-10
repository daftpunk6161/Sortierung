using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class EnrichmentParallelizationTests : IDisposable
{
    private readonly string _tempDir;

    public EnrichmentParallelizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_EnrichmentParallel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Execute_PreservesInputOrder_WhenProcessedInParallel()
    {
        var root = Path.Combine(_tempDir, "ordered");
        Directory.CreateDirectory(root);

        var input = CreateScannedFiles(root, 8)
            .OrderByDescending(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(input, null, null, null, null),
            CreateContext(root),
            CancellationToken.None);

        Assert.Equal(input.Select(file => file.Path), result.Select(candidate => candidate.MainPath));
    }

    [Fact]
    public async Task ExecuteStreamingAsync_PreservesInputOrder_WhenProcessedInParallel()
    {
        var root = Path.Combine(_tempDir, "streaming");
        Directory.CreateDirectory(root);

        var input = CreateScannedFiles(root, 8)
            .OrderByDescending(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var phase = new EnrichmentPipelinePhase();
        var result = new List<RomCandidate>();

        await foreach (var candidate in phase.ExecuteStreamingAsync(
            new EnrichmentPhaseStreamingInput(ToAsync(input), null, null, null, null),
            CreateContext(root),
            CancellationToken.None))
        {
            result.Add(candidate);
        }

        Assert.Equal(input.Select(file => file.Path), result.Select(candidate => candidate.MainPath));
    }

    [Fact]
    public async Task Execute_And_ExecuteStreamingAsync_ProduceEquivalentCandidates()
    {
        var root = Path.Combine(_tempDir, "equivalent");
        Directory.CreateDirectory(root);

        var input = CreateScannedFiles(root, 8).ToArray();
        var phase = new EnrichmentPipelinePhase();
        var context = CreateContext(root);

        var syncResult = phase.Execute(
            new EnrichmentPhaseInput(input, null, null, null, null),
            context,
            CancellationToken.None);

        var streamingResult = new List<RomCandidate>();
        await foreach (var candidate in phase.ExecuteStreamingAsync(
            new EnrichmentPhaseStreamingInput(ToAsync(input), null, null, null, null),
            CreateContext(root),
            CancellationToken.None))
        {
            streamingResult.Add(candidate);
        }

        Assert.Equal(syncResult.Select(c => c.MainPath), streamingResult.Select(c => c.MainPath));
        Assert.Equal(syncResult.Select(c => c.GameKey), streamingResult.Select(c => c.GameKey));
        Assert.Equal(syncResult.Select(c => c.Category), streamingResult.Select(c => c.Category));
        Assert.Equal(syncResult.Select(c => c.SortDecision), streamingResult.Select(c => c.SortDecision));
    }

    private static async IAsyncEnumerable<ScannedFileEntry> ToAsync(IEnumerable<ScannedFileEntry> files)
    {
        foreach (var file in files)
        {
            await Task.Yield();
            yield return file;
        }
    }

    private static PipelineContext CreateContext(string root)
    {
        return new PipelineContext
        {
            Options = new RunOptions
            {
                Roots = [root],
                Extensions = [".zip"],
                Mode = "DryRun"
            },
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(new FileSystemAdapter()),
            Metrics = new PhaseMetricsCollector(),
            OnProgress = _ => { }
        };
    }

    private static IEnumerable<ScannedFileEntry> CreateScannedFiles(string root, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var filePath = Path.Combine(root, $"game_{i:D2}.zip");
            File.WriteAllText(filePath, $"content-{i}");
            yield return new ScannedFileEntry(root, filePath, ".zip");
        }
    }

}
