using Romulus.Contracts.Models;
using Romulus.Tests.Benchmark.Models;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Tests.Benchmark;

internal static class BenchmarkEvaluationRunner
{
    private static readonly EnrichmentPipelinePhase EnrichmentPhase = new();
    private static readonly FileHashService HashService = new();
    private static readonly ArchiveHashService ArchiveHashService = new();

    public static BenchmarkSampleResult Evaluate(BenchmarkFixture fixture, GroundTruthEntry entry)
    {
        var samplePath = fixture.GetSamplePath(entry);
        if (!File.Exists(samplePath))
        {
            return new BenchmarkSampleResult(
                entry.Id,
                BenchmarkVerdict.Missed,
                entry.Expected.ConsoleKey,
                "UNKNOWN",
                0,
                false,
                $"Sample file missing: {samplePath}",
                ExpectedCategory: entry.Expected.Category,
                ActualCategory: "Unknown",
                ActualSortDecision: SortDecision.Blocked);
        }

        var detection = fixture.Detector.DetectWithConfidence(samplePath, fixture.SamplesRoot);
        var fileName = Path.GetFileNameWithoutExtension(samplePath);
        var extension = Path.GetExtension(samplePath);
        var sizeBytes = new FileInfo(samplePath).Length;
        var classification = FileClassifier.Analyze(fileName, extension, sizeBytes);
        var enrichmentCandidate = TryEvaluateEnrichmentCandidate(fixture, samplePath, extension);

        return MergeEnrichmentProjection(
            entry,
            detection,
            classification.Category.ToString(),
            enrichmentCandidate);
    }

    public static List<BenchmarkSampleResult> EvaluateSet(BenchmarkFixture fixture, string setFileName)
    {
        var entries = GroundTruthLoader.LoadSet(setFileName);
        return entries
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => Evaluate(fixture, entry))
            .ToList();
    }

    internal static BenchmarkSampleResult MergeEnrichmentProjection(
        GroundTruthEntry entry,
        ConsoleDetectionResult detection,
        string actualCategory,
        RomCandidate? enrichmentCandidate)
    {
        var effectiveDetection = detection;
        var effectiveCategory = actualCategory;

        if (enrichmentCandidate is not null)
        {
            effectiveDetection = new ConsoleDetectionResult(
                enrichmentCandidate.ConsoleKey,
                enrichmentCandidate.DetectionConfidence,
                detection.Hypotheses,
                enrichmentCandidate.DetectionConflict,
                detection.ConflictDetail,
                enrichmentCandidate.HasHardEvidence,
                enrichmentCandidate.IsSoftOnly,
                enrichmentCandidate.SortDecision,
                enrichmentCandidate.DecisionClass,
                enrichmentCandidate.MatchEvidence,
                enrichmentCandidate.DetectionConflictType);

            effectiveCategory = enrichmentCandidate.Category.ToString();
        }

        return GroundTruthComparator.Compare(entry, effectiveDetection, effectiveCategory);
    }

    private static RomCandidate? TryEvaluateEnrichmentCandidate(BenchmarkFixture fixture, string samplePath, string extension)
    {
        try
        {
            var metrics = new PhaseMetricsCollector();
            metrics.Initialize();

            var context = new PipelineContext
            {
                Options = new RunOptions
                {
                    Roots = [fixture.SamplesRoot],
                    Extensions = [extension],
                    Mode = "DryRun",
                    HashType = "SHA1"
                },
                FileSystem = new FileSystemAdapter(),
                AuditStore = new AuditCsvStore(),
                Metrics = metrics
            };

            var candidates = EnrichmentPhase.Execute(
                new EnrichmentPhaseInput(
                    [new ScannedFileEntry(fixture.SamplesRoot, samplePath, extension)],
                    fixture.Detector,
                    HashService,
                    ArchiveHashService,
                    DatIndex: null),
                context,
                CancellationToken.None);

            return candidates.Count == 1 ? candidates[0] : null;
        }
        catch
        {
            return null;
        }
    }
}
