using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
using RomCleanup.Core.SetParsing;
using RomCleanup.Infrastructure.Hashing;
using System.Runtime.CompilerServices;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that enriches scanned files into RomCandidate objects.
/// </summary>
public sealed class EnrichmentPipelinePhase : IPipelinePhase<EnrichmentPhaseInput, List<RomCandidate>>
{
    public string Name => "Enrichment";

    public List<RomCandidate> Execute(EnrichmentPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        var candidates = new List<RomCandidate>(input.Files.Count);
        var folderConsoleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versionScorer = new VersionScorer();

        foreach (var file in input.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(MapToCandidate(file, input.ConsoleDetector, input.HashService, input.DatIndex, context, folderConsoleCache, versionScorer));
        }

        return candidates;
    }

    public async IAsyncEnumerable<RomCandidate> ExecuteStreamingAsync(
        EnrichmentPhaseStreamingInput input,
        PipelineContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var folderConsoleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versionScorer = new VersionScorer();

        await foreach (var file in input.Files.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return MapToCandidate(file, input.ConsoleDetector, input.HashService, input.DatIndex, context, folderConsoleCache, versionScorer);
        }
    }

    private static RomCandidate MapToCandidate(
        ScannedFileEntry file,
        ConsoleDetector? consoleDetector,
        FileHashService? hashService,
        DatIndex? datIndex,
        PipelineContext context,
        Dictionary<string, string> folderConsoleCache,
        VersionScorer versionScorer)
    {
        var filePath = file.Path;
        var root = file.Root;
        var ext = file.Extension;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var gameKey = GameKeyNormalizer.Normalize(fileName);
        var classification = FileClassifier.Analyze(fileName, context.Options.AggressiveJunk);
        var category = classification.Category;

        string consoleKey = "";
        if (consoleDetector is not null)
        {
            var folder = Path.GetDirectoryName(filePath) ?? "";
            if (!folderConsoleCache.TryGetValue(folder, out var cachedKey))
            {
                cachedKey = consoleDetector.Detect(filePath, root);
                folderConsoleCache[folder] = cachedKey;
            }

            consoleKey = cachedKey;
        }

        var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
        var regionScore = FormatScorer.GetRegionScore(regionTag, context.Options.PreferRegions);
        var fmtScore = FormatScorer.GetFormatScore(ext);
        var verScore = versionScorer.GetVersionScore(fileName);

        long sizeBytes = 0;
        if (File.Exists(filePath))
        {
            try
            {
                sizeBytes = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                context.OnProgress?.Invoke($"WARNING: Could not read file size for {filePath}: {ex.Message}");
            }
        }

        bool datMatch = false;
        if (datIndex is not null && hashService is not null)
        {
            if (sizeBytes > 50_000_000)
            {
                var sizeMb = sizeBytes / (1024.0 * 1024.0);
                context.OnProgress?.Invoke($"[Scan] Hash: {Path.GetFileName(filePath)} ({sizeMb:F0} MB)…");
            }

            var hash = hashService.GetHash(filePath, context.Options.HashType);
            if (hash is not null)
            {
                if (consoleKey is "UNKNOWN" or "")
                {
                    // Console unknown: try matching hash against ALL loaded DATs
                    var anyMatch = datIndex.LookupAny(hash);
                    if (anyMatch is not null)
                    {
                        datMatch = true;
                        consoleKey = anyMatch.Value.ConsoleKey;
                        context.OnProgress?.Invoke(
                            $"[DAT] Konsole via DAT erkannt: {Path.GetFileName(filePath)} → {consoleKey}");
                    }
                    else
                    {
                            var hashHint = hash.Length >= 12 ? hash[..12] : hash;
                        context.OnProgress?.Invoke(
                                $"[DAT] Kein Match fuer UNKNOWN-Konsole: {Path.GetFileName(filePath)} (hash={hashHint})");
                    }
                }
                else
                {
                    datMatch = datIndex.Lookup(consoleKey, hash) is not null;
                }
            }
        }

        var headerScore = FormatScorer.GetHeaderVariantScore(root, filePath);
        var sizeTieBreak = FormatScorer.GetSizeTieBreakScore(null, ext, sizeBytes);
        var setMembers = GetSetMembers(filePath, ext);
        var completeness = CompletenessScorer.Calculate(filePath, ext, setMembers, datMatch);

        return CandidateFactory.Create(
            normalizedPath: filePath,
            extension: ext,
            sizeBytes: sizeBytes,
            category: category,
            gameKey: gameKey,
            region: regionTag,
            regionScore: regionScore,
            formatScore: fmtScore,
            versionScore: verScore,
            headerScore: headerScore,
            completenessScore: completeness,
            sizeTieBreakScore: sizeTieBreak,
            datMatch: datMatch,
            consoleKey: consoleKey,
            classificationReasonCode: classification.ReasonCode,
            classificationConfidence: classification.Confidence);
    }

    private static IReadOnlyList<string> GetSetMembers(string filePath, string ext)
    {
        return ext switch
        {
            ".cue" => CueSetParser.GetRelatedFiles(filePath),
            ".gdi" => GdiSetParser.GetRelatedFiles(filePath),
            ".ccd" => CcdSetParser.GetRelatedFiles(filePath),
            ".m3u" => M3uPlaylistParser.GetRelatedFiles(filePath),
            _ => Array.Empty<string>()
        };
    }

}

public sealed record EnrichmentPhaseInput(
    IReadOnlyList<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    DatIndex? DatIndex);

public sealed record EnrichmentPhaseStreamingInput(
    IAsyncEnumerable<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    DatIndex? DatIndex);