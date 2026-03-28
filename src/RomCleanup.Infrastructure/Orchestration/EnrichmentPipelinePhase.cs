using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Hashing;
using System.Runtime.CompilerServices;
using RomCleanup.Core.SetParsing;

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
            candidates.Add(MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex, input.HeaderlessHasher, input.KnownBiosHashes, context, folderConsoleCache, versionScorer));
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
            yield return MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex, input.HeaderlessHasher, input.KnownBiosHashes, context, folderConsoleCache, versionScorer);
        }
    }

    private static RomCandidate MapToCandidate(
        ScannedFileEntry file,
        ConsoleDetector? consoleDetector,
        FileHashService? hashService,
        ArchiveHashService? archiveHashService,
        DatIndex? datIndex,
        Contracts.Ports.IHeaderlessHasher? headerlessHasher,
        IReadOnlySet<string>? knownBiosHashes,
        PipelineContext context,
        Dictionary<string, string> folderConsoleCache,
        VersionScorer versionScorer)
    {
        var filePath = file.Path;
        var root = file.Root;
        var ext = file.Extension;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var gameKey = GameKeyNormalizationProfile.TagPatterns is { Count: > 0 }
            ? GameKeyNormalizer.Normalize(
                fileName,
                GameKeyNormalizationProfile.TagPatterns,
                GameKeyNormalizationProfile.AlwaysAliasMap)
            : GameKeyNormalizer.Normalize(fileName);

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
        MatchEvidence matchEvidence = new();
        ConsoleDetectionResult? detectionResult = null;
        if (consoleDetector is not null)
        {
            detectionResult = consoleDetector.DetectWithConfidence(filePath, root);
            consoleKey = detectionResult.ConsoleKey;
            detectionConfidence = detectionResult.Confidence;
            detectionConflict = detectionResult.HasConflict;
            hasHardEvidence = detectionResult.HasHardEvidence;
            isSoftOnly = detectionResult.IsSoftOnly;
            sortDecision = detectionResult.SortDecision;
            matchEvidence = detectionResult.MatchEvidence ?? new MatchEvidence();
        }

        var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
        var regionScore = FormatScorer.GetRegionScore(regionTag, context.Options.PreferRegions);
        var fmtScore = FormatScorer.GetFormatScore(ext);
        var verScore = versionScorer.GetVersionScore(fileName);

        // DAT lookup (3-stage: archive hash → headerless hash → container hash)
        var datResult = LookupDat(filePath, ext, sizeBytes, consoleKey, detectionConflict,
            datIndex, hashService, archiveHashService, headerlessHasher, detectionResult, context);
        consoleKey = datResult.ConsoleKey;
        detectionConflict = datResult.DetectionConflict;
        var computedHash = datResult.ComputedHash;
        var computedHeaderlessHash = datResult.ComputedHeaderlessHash;

        // BIOS classification (from DAT metadata and known BIOS hash catalog)
        ResolveBios(ref category, ref matchEvidence, ref computedHash,
            datResult.DatMatchedBios, knownBiosHashes, hashService, filePath,
            computedHeaderlessHash, context);

        // DAT authority — set max confidence and SortDecision
        if (datResult.DatMatch && consoleKey is not "UNKNOWN" and not "")
        {
            ApplyDatAuthority(ref detectionConfidence, ref hasHardEvidence, ref isSoftOnly,
                ref sortDecision, ref matchEvidence,
                detectionConflict, datResult.DatResolvedFromAmbiguousCandidates);
        }

        var headerScore = FormatScorer.GetHeaderVariantScore(root, filePath);
        var sizeTieBreak = FormatScorer.GetSizeTieBreakScore(null, ext, sizeBytes);
        var setMembers = PipelinePhaseHelpers.GetSetMembers(filePath, ext, includeM3uMembers: true);
        var missingSetMembersCount = ext switch
        {
            ".cue" => CueSetParser.GetMissingFiles(filePath).Count,
            ".gdi" => GdiSetParser.GetMissingFiles(filePath).Count,
            ".ccd" => CcdSetParser.GetMissingFiles(filePath).Count,
            ".m3u" => M3uPlaylistParser.GetMissingFiles(filePath).Count,
            _ => 0
        };
        var completeness = CompletenessScorer.Calculate(filePath, ext, setMembers, missingSetMembersCount, datResult.DatMatch);

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
            datMatch: datResult.DatMatch,
            consoleKey: consoleKey,
            hash: computedHash,
            headerlessHash: computedHeaderlessHash,
            classificationReasonCode: classification.ReasonCode,
            classificationConfidence: classification.Confidence,
            detectionConfidence: detectionConfidence,
            detectionConflict: detectionConflict,
            hasHardEvidence: hasHardEvidence,
            isSoftOnly: isSoftOnly,
                sortDecision: sortDecision,
                matchEvidence: matchEvidence);
    }

    private readonly record struct DatLookupResult(
        bool DatMatch,
        bool DatMatchedBios,
        bool DatResolvedFromAmbiguousCandidates,
        string? ComputedHash,
        string? ComputedHeaderlessHash,
        string ConsoleKey,
        bool DetectionConflict);

    private static DatLookupResult LookupDat(
        string filePath, string ext, long sizeBytes,
        string consoleKey, bool detectionConflict,
        DatIndex? datIndex, FileHashService? hashService,
        ArchiveHashService? archiveHashService,
        Contracts.Ports.IHeaderlessHasher? headerlessHasher,
        ConsoleDetectionResult? detectionResult,
        PipelineContext context)
    {
        bool datMatch = false;
        bool datMatchedBios = false;
        bool datResolvedFromAmbiguousCandidates = false;
        string? computedHash = null;
        string? computedHeaderlessHash = null;

        if (datIndex is null || hashService is null)
            return new DatLookupResult(false, false, false, null, null, consoleKey, detectionConflict);

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
                if (consoleKey is "UNKNOWN" or "" or "AMBIGUOUS")
                {
                    var unknownResolution = ResolveUnknownDatMatch(datIndex, innerHash, detectionResult);
                    if (unknownResolution.IsMatch)
                    {
                        datMatch = true;
                        computedHash = innerHash;
                        datMatchedBios = unknownResolution.IsBios;
                        datResolvedFromAmbiguousCandidates = unknownResolution.ResolvedFromAmbiguousCandidates;
                        var previousConsole = consoleKey;
                        consoleKey = unknownResolution.ConsoleKey!;
                        if (!string.IsNullOrEmpty(previousConsole) &&
                            previousConsole != "UNKNOWN" &&
                            !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                        {
                            detectionConflict = true;
                        }
                        if (unknownResolution.ResolvedFromAmbiguousCandidates)
                        {
                            context.OnProgress?.Invoke(
                                $"[DAT] Mehrdeutigen Hash via Hypothesen aufgeloest: {Path.GetFileName(filePath)} → {consoleKey}");
                        }
                        else
                        {
                            context.OnProgress?.Invoke(
                                $"[DAT] Konsole via DAT erkannt: {Path.GetFileName(filePath)} → {consoleKey}");
                        }
                        break;
                    }
                }
                else
                {
                    var byConsole = datIndex.LookupWithFilename(consoleKey, innerHash);
                    if (byConsole is not null)
                    {
                        datMatch = true;
                        computedHash = innerHash;
                        datMatchedBios = byConsole.Value.IsBios;
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
                if (consoleKey is "UNKNOWN" or "" or "AMBIGUOUS")
                {
                    var unknownResolution = ResolveUnknownDatMatch(datIndex, hash, detectionResult);
                    if (unknownResolution.IsMatch)
                    {
                        datMatch = true;
                        computedHash = hash;
                        datMatchedBios = unknownResolution.IsBios;
                        datResolvedFromAmbiguousCandidates = unknownResolution.ResolvedFromAmbiguousCandidates;
                        var previousConsole = consoleKey;
                        consoleKey = unknownResolution.ConsoleKey!;
                        if (!string.IsNullOrEmpty(previousConsole) &&
                            previousConsole != "UNKNOWN" &&
                            !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                        {
                            detectionConflict = true;
                        }
                        if (unknownResolution.ResolvedFromAmbiguousCandidates)
                        {
                            context.OnProgress?.Invoke(
                                $"[DAT] Mehrdeutigen Hash via Hypothesen aufgeloest: {Path.GetFileName(filePath)} → {consoleKey}");
                        }
                        else
                        {
                            context.OnProgress?.Invoke(
                                $"[DAT] Konsole via DAT erkannt: {Path.GetFileName(filePath)} → {consoleKey}");
                        }
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
                    var byConsole = datIndex.LookupWithFilename(consoleKey, hash);
                    datMatch = byConsole is not null;
                    datMatchedBios = byConsole is not null && byConsole.Value.IsBios;
                }
            }
        }

        return new DatLookupResult(datMatch, datMatchedBios, datResolvedFromAmbiguousCandidates,
            computedHash, computedHeaderlessHash, consoleKey, detectionConflict);
    }

    private static void ResolveBios(
        ref FileCategory category, ref MatchEvidence matchEvidence, ref string? computedHash,
        bool datMatchedBios, IReadOnlySet<string>? knownBiosHashes,
        FileHashService? hashService, string filePath,
        string? computedHeaderlessHash, PipelineContext context)
    {
        if (datMatchedBios)
        {
            category = FileCategory.Bios;
            matchEvidence = matchEvidence with
            {
                Reasoning = string.IsNullOrWhiteSpace(matchEvidence.Reasoning)
                    ? "DAT marks this hash as BIOS."
                    : $"{matchEvidence.Reasoning}; DAT marks hash as BIOS",
                Level = matchEvidence.Level == MatchLevel.None ? MatchLevel.Strong : matchEvidence.Level
            };
        }

        if (knownBiosHashes is not null && hashService is not null && string.IsNullOrWhiteSpace(computedHash))
        {
            computedHash = hashService.GetHash(filePath, context.Options.HashType);
        }

        if (knownBiosHashes is not null)
        {
            if (!string.IsNullOrWhiteSpace(computedHash) && knownBiosHashes.Contains(computedHash))
            {
                category = FileCategory.Bios;
                matchEvidence = matchEvidence with
                {
                    Level = MatchLevel.Exact,
                    Reasoning = "Known BIOS hash catalog match.",
                    DatVerified = matchEvidence.DatVerified
                };
            }
            if (!string.IsNullOrWhiteSpace(computedHeaderlessHash) && knownBiosHashes.Contains(computedHeaderlessHash))
            {
                category = FileCategory.Bios;
                matchEvidence = matchEvidence with
                {
                    Level = MatchLevel.Exact,
                    Reasoning = "Known BIOS headerless hash catalog match.",
                    DatVerified = matchEvidence.DatVerified
                };
            }
        }
    }

    private static void ApplyDatAuthority(
        ref int detectionConfidence, ref bool hasHardEvidence, ref bool isSoftOnly,
        ref SortDecision sortDecision, ref MatchEvidence matchEvidence,
        bool detectionConflict, bool datResolvedFromAmbiguousCandidates)
    {
        detectionConfidence = detectionConflict
            ? Math.Max(detectionConfidence, 95)
            : 100;
        hasHardEvidence = true;
        isSoftOnly = false;
        if (datResolvedFromAmbiguousCandidates)
        {
            sortDecision = SortDecision.Review;
            matchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Ambiguous,
                Reasoning = "DAT hash matched multiple consoles; resolved using detector hypotheses and routed to review.",
                Sources = new[] { "DatHash", "DetectorHypotheses" },
                HasHardEvidence = true,
                HasConflict = true,
                DatVerified = false
            };
        }
        else
        {
            sortDecision = detectionConflict
                ? SortDecision.Review
                : SortDecision.DatVerified;
            matchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Exact,
                Reasoning = "Exact DAT hash match.",
                Sources = new[] { "DatHash" },
                HasHardEvidence = true,
                HasConflict = detectionConflict,
                DatVerified = !detectionConflict
            };
        }
    }

    internal static DatUnknownResolution ResolveUnknownDatMatch(
        DatIndex datIndex,
        string hash,
        ConsoleDetectionResult? detectionResult)
    {
        var matches = datIndex.LookupAllByHash(hash);
        if (matches.Count == 0)
            return DatUnknownResolution.NoMatch;

        if (matches.Count == 1)
        {
            var single = matches[0];
            return new DatUnknownResolution(true, single.ConsoleKey, single.Entry.IsBios, false);
        }

        if (detectionResult is null || detectionResult.Hypotheses.Count == 0)
            return DatUnknownResolution.NoMatch;

        var matchMap = new Dictionary<string, DatIndex.DatIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (consoleKey, entry) in matches)
        {
            if (!matchMap.ContainsKey(consoleKey))
                matchMap[consoleKey] = entry;
        }

        var rankedHypothesisKeys = detectionResult.Hypotheses
            .OrderByDescending(h => h.Confidence)
            .ThenBy(h => h.ConsoleKey, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.ConsoleKey)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        string? selectedKey = null;
        foreach (var hypothesisKey in rankedHypothesisKeys)
        {
            if (matchMap.ContainsKey(hypothesisKey))
            {
                selectedKey = hypothesisKey;
                break;
            }
        }

        if (selectedKey is null)
            return DatUnknownResolution.NoMatch;

        return new DatUnknownResolution(true, selectedKey, matchMap[selectedKey].IsBios, true);
    }

    internal readonly record struct DatUnknownResolution(
        bool IsMatch,
        string? ConsoleKey,
        bool IsBios,
        bool ResolvedFromAmbiguousCandidates)
    {
        public static DatUnknownResolution NoMatch { get; } = new(false, null, false, false);
    }

}

public sealed record EnrichmentPhaseInput(
    IReadOnlyList<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null,
    IReadOnlySet<string>? KnownBiosHashes = null);

public sealed record EnrichmentPhaseStreamingInput(
    IAsyncEnumerable<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null,
    IReadOnlySet<string>? KnownBiosHashes = null);