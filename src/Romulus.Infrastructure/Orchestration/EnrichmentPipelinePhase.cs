using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.GameKeys;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Hashing;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Romulus.Core.SetParsing;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that enriches scanned files into RomCandidate objects.
/// </summary>
public sealed class EnrichmentPipelinePhase : IPipelinePhase<EnrichmentPhaseInput, List<RomCandidate>>
{
    private const int ParallelizationThreshold = 4;
    private static readonly IFamilyDatStrategyResolver DefaultFamilyDatStrategyResolver = new FamilyDatStrategyResolver();
    private static readonly IFamilyPipelineSelector DefaultFamilyPipelineSelector = new FamilyPipelineSelector();

    /// <summary>Generic stems too short or ambiguous to qualify as strict DAT name candidates.</summary>
    private static readonly HashSet<string> GenericDatNameBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "track", "disk", "disc", "rom", "game", "image"
    };

    public string Name => "Enrichment";

    public List<RomCandidate> Execute(EnrichmentPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        var onProgress = CreateSerializedProgressCallback(context.OnProgress);
        var parallelism = GetParallelismHint(input.Files.Count);
        if (input.Files.Count <= ParallelizationThreshold || parallelism <= 1)
        {
            var candidates = new List<RomCandidate>(input.Files.Count);
            var versionScorer = new VersionScorer();

            foreach (var file in input.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex,
                    input.HeaderlessHasher, input.KnownBiosHashes, input.FamilyDatStrategyResolver, input.FamilyPipelineSelector,
                    context, versionScorer, onProgress));
            }

            return candidates;
        }

        var results = new RomCandidate[input.Files.Count];
        using var versionScorers = new ThreadLocal<VersionScorer>(() => new VersionScorer());

        Parallel.For(
            0,
            input.Files.Count,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = cancellationToken
            },
            index =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = MapToCandidate(
                    input.Files[index],
                    input.ConsoleDetector,
                    input.HashService,
                    input.ArchiveHashService,
                    input.DatIndex,
                    input.HeaderlessHasher,
                    input.KnownBiosHashes,
                    input.FamilyDatStrategyResolver,
                    input.FamilyPipelineSelector,
                    context,
                    versionScorers.Value!,
                    onProgress);
            });

        return results.ToList();
    }

    public async IAsyncEnumerable<RomCandidate> ExecuteStreamingAsync(
        EnrichmentPhaseStreamingInput input,
        PipelineContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var onProgress = CreateSerializedProgressCallback(context.OnProgress);
        var parallelism = GetParallelismHint();
        if (parallelism <= 1)
        {
            var versionScorer = new VersionScorer();
            await foreach (var file in input.Files.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return MapToCandidate(file, input.ConsoleDetector, input.HashService, input.ArchiveHashService, input.DatIndex,
                    input.HeaderlessHasher, input.KnownBiosHashes, input.FamilyDatStrategyResolver, input.FamilyPipelineSelector,
                    context, versionScorer, onProgress);
            }

            yield break;
        }

        using var versionScorers = new ThreadLocal<VersionScorer>(() => new VersionScorer());
        var pending = new Queue<Task<RomCandidate>>(parallelism);

        Task<RomCandidate> StartCandidateTask(ScannedFileEntry file)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MapToCandidate(
                    file,
                    input.ConsoleDetector,
                    input.HashService,
                    input.ArchiveHashService,
                    input.DatIndex,
                    input.HeaderlessHasher,
                    input.KnownBiosHashes,
                    input.FamilyDatStrategyResolver,
                    input.FamilyPipelineSelector,
                    context,
                    versionScorers.Value!,
                    onProgress);
            }, cancellationToken);
        }

        await foreach (var file in input.Files.WithCancellation(cancellationToken))
        {
            pending.Enqueue(StartCandidateTask(file));
            if (pending.Count < parallelism)
                continue;

            yield return await pending.Dequeue().WaitAsync(cancellationToken);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await pending.Dequeue().WaitAsync(cancellationToken);
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
        IFamilyDatStrategyResolver? familyDatStrategyResolver,
        IFamilyPipelineSelector? familyPipelineSelector,
        PipelineContext context,
        VersionScorer versionScorer,
        Action<string>? onProgress)
    {
        familyDatStrategyResolver ??= DefaultFamilyDatStrategyResolver;
        familyPipelineSelector ??= DefaultFamilyPipelineSelector;

        var filePath = file.Path;
        var root = file.Root;
        var ext = file.Extension;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var gameKey = GameKeyNormalizer.Normalize(
                fileName,
                GameKeyNormalizationProfile.TagPatterns ?? [],
                GameKeyNormalizationProfile.AlwaysAliasMap);

        long sizeBytes = file.SizeBytes ?? 0;
        if (sizeBytes == 0 && File.Exists(filePath))
        {
            try
            {
                sizeBytes = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"WARNING: Could not read file size for {filePath}: {ex.Message}");
            }
        }

        var classification = FileClassifier.Analyze(fileName, ext, sizeBytes, context.Options.AggressiveJunk);
        var category = classification.Category;

        string consoleKey = "UNKNOWN";
        int detectionConfidence = 0;
        bool detectionConflict = false;
        bool hasHardEvidence = false;
        bool isSoftOnly = true;
        var sortDecision = SortDecision.Unknown;
        var decisionClass = DecisionClass.Unknown;
        MatchEvidence matchEvidence = new()
        {
            Tier = EvidenceTier.Tier4_Unknown,
            PrimaryMatchKind = MatchKind.None,
        };
        ConsoleDetectionResult? detectionResult = null;

        var regionTag = Core.Regions.RegionDetector.GetRegionTag(fileName);
        var regionScore = FormatScorer.GetRegionScore(regionTag, context.Options.PreferRegions);
        var fmtScore = FormatScorer.GetFormatScore(ext);
        var verScore = versionScorer.GetVersionScore(fileName);

        // DAT-first lookup first, then fallback to detector-guided resolution only if needed.
        var preDetectFamily = ResolveFamily(consoleDetector, consoleKey, detectionResult);
        var preDetectHashStrategy = ResolveHashStrategy(consoleDetector, consoleKey, detectionResult);
        var preDetectDatPolicy = familyDatStrategyResolver.ResolvePolicy(preDetectFamily, ext, preDetectHashStrategy);

        var datResult = LookupDat(filePath, ext, sizeBytes, consoleKey, detectionConflict,
            datIndex, hashService, archiveHashService, headerlessHasher, preDetectDatPolicy,
            detectionResult: null, context, onProgress);

        // Even with a DAT-first hit, run detector once to gather family/context evidence.
        // This enables post-validation for cross-family mismatches (Phase 4).
        // IMPORTANT: Only extract family/conflict information. Do NOT overwrite primary
        // enrichment variables (confidence, conflict flag, hardEvidence, isSoftOnly)
        // to avoid side-effects on the DAT-authority decision path.
        if (datResult.DatMatch && detectionResult is null && consoleDetector is not null)
        {
            var parityDetection = consoleDetector.DetectWithConfidence(filePath, root);
            if (parityDetection.ConsoleKey is not "UNKNOWN" and not "")
            {
                detectionResult = parityDetection;
                // Only enrich matchEvidence if it was completely empty (DAT-first with no prior detection)
                if (matchEvidence.PrimaryMatchKind == MatchKind.None)
                    matchEvidence = parityDetection.MatchEvidence ?? matchEvidence;
            }
        }

        if (!datResult.DatMatch && consoleDetector is not null)
        {
            detectionResult = consoleDetector.DetectWithConfidence(filePath, root);
            consoleKey = detectionResult.ConsoleKey;
            detectionConfidence = detectionResult.Confidence;
            detectionConflict = detectionResult.HasConflict;
            hasHardEvidence = detectionResult.HasHardEvidence;
            isSoftOnly = detectionResult.IsSoftOnly;
            sortDecision = detectionResult.SortDecision;
            decisionClass = detectionResult.DecisionClass;
            matchEvidence = detectionResult.MatchEvidence ?? matchEvidence;

            var postDetectFamily = ResolveFamily(consoleDetector, consoleKey, detectionResult);
            var postDetectHashStrategy = ResolveHashStrategy(consoleDetector, consoleKey, detectionResult);
            var postDetectDatPolicy = familyDatStrategyResolver.ResolvePolicy(postDetectFamily, ext, postDetectHashStrategy);

            datResult = LookupDat(filePath, ext, sizeBytes, consoleKey, detectionConflict,
                datIndex, hashService, archiveHashService, headerlessHasher, postDetectDatPolicy,
                detectionResult, context, onProgress);
        }

        consoleKey = datResult.ConsoleKey;
        detectionConflict = datResult.DetectionConflict || (detectionResult?.HasConflict ?? false);
        var computedHash = datResult.ComputedHash;
        var computedHeaderlessHash = datResult.ComputedHeaderlessHash;

        if (!datResult.DatMatch && consoleKey is "UNKNOWN" or "" or "AMBIGUOUS")
        {
            onProgress?.Invoke(
                $"[DAT] Kein Match fuer {consoleKey}-Konsole: {Path.GetFileName(filePath)}");
        }

        // BIOS classification (from DAT metadata and known BIOS hash catalog)
        ResolveBios(ref category, ref matchEvidence, ref computedHash,
            datResult.DatMatchedBios, knownBiosHashes, hashService, filePath,
            computedHeaderlessHash, context, onProgress);

        // DAT authority — tier-based decision. DAT remains the highest authority.
        // Derive conflict type from detector result for family-aware escalation.
        var conflictType = detectionResult?.ConflictType ?? ConflictType.None;
        var datAvailableForConsole = datIndex is not null && datIndex.ConsoleCount > 0;

        if (datResult.DatMatch && consoleKey is not "")
        {
            var datTier = datResult.DatMatchKind.GetTier();
            var datConfidence = datResult.DatNameOnlyMatch ? 85 : 100;
            var datDecision = DecisionResolver.Resolve(datTier, detectionConflict, datConfidence,
                datAvailable: datAvailableForConsole, conflictType: conflictType);

            detectionConfidence = Math.Max(detectionConfidence, datConfidence);
            decisionClass = datDecision;
            sortDecision = datDecision.ToSortDecision();
            hasHardEvidence = datTier <= EvidenceTier.Tier1_Structural;
            isSoftOnly = !hasHardEvidence;

            // Hash-verified DAT match restores canonical game classification unless
            // BIOS detection has already marked this file as BIOS.
            if (!datResult.DatNameOnlyMatch && category is FileCategory.Junk or FileCategory.NonGame or FileCategory.Unknown)
                category = FileCategory.Game;

            matchEvidence = new MatchEvidence
            {
                Level = datResult.DatNameOnlyMatch
                    ? MatchLevel.Probable
                    : detectionConflict
                        ? MatchLevel.Ambiguous
                        : MatchLevel.Exact,
                Reasoning = datResult.DatNameOnlyMatch
                    ? "DAT game name match (no hash verification — disc image format)."
                    : datResult.DatResolvedFromAmbiguousCandidates
                        ? "DAT hash matched multiple consoles; resolved with detector hypotheses and routed to review."
                        : "Exact DAT hash match.",
                Sources = datResult.DatNameOnlyMatch
                    ? ["DatName"]
                    : datResult.DatResolvedFromAmbiguousCandidates
                        ? ["DatHash", "DetectorHypotheses"]
                        : ["DatHash"],
                HasHardEvidence = hasHardEvidence,
                HasConflict = detectionConflict,
                DatVerified = datDecision == DecisionClass.DatVerified,
                Tier = datTier,
                PrimaryMatchKind = datResult.DatMatchKind,
            };
        }
        else if (detectionResult is not null)
        {
            var fallbackTier = matchEvidence.PrimaryMatchKind.GetTier();
            decisionClass = DecisionResolver.Resolve(fallbackTier, detectionConflict, detectionConfidence,
                datAvailable: datAvailableForConsole, conflictType: conflictType);
            sortDecision = decisionClass.ToSortDecision();
        }

        var headerScore = FormatScorer.GetHeaderVariantScore(root, filePath);
        var sizeTieBreak = FormatScorer.GetSizeTieBreakScore(null, ext, sizeBytes);
        var setMembers = PipelinePhaseHelpers.GetSetMembers(filePath, ext, includeM3uMembers: true);
        var missingSetMembersCount = SetDescriptorSupport.GetMissingFilesCount(filePath, ext);
        var completeness = CompletenessScorer.Calculate(filePath, ext, setMembers, missingSetMembersCount, datResult.DatMatch);

        // Derive final evidence tier: DAT match takes precedence over detection-level evidence
        var finalMatchKind = datResult.DatMatchKind != MatchKind.None
            ? datResult.DatMatchKind
            : matchEvidence.PrimaryMatchKind;
        var finalEvidenceTier = finalMatchKind.GetTier();
        var platformFamily = consoleDetector?.GetPlatformFamily(consoleKey) ?? PlatformFamily.Unknown;

        var detectedFamily = detectionResult is not null && consoleDetector is not null
            ? consoleDetector.GetPlatformFamily(detectionResult.ConsoleKey)
            : PlatformFamily.Unknown;

        var familyDecision = familyPipelineSelector.Apply(new FamilyPipelineInput(
            DecisionClass: decisionClass,
            SortDecision: sortDecision,
            MatchEvidence: matchEvidence,
            DetectionConfidence: detectionConfidence,
            DetectionConflict: detectionConflict,
            ConflictType: conflictType,
            DatMatch: datResult.DatMatch,
            DetectedFamily: detectedFamily,
            ResolvedFamily: platformFamily,
            FinalMatchKind: finalMatchKind));

        decisionClass = familyDecision.DecisionClass;
        sortDecision = familyDecision.SortDecision;
        matchEvidence = familyDecision.MatchEvidence;
        detectionConfidence = familyDecision.DetectionConfidence;
        detectionConflict = familyDecision.DetectionConflict;
        conflictType = familyDecision.ConflictType;

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
            datGameName: datResult.DatGameName,
            hash: computedHash,
            headerlessHash: computedHeaderlessHash,
            classificationReasonCode: classification.ReasonCode,
            classificationConfidence: classification.Confidence,
            detectionConfidence: detectionConfidence,
            detectionConflict: detectionConflict,
            detectionConflictType: conflictType,
            hasHardEvidence: hasHardEvidence,
            isSoftOnly: isSoftOnly,
            sortDecision: sortDecision,
            decisionClass: decisionClass,
            matchEvidence: matchEvidence,
            evidenceTier: finalEvidenceTier,
                primaryMatchKind: finalMatchKind,
                platformFamily: platformFamily);
    }

    private readonly record struct DatLookupResult(
        bool DatMatch,
        bool DatMatchedBios,
        bool DatResolvedFromAmbiguousCandidates,
        bool DatNameOnlyMatch,
        string? ComputedHash,
        string? ComputedHeaderlessHash,
        string? DatGameName,
        string ConsoleKey,
        bool DetectionConflict,
        MatchKind DatMatchKind);

    private static DatLookupResult LookupDat(
        string filePath, string ext, long sizeBytes,
        string consoleKey, bool detectionConflict,
        DatIndex? datIndex, FileHashService? hashService,
        ArchiveHashService? archiveHashService,
        Contracts.Ports.IHeaderlessHasher? headerlessHasher,
        FamilyDatPolicy datPolicy,
        ConsoleDetectionResult? detectionResult,
        PipelineContext context,
        Action<string>? onProgress)
    {
        bool datMatch = false;
        bool datMatchedBios = false;
        bool datResolvedFromAmbiguousCandidates = false;
        string? computedHash = null;
        string? computedHeaderlessHash = null;
        string? datGameName = null;
        bool datNameOnlyMatch = false;
        var datMatchKind = MatchKind.None;

        if (datIndex is null || hashService is null)
            return new DatLookupResult(false, false, false, false, null, null, null, consoleKey, detectionConflict, MatchKind.None);

        if (sizeBytes > 50_000_000)
        {
            var sizeMb = sizeBytes / (1024.0 * 1024.0);
            onProgress?.Invoke(RunProgressLocalization.Format(
                "Scan.HashLarge",
                "[Scan] Hash: {0} ({1:F0} MB)…",
                Path.GetFileName(filePath),
                sizeMb));
        }

        var lowerExt = ext.ToLowerInvariant();
        var isArchive = lowerExt is ".zip" or ".7z";

        // For archives: try inner hashes first (DATs store ROM content hashes, not container hashes)
        if (isArchive && archiveHashService is not null && datPolicy.PreferArchiveInnerHash)
        {
            var innerHashes = archiveHashService.GetArchiveHashes(filePath, context.Options.HashType);
            foreach (var innerHash in innerHashes)
            {
                computedHash ??= innerHash;

                // DAT-first: always try cross-console lookup first, even when consoleKey is known.
                // This catches misdetections where Detection assigned the wrong console.
                var crossConsoleResult = TryCrossConsoleDatLookup(datIndex, innerHash, consoleKey, detectionResult, filePath, onProgress);
                if (crossConsoleResult.IsMatch)
                {
                    datMatch = true;
                    computedHash = innerHash;
                    datMatchedBios = crossConsoleResult.IsBios;
                    datResolvedFromAmbiguousCandidates = crossConsoleResult.ResolvedFromAmbiguousCandidates;
                    datGameName = crossConsoleResult.DatGameName;
                    datMatchKind = MatchKind.ArchiveInnerExactDat;
                    if (!string.Equals(consoleKey, crossConsoleResult.ConsoleKey, StringComparison.OrdinalIgnoreCase)
                        && consoleKey is not "UNKNOWN" and not "" and not "AMBIGUOUS")
                    {
                        detectionConflict = true;
                    }
                    consoleKey = crossConsoleResult.ConsoleKey!;
                    break;
                }
            }
        }

        // For non-archives: try headerless hash first (No-Intro DATs hash without headers for NES/SNES/7800/Lynx)
        if (!datMatch && !isArchive && datPolicy.UseHeaderlessHash && headerlessHasher is not null
            && consoleKey is not "UNKNOWN" and not "")
        {
            computedHeaderlessHash = headerlessHasher.ComputeHeaderlessHash(filePath, consoleKey, context.Options.HashType);
            if (computedHeaderlessHash is not null)
            {
                if (datIndex.Lookup(consoleKey, computedHeaderlessHash) is not null)
                {
                    datMatch = true;
                    datGameName = datIndex.LookupWithFilename(consoleKey, computedHeaderlessHash)?.GameName;
                    datMatchKind = MatchKind.HeaderlessDatHash;
                }
                else
                {
                    // Cross-console fallback: headerless hash might match a different console's DAT
                    var crossConsoleResult = TryCrossConsoleDatLookup(datIndex, computedHeaderlessHash, consoleKey, detectionResult, filePath, onProgress);
                    if (crossConsoleResult.IsMatch)
                    {
                        datMatch = true;
                        datMatchedBios = crossConsoleResult.IsBios;
                        datResolvedFromAmbiguousCandidates = crossConsoleResult.ResolvedFromAmbiguousCandidates;
                        datGameName = crossConsoleResult.DatGameName;
                        datMatchKind = MatchKind.HeaderlessDatHash;
                        if (!string.Equals(consoleKey, crossConsoleResult.ConsoleKey, StringComparison.OrdinalIgnoreCase))
                        {
                            detectionConflict = true;
                        }
                        consoleKey = crossConsoleResult.ConsoleKey!;
                    }
                }
            }
        }

        // Fallback: try container hash (works for uncompressed ROMs, CHD, ISO, etc.)
        if (!datMatch && datPolicy.UseContainerHash)
        {
            var hash = hashService.GetHash(filePath, context.Options.HashType);
            computedHash ??= hash;
            if (hash is not null)
            {
                // DAT-first: always try cross-console lookup for container hash too
                var crossConsoleResult = TryCrossConsoleDatLookup(datIndex, hash, consoleKey, detectionResult, filePath, onProgress);
                if (crossConsoleResult.IsMatch)
                {
                    datMatch = true;
                    computedHash = hash;
                    datMatchedBios = crossConsoleResult.IsBios;
                    datResolvedFromAmbiguousCandidates = crossConsoleResult.ResolvedFromAmbiguousCandidates;
                    datGameName = crossConsoleResult.DatGameName;
                    datMatchKind = lowerExt == ".chd" ? MatchKind.ChdRawDatHash : MatchKind.ExactDatHash;
                    if (!string.Equals(consoleKey, crossConsoleResult.ConsoleKey, StringComparison.OrdinalIgnoreCase)
                        && consoleKey is not "UNKNOWN" and not "" and not "AMBIGUOUS")
                    {
                        detectionConflict = true;
                    }
                    consoleKey = crossConsoleResult.ConsoleKey!;
                }
                else if (consoleKey is not "UNKNOWN" and not "" and not "AMBIGUOUS")
                {
                    var hashHint = hash.Length >= 12 ? hash[..12] : hash;
                    onProgress?.Invoke(
                        $"[DAT] Kein Match: {Path.GetFileName(filePath)} (hash={hashHint})");
                }
            }
        }

        // Stage 4: Name-based fallback for disc images (CHD raw SHA1 ≠ per-track SHA1 in Redump DATs)
        if (!datMatch
            && datPolicy.AllowNameOnlyDatMatch
            && lowerExt is ".chd" or ".iso" or ".gcm" or ".img" or ".cso" or ".rvz")
        {
            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrEmpty(stem)
                && (!datPolicy.RequireStrictNameForNameOnly || IsStrictDatNameCandidate(stem)))
            {
                if (consoleKey is "UNKNOWN" or "" or "AMBIGUOUS")
                {
                    // Conservative guard: unknown-console name-only fallback only with detector context.
                    // This prevents pre-detection false positives for families where name-only matching is unsafe.
                    if (detectionResult is null)
                    {
                        onProgress?.Invoke(
                            $"[DAT] Name-Only-Fallback uebersprungen (kein Detector-Kontext): {Path.GetFileName(filePath)}");
                    }
                    else
                    {
                        var nameMatches = datIndex.LookupAllByName(stem);
                        if (nameMatches.Count == 1)
                        {
                            datMatch = true;
                            datNameOnlyMatch = true;
                            datMatchKind = MatchKind.DatNameOnlyMatch;
                            var previousConsole = consoleKey;
                            consoleKey = nameMatches[0].ConsoleKey;
                            datMatchedBios = nameMatches[0].Entry.IsBios;
                            datGameName = nameMatches[0].Entry.GameName;
                            if (!string.IsNullOrEmpty(previousConsole) &&
                                previousConsole != "UNKNOWN" &&
                                !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                            {
                                detectionConflict = true;
                            }

                            onProgress?.Invoke(
                                $"[DAT] Konsole via DAT-Name erkannt: {Path.GetFileName(filePath)} → {consoleKey}");
                        }
                        else if (nameMatches.Count > 1)
                        {
                            var resolution = ResolveUnknownDatNameMatch(nameMatches, detectionResult);
                            if (resolution.IsMatch)
                            {
                                datMatch = true;
                                datNameOnlyMatch = true;
                                datMatchKind = MatchKind.DatNameOnlyMatch;
                                datMatchedBios = resolution.IsBios;
                                datResolvedFromAmbiguousCandidates = resolution.ResolvedFromAmbiguousCandidates;
                                datGameName = resolution.DatGameName;
                                var previousConsole = consoleKey;
                                consoleKey = resolution.ConsoleKey!;
                                if (!string.IsNullOrEmpty(previousConsole) &&
                                    previousConsole != "UNKNOWN" &&
                                    !string.Equals(previousConsole, consoleKey, StringComparison.OrdinalIgnoreCase))
                                {
                                    detectionConflict = true;
                                }

                                onProgress?.Invoke(
                                    $"[DAT] Mehrdeutigen Name via Hypothesen aufgeloest: {Path.GetFileName(filePath)} → {consoleKey}");
                            }
                        }
                    }
                }
                else
                {
                    var byName = datIndex.LookupByName(consoleKey, stem);
                    if (byName is not null)
                    {
                        datMatch = true;
                        datNameOnlyMatch = true;
                        datGameName = byName.Value.GameName;
                        datMatchKind = MatchKind.DatNameOnlyMatch;
                        datMatchedBios = byName.Value.IsBios;
                        onProgress?.Invoke(
                            $"[DAT] Name-Match: {Path.GetFileName(filePath)} → {consoleKey}");
                    }
                }
            }
        }

        return new DatLookupResult(datMatch, datMatchedBios, datResolvedFromAmbiguousCandidates,
            datNameOnlyMatch, computedHash, computedHeaderlessHash, datGameName, consoleKey, detectionConflict, datMatchKind);
    }

    /// <summary>
    /// DAT-first cross-console lookup: searches ALL loaded DATs for a hash match.
    /// If consoleKey is already known, tries that console first (fast path),
    /// then falls back to cross-console search if no match in the expected console.
    /// This is the key improvement over the old approach which only searched one console's DAT.
    /// </summary>
    private static DatUnknownResolution TryCrossConsoleDatLookup(
        DatIndex datIndex, string hash, string consoleKey,
        ConsoleDetectionResult? detectionResult, string filePath, Action<string>? onProgress)
    {
        // Fast path: if consoleKey is known, try that console first
        if (consoleKey is not "UNKNOWN" and not "" and not "AMBIGUOUS")
        {
            var byConsole = datIndex.LookupWithFilename(consoleKey, hash);
            if (byConsole is not null)
            {
                return new DatUnknownResolution(true, consoleKey, byConsole.Value.IsBios, false, byConsole.Value.GameName);
            }
        }

        // Cross-console lookup: search ALL DATs
        var resolution = ResolveUnknownDatMatch(datIndex, hash, detectionResult);
        if (resolution.IsMatch)
        {
            if (resolution.ResolvedFromAmbiguousCandidates)
            {
                onProgress?.Invoke(
                    $"[DAT] Mehrdeutigen Hash via Hypothesen aufgeloest: {Path.GetFileName(filePath)} → {resolution.ConsoleKey}");
            }
            else
            {
                onProgress?.Invoke(
                    $"[DAT] Konsole via DAT-Hash erkannt: {Path.GetFileName(filePath)} → {resolution.ConsoleKey}");
            }
        }

        return resolution;
    }

    private static void ResolveBios(
        ref FileCategory category, ref MatchEvidence matchEvidence, ref string? computedHash,
        bool datMatchedBios, IReadOnlySet<string>? knownBiosHashes,
        FileHashService? hashService, string filePath,
        string? computedHeaderlessHash, PipelineContext context, Action<string>? onProgress)
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
            return new DatUnknownResolution(true, single.ConsoleKey, single.Entry.IsBios, false, single.Entry.GameName);
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

        return new DatUnknownResolution(true, selectedKey, matchMap[selectedKey].IsBios, true, matchMap[selectedKey].GameName);
    }

    internal readonly record struct DatUnknownResolution(
        bool IsMatch,
        string? ConsoleKey,
        bool IsBios,
        bool ResolvedFromAmbiguousCandidates,
        string? DatGameName)
    {
        public static DatUnknownResolution NoMatch { get; } = new(false, null, false, false, null);
    }

    /// <summary>
    /// Resolve UNKNOWN console via name-based DAT matches and detection hypotheses.
    /// Same logic as ResolveUnknownDatMatch but operates on name-based matches.
    /// </summary>
    internal static DatUnknownResolution ResolveUnknownDatNameMatch(
        IReadOnlyList<(string ConsoleKey, DatIndex.DatIndexEntry Entry)> nameMatches,
        ConsoleDetectionResult? detectionResult)
    {
        if (nameMatches.Count == 0)
            return DatUnknownResolution.NoMatch;

        if (nameMatches.Count == 1)
        {
            var single = nameMatches[0];
            return new DatUnknownResolution(true, single.ConsoleKey, single.Entry.IsBios, false, single.Entry.GameName);
        }

        if (detectionResult is null || detectionResult.Hypotheses.Count == 0)
            return DatUnknownResolution.NoMatch;

        var matchMap = new Dictionary<string, DatIndex.DatIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (consoleKey, entry) in nameMatches)
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

        return new DatUnknownResolution(true, selectedKey, matchMap[selectedKey].IsBios, true, matchMap[selectedKey].GameName);
    }

    internal static PlatformFamily ResolveFamily(
        ConsoleDetector? consoleDetector,
        string consoleKey,
        ConsoleDetectionResult? detectionResult)
    {
        if (consoleDetector is not null && !string.IsNullOrWhiteSpace(consoleKey)
            && consoleKey is not "UNKNOWN" and not "AMBIGUOUS")
        {
            return consoleDetector.GetPlatformFamily(consoleKey);
        }

        if (consoleDetector is not null && detectionResult is { Hypotheses.Count: > 0 })
        {
            var topHypothesis = detectionResult.Hypotheses
                .OrderByDescending(h => h.Confidence)
                .ThenBy(h => h.ConsoleKey, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (topHypothesis is not null)
                return consoleDetector.GetPlatformFamily(topHypothesis.ConsoleKey);
        }

        return PlatformFamily.Unknown;
    }

    internal static string? ResolveHashStrategy(
        ConsoleDetector? consoleDetector,
        string consoleKey,
        ConsoleDetectionResult? detectionResult)
    {
        if (consoleDetector is not null && !string.IsNullOrWhiteSpace(consoleKey)
            && consoleKey is not "UNKNOWN" and not "AMBIGUOUS")
        {
            return consoleDetector.GetConsole(consoleKey)?.HashStrategy;
        }

        if (consoleDetector is not null && detectionResult is { Hypotheses.Count: > 0 })
        {
            var topHypothesis = detectionResult.Hypotheses
                .OrderByDescending(h => h.Confidence)
                .ThenBy(h => h.ConsoleKey, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (topHypothesis is not null)
                return consoleDetector.GetConsole(topHypothesis.ConsoleKey)?.HashStrategy;
        }

        return null;
    }

    internal static bool IsStrictDatNameCandidate(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return false;

        var normalized = stem.Trim();
        if (normalized.Length < 3)
            return false;

        var lowered = normalized.ToLowerInvariant();
        if (GenericDatNameBlocklist.Contains(lowered))
            return false;

        var lettersOrDigits = normalized.Count(char.IsLetterOrDigit);
        return lettersOrDigits >= 3;
    }

    internal static int GetParallelismHint(int itemCountHint = int.MaxValue)
    {
        var optimalThreads = ParallelHasher.GetOptimalThreadCount();
        if (itemCountHint <= ParallelizationThreshold || optimalThreads <= 1)
            return 1;

        return optimalThreads;
    }

    private static Action<string>? CreateSerializedProgressCallback(Action<string>? onProgress)
    {
        if (onProgress is null)
            return null;

        var gate = new object();
        var lastInvoke = 0L; // Stopwatch ticks of last forwarded call
        const long ThrottleIntervalTicks = 200 * TimeSpan.TicksPerMillisecond; // 200ms

        return message =>
        {
            var now = Stopwatch.GetTimestamp();
            lock (gate)
            {
                if (now - lastInvoke < ThrottleIntervalTicks)
                    return;
                lastInvoke = now;
                onProgress(message);
            }
        };
    }

}

public sealed record EnrichmentPhaseInput(
    IReadOnlyList<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null,
    IReadOnlySet<string>? KnownBiosHashes = null,
    IFamilyDatStrategyResolver? FamilyDatStrategyResolver = null,
    IFamilyPipelineSelector? FamilyPipelineSelector = null);

public sealed record EnrichmentPhaseStreamingInput(
    IAsyncEnumerable<ScannedFileEntry> Files,
    ConsoleDetector? ConsoleDetector,
    FileHashService? HashService,
    ArchiveHashService? ArchiveHashService,
    DatIndex? DatIndex,
    Contracts.Ports.IHeaderlessHasher? HeaderlessHasher = null,
    IReadOnlySet<string>? KnownBiosHashes = null,
    IFamilyDatStrategyResolver? FamilyDatStrategyResolver = null,
    IFamilyPipelineSelector? FamilyPipelineSelector = null);
