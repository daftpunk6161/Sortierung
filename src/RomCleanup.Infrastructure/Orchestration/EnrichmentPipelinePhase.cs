using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
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
            candidates.Add(MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex, input.HeaderlessHasher, context, folderConsoleCache, versionScorer));
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
            yield return MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex, input.HeaderlessHasher, context, folderConsoleCache, versionScorer);
        }
    }

    private static RomCandidate MapToCandidate(
        ScannedFileEntry file,
        ConsoleDetector? consoleDetector,
        FileHashService? hashService,
        ArchiveHashService? archiveHashService,
        DatIndex? datIndex,
        Contracts.Ports.IHeaderlessHasher? headerlessHasher,
        PipelineContext context,
        Dictionary<string, string> folderConsoleCache,
        VersionScorer versionScorer)
    {
        var filePath = file.Path;
        var root = file.Root;
        var ext = file.Extension;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var gameKey = GameKeyNormalizer.Normalize(fileName);

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

        var classification = FileClassifier.Analyze(fileName, ext, sizeBytes, context.Options.AggressiveJunk);
        var category = classification.Category;

        string consoleKey = "";
        int detectionConfidence = 0;
        bool detectionConflict = false;
        bool hasHardEvidence = false;
        bool isSoftOnly = true;
        var sortDecision = SortDecision.Blocked;
        if (consoleDetector is not null)
        {
            var result = consoleDetector.DetectWithConfidence(filePath, root);
            consoleKey = result.ConsoleKey;
            detectionConfidence = result.Confidence;
            detectionConflict = result.HasConflict;
            hasHardEvidence = result.HasHardEvidence;
            isSoftOnly = result.IsSoftOnly;
            sortDecision = result.SortDecision;
        }

        var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
        var regionScore = FormatScorer.GetRegionScore(regionTag, context.Options.PreferRegions);
        var fmtScore = FormatScorer.GetFormatScore(ext);
        var verScore = versionScorer.GetVersionScore(fileName);

        bool datMatch = false;
        string? computedHash = null;
        string? computedHeaderlessHash = null;
        if (datIndex is not null && hashService is not null)
        {
            if (sizeBytes > 50_000_000)
            {
                var sizeMb = sizeBytes / (1024.0 * 1024.0);
                context.OnProgress?.Invoke($"[Scan] Hash: {Path.GetFileName(filePath)} ({sizeMb:F0} MB)…");
            }

            var lowerExt = ext.ToLowerInvariant();
            var isArchive = lowerExt is ".zip" or ".7z";

            // For archives: try inner hashes first (DATs store ROM content hashes, not container hashes)
            if (isArchive && archiveHashService is not null)
            {
                var innerHashes = archiveHashService.GetArchiveHashes(filePath, context.Options.HashType);
                foreach (var innerHash in innerHashes)
                {
                    computedHash ??= innerHash;
                    if (consoleKey is "UNKNOWN" or "")
                    {
                        var anyMatch = datIndex.LookupAny(innerHash);
                        if (anyMatch is not null)
                        {
                            datMatch = true;
                            computedHash = innerHash;
                            var previousConsole = consoleKey;
                            consoleKey = anyMatch.Value.ConsoleKey;
                            if (!string.IsNullOrEmpty(previousConsole) &&
                                previousConsole != "UNKNOWN" &&
                                !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                            {
                                detectionConflict = true;
                            }
                            context.OnProgress?.Invoke(
                                $"[DAT] Konsole via DAT erkannt: {Path.GetFileName(filePath)} → {consoleKey}");
                            break;
                        }
                    }
                    else
                    {
                        if (datIndex.Lookup(consoleKey, innerHash) is not null)
                        {
                            datMatch = true;
                            computedHash = innerHash;
                            break;
                        }
                    }
                }
            }

            // For non-archives: try headerless hash first (No-Intro DATs hash without headers for NES/SNES/7800/Lynx)
            if (!datMatch && !isArchive && headerlessHasher is not null && consoleKey is not "UNKNOWN" and not "")
            {
                computedHeaderlessHash = headerlessHasher.ComputeHeaderlessHash(filePath, consoleKey, context.Options.HashType);
                if (computedHeaderlessHash is not null)
                {
                    datMatch = datIndex.Lookup(consoleKey, computedHeaderlessHash) is not null;
                }
            }

            // Fallback: try container hash (works for uncompressed ROMs, CHD, ISO, etc.)
            if (!datMatch)
            {
                var hash = hashService.GetHash(filePath, context.Options.HashType);
                computedHash ??= hash;
                if (hash is not null)
                {
                    if (consoleKey is "UNKNOWN" or "")
                    {
                        var anyMatch = datIndex.LookupAny(hash);
                        if (anyMatch is not null)
                        {
                            datMatch = true;
                            computedHash = hash;
                            var previousConsole = consoleKey;
                            consoleKey = anyMatch.Value.ConsoleKey;
                            if (!string.IsNullOrEmpty(previousConsole) &&
                                previousConsole != "UNKNOWN" &&
                                !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                            {
                                detectionConflict = true;
                            }
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
        }

        // DAT match is the ultimate authority — set max confidence and SortDecision
        if (datMatch && consoleKey is not "UNKNOWN" and not "")
        {
            detectionConfidence = detectionConflict
                ? Math.Max(detectionConfidence, 95)
                : 100;
            hasHardEvidence = true;
            isSoftOnly = false;
            sortDecision = detectionConflict
                ? SortDecision.Review
                : SortDecision.DatVerified;
        }

        var headerScore = FormatScorer.GetHeaderVariantScore(root, filePath);
        var sizeTieBreak = FormatScorer.GetSizeTieBreakScore(null, ext, sizeBytes);
        var setMembers = PipelinePhaseHelpers.GetSetMembers(filePath, ext, includeM3uMembers: true);
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
            hash: computedHash,
            headerlessHash: computedHeaderlessHash,
            classificationReasonCode: classification.ReasonCode,
            classificationConfidence: classification.Confidence,
            detectionConfidence: detectionConfidence,
            detectionConflict: detectionConflict,
            hasHardEvidence: hasHardEvidence,
            isSoftOnly: isSoftOnly,
            sortDecision: sortDecision);
    }

}

public sealed record EnrichmentPhaseInput(
    IReadOnlyList<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null);

public sealed record EnrichmentPhaseStreamingInput(
    IAsyncEnumerable<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null);