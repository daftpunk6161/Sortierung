using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Programmatic expansion of ground-truth JSONL datasets.
/// Generates entries for all 69 systems across all 20 Fallklassen to meet gate thresholds.
/// </summary>
internal sealed class DatasetExpander
{
    private readonly HashSet<string> _existingIds;
    private readonly Dictionary<string, int> _idCounters = new(StringComparer.Ordinal);

    private record SystemDef(
        string Key, bool DiscBased, string[] UniqueExts, string[] AmbigExts,
        string FolderAlias, string PrimaryDetection, string DatEcosystem,
        bool HasCartridgeHeader, string[] SampleGames, long TypicalSize);

    private static readonly SystemDef[] Systems = BuildSystemCatalog();

    public DatasetExpander(IEnumerable<GroundTruthEntry> existingEntries)
    {
        _existingIds = new HashSet<string>(
            existingEntries.Select(e => e.Id), StringComparer.Ordinal);
    }

    /// <summary>
    /// Generates all expanded entries grouped by target JSONL filename.
    /// Does NOT include existing entries — caller merges.
    /// </summary>
    public Dictionary<string, List<GroundTruthEntry>> GenerateExpansion()
    {
        var result = new Dictionary<string, List<GroundTruthEntry>>();

        GenerateCleanReferences(result);       // FC-01 → golden-core.jsonl
        GenerateWrongNameEntries(result);       // FC-02 → chaos-mixed.jsonl
        GenerateHeaderConflicts(result);        // FC-03 → chaos-mixed.jsonl
        GenerateWrongExtensions(result);        // FC-04 → edge-cases.jsonl
        GenerateFolderConflicts(result);        // FC-05 → edge-cases.jsonl
        GenerateDatExactEntries(result);        // FC-06 → dat-coverage.jsonl
        GenerateDatWeakEntries(result);         // FC-07 → dat-coverage.jsonl
        GenerateBiosEntries(result);            // FC-08 → golden-core.jsonl
        GenerateArcadeParentClone(result);      // FC-09 → golden-core.jsonl
        GenerateMultiDiscEntries(result);       // FC-10 → golden-realworld.jsonl
        GenerateMultiFileEntries(result);       // FC-11 → golden-realworld.jsonl
        GenerateArchiveInnerEntries(result);    // FC-12 → golden-realworld.jsonl
        GenerateDirectoryEntries(result);       // FC-13 → golden-realworld.jsonl
        GenerateUnknownExpected(result);        // FC-14 → negative-controls.jsonl
        GenerateAmbiguousEntries(result);       // FC-15 → edge-cases.jsonl
        GenerateNegativeControls(result);       // FC-16 → negative-controls.jsonl
        GenerateRepairBlocked(result);          // FC-17 → repair-safety.jsonl
        GenerateCrossSystem(result);            // FC-18 → edge-cases.jsonl
        GenerateJunkEntries(result);            // FC-19 → golden-realworld.jsonl
        GenerateBrokenEntries(result);          // FC-20 → chaos-mixed.jsonl
        GeneratePsDisambiguation(result);       // special: psDisambiguation
        GenerateHeaderlessEntries(result);      // special: headerless
        GenerateChdRawSha1Entries(result);      // special: chdRawSha1
        GenerateExtraArcadeEntries(result);     // boost: arcade family coverage
        GenerateExtraComputerEntries(result);   // boost: computer family coverage

        // ═══ PHASE A EXPANSION ═══════════════════════════════════════════
        GenerateUnicodeFilenames(result);        // A1: chaos-mixed unicode
        GenerateFalseExtensionChaos(result);     // A1: chaos-mixed false extensions
        GenerateCorruptArchiveEntries(result);   // A1: chaos-mixed corrupt archives
        GenerateMixedArchiveChaos(result);       // A1: chaos-mixed cross-system archives
        GenerateFlatFolderChaos(result);         // A1: chaos-mixed flat folder
        GenerateDuplicateNameVariants(result);   // A1: chaos-mixed duplicate names
        GenerateRenamedExtensions(result);       // A1: chaos-mixed renamed extensions
        GenerateRegionVariants(result);          // A2: golden-realworld regions
        GenerateRevisionVariants(result);        // A2: golden-realworld revisions
        GenerateNoIntroNaming(result);           // A2: golden-realworld No-Intro
        GenerateRedumpNaming(result);            // A2: golden-realworld Redump
        GenerateFolderSortedCollection(result);  // A2: golden-realworld folder-sorted
        GenerateGbGbcAmbiguity(result);          // A3: edge-cases GB/GBC
        GenerateMd32xAmbiguity(result);          // A3: edge-cases MD/32X
        GenerateBiosEdgeCases(result);           // A3: edge-cases BIOS
        GenerateDatCollisions(result);           // A3: edge-cases DAT collisions
        GenerateAdditionalPsDisambiguation(result); // A3: edge-cases PSP/PS3
        GenerateFastRomNegatives(result);        // A4: negative-controls fast-ROM
        GenerateHomebrewNegatives(result);       // A4: negative-controls homebrew
        GenerateIrrelevantNegatives(result);     // A4: negative-controls irrelevant
        GenerateRepairHighConfidence(result);    // A5: repair-safety high confidence
        GenerateRepairConflict(result);          // A5: repair-safety conflict
        GenerateRepairFolderOnly(result);        // A5: repair-safety folder-only
        GenerateRepairWeakMatch(result);         // A5: repair-safety weak match

        // ═══ PHASE B+C EXPANSION ═════════════════════════════════════════
        GenerateArcadeDepthExpansion(result);    // B5: arcade depth
        GenerateDirectoryGameSamples(result);   // C3: directory-based games
        GenerateTosecCoverage(result);           // C5: TOSEC DAT coverage
        GenerateAdditionalMultiDiscVariants(result); // C6: more multi-disc
        GenerateContainerFormats(result);        // C6: CSO/RVZ/WBFS
        GenerateSerialNumberEntries(result);     // C6: serial number detection
        GenerateKeywordDetection(result);        // C6: keyword detection

        // ═══ PHASE S1 EXPANSION — Disc-Format-Tiefe ══════════════════════
        GenerateDiscFormatEntries(result);       // S1: CUE/BIN, GDI, CCD, MDS, M3U

        // ═══ PHASE S2 EXPANSION — BIOS-Fehlermodi ═══════════════════════
        GenerateBiosErrorModes(result);          // S2: wrong-name, wrong-folder, FP, FN, shared

        // ═══ PHASE S3 EXPANSION — Arcade-Konfusion ═══════════════════════
        GenerateArcadeConfusion(result);         // S3: split-merged, merged-nonmerged, CHD, disc-arcade

        // ═══ PHASE S4 EXPANSION — Header-vs-Headerless + Cross-System ════
        GenerateHeaderVsHeaderlessPairs(result); // S4: header/headerless pairs
        GenerateNewCrossSystemPairs(result);     // S4: SAT/DC, PCE/PCECD, NES variants

        // ═══ PHASE S5 EXPANSION — Golden-Core Schwierigkeitsbalance ══════
        GenerateGoldenCoreHardEntries(result);        // S5: 60 hard entries (Tier-1 + Tier-2)
        GenerateGoldenCoreAdversarialEntries(result);  // S5: 20 adversarial entries

        // ═══ PHASE S6 EXPANSION — Negative Controls + NonGame Upgrade ════
        GenerateNonRomNegativeControls(result);        // S6: 15 non-ROM file types
        GenerateDemoEntries(result);                   // S6: 8 demo entries
        GenerateHackEntries(result);                   // S6: 4 hack entries
        GenerateUtilityEntries(result);                // S6: 3 cheat device entries

        // ═══ PHASE M1 EXPANSION — Computer-Format-Tiefe ═════════════════
        GenerateComputerFormatDepth(result);            // M1: 8 WHDLoad + 21 computer formats
        GenerateKeywordOnlyDetection(result);           // M1: 12 keyword-only entries

        // ═══ PHASE M3 EXPANSION — Region/Revision + Corrupt ═════════════
        GenerateM3RegionVariants(result);               // M3: 30 region variants
        GenerateM3RevisionVariants(result);             // M3: 15 revision variants
        GenerateM3CorruptEntries(result);               // M3: 10 corrupt/truncated entries

        // ═══ PHASE M4 EXPANSION — SNES ROM-Types + Copier-Header ════════
        GenerateSnesRomTypes(result);                   // M4: 6 SNES LoROM/HiROM/ExHiROM entries
        GenerateCopierHeaderEntries(result);            // M4: 8 copier-header ROM entries

        return result;
    }

    // ═══ GENERATION METHODS ═════════════════════════════════════════════

    private void GenerateCleanReferences(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems)
        {
            int count = GetTierCount(sys.Key, 12, 8, 5, 3);
            for (int i = 0; i < count; i++)
            {
                var ext = GetPrimaryExtension(sys);
                var id = NextId("gc", sys.Key, "ref");
                if (_existingIds.Contains(id)) continue;

                var gameName = sys.SampleGames[i % sys.SampleGames.Length];
                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{gameName} (USA){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize + (i * 1024),
                        Directory = sys.FolderAlias,
                    },
                    Tags = BuildTags("clean-reference", sys),
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = GetAlternatives(sys)
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateWrongNameEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems.Where(s => s.UniqueExts.Length > 0))
        {
            var ext = sys.UniqueExts[0];
            var id = NextId("cm", sys.Key, "wrongname");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"unknown_game_2024{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["wrong-name", ..BuildTags(null, sys)],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = GetAlternatives(sys)
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateHeaderConflicts(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Systems with cartridge headers where header could conflict with filename
        var headerSystems = Systems.Where(s => s.HasCartridgeHeader).ToArray();
        foreach (var sys in headerSystems)
        {
            // First variant: wrong folder placement
            var ext = GetPrimaryExtension(sys);
            var id = NextId("cm", sys.Key, "hdrconf");
            if (!_existingIds.Contains(id))
            {
                Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"Wrong System Game{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize,
                        Directory = "roms",
                    },
                    Tags = ["header-conflict", "wrong-name"],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 75,
                        HasConflict = true,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "CartridgeHeader",
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }

            // Second variant: misplaced in wrong console folder
            var id2 = NextId("cm", sys.Key, "hdrconf");
            if (!_existingIds.Contains(id2))
            {
                var wrongFolder = sys.Key == "NES" ? "snes" : "nes";
                Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
                {
                    Id = id2,
                    Source = new SourceInfo
                    {
                        FileName = $"{sys.SampleGames[0]}{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize,
                        Directory = wrongFolder,
                    },
                    Tags = ["header-conflict", "folder-header-conflict"],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 70,
                        HasConflict = true,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "CartridgeHeader",
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateWrongExtensions(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems.Where(s => s.UniqueExts.Length > 0))
        {
            var id = NextId("ec", sys.Key, "wrongext");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA).bin",
                    Extension = ".bin",
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["wrong-extension"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 70,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.HasCartridgeHeader ? "CartridgeHeader" : "FolderName",
                    AcceptableAlternatives = sys.HasCartridgeHeader ? ["FolderName"] : []
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateFolderConflicts(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // File in WRONG folder, but with correct extension/header
        foreach (var sys in Systems.Where(s => s.UniqueExts.Length > 0).Take(40))
        {
            var ext = sys.UniqueExts[0];
            var id = NextId("ec", sys.Key, "foldconf");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA){ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "wrong_folder",
                },
                Tags = ["folder-header-conflict"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 75,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.HasCartridgeHeader ? "CartridgeHeader" : "UniqueExtension",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateDatExactEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems)
        {
            int count = GetTierCount(sys.Key, 8, 5, 3, 2);
            for (int i = 0; i < count; i++)
            {
                var ext = GetPrimaryExtension(sys);
                var id = NextId("dc", sys.Key, "exact");
                if (_existingIds.Contains(id)) continue;

                var gameName = sys.SampleGames[i % sys.SampleGames.Length];
                Add(result, "dat-coverage.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{gameName} (USA){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize,
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["dat-exact-match", sys.DatEcosystem],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 98,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "DatMatch",
                        AcceptableAlternatives = [sys.PrimaryDetection]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateDatWeakEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems)
        {
            var ext = GetPrimaryExtension(sys);
            var id = NextId("dc", sys.Key, "weak");
            if (_existingIds.Contains(id)) continue;

            Add(result, "dat-coverage.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"homebrew_game (PD){ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize / 4,
                    Directory = sys.FolderAlias,
                },
                Tags = ["dat-none"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 60,
                    HasConflict = false,
                    DatMatchLevel = "none",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = GetAlternatives(sys)
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Systems that commonly have BIOS files
        string[] biosSystems = [
            "PS1", "PS2", "PS3", "SAT", "DC", "GBA", "NDS", "3DS",
            "NEOCD", "PCECD", "PCFX", "SCD", "CD32", "CDI", "JAGCD",
            "3DO", "GC", "WII", "WIIU", "FMTOWNS"
        ];

        foreach (var key in biosSystems)
        {
            var sys = Systems.FirstOrDefault(s => s.Key == key);
            if (sys is null) continue;

            var ext = GetPrimaryExtension(sys);
            var id = NextId("gc", key, "bios");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"[BIOS] {sys.Key} ({(sys.DiscBased ? "CD" : "System")}).bin",
                    Extension = ".bin",
                    SizeBytes = 524288,
                    Directory = sys.FolderAlias,
                },
                Tags = ["bios", "clean-reference", sys.DatEcosystem],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = key,
                    Category = "Bios",
                    Confidence = 95,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo
                {
                    BiosSystemKeys = [key]
                }
            });
        }

        // Regional BIOS variants for major systems
        foreach (var key in new[] { "PS1", "PS2", "SAT", "DC", "3DO", "GC", "WII", "NDS", "3DS", "PCECD", "PCFX", "SCD", "NEOCD", "CD32", "FMTOWNS" })
        {
            var sys2 = Systems.FirstOrDefault(s => s.Key == key);
            if (sys2 is null) continue;

            var id2 = NextId("gc", key, "biosrgn");
            if (_existingIds.Contains(id2)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id2,
                Source = new SourceInfo
                {
                    FileName = $"[BIOS] {sys2.Key} (Japan).bin",
                    Extension = ".bin",
                    SizeBytes = 524288,
                    Directory = sys2.FolderAlias,
                },
                Tags = ["bios", "clean-reference", sys2.DatEcosystem],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = key,
                    Category = "Bios",
                    Confidence = 95,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys2.DatEcosystem,
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo
                {
                    BiosSystemKeys = [key]
                }
            });
        }

        // Arcade BIOS entries
        foreach (var biosName in new[] { "pgm", "cps2", "cps3", "decocass", "isgsm", "skns", "stvbios" })
        {
            var id = NextId("gc", "ARCADE", $"bios{biosName}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{biosName}.zip",
                    Extension = ".zip",
                    SizeBytes = 131072,
                    Directory = "arcade",
                },
                Tags = ["bios", "arcade-bios", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Bios",
                    Confidence = 92,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo
                {
                    BiosSystemKeys = ["ARCADE"]
                }
            });
        }
    }

    private void GenerateArcadeParentClone(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var arcadeGames = new[]
        {
            ("pacman", "Pac-Man", 49152L),
            ("dkong", "Donkey Kong", 32768L),
            ("galaga", "Galaga", 32768L),
            ("1942", "1942", 65536L),
            ("bublbobl", "Bubble Bobble", 131072L),
            ("tmnt", "TMNT", 524288L),
            ("xmen", "X-Men", 2097152L),
            ("simpsons", "The Simpsons", 2097152L),
            ("mslug2", "Metal Slug 2", 33554432L),
            ("garou", "Garou MOTW", 67108864L),
            ("sf2", "Street Fighter II", 3145728L),
            ("mslug", "Metal Slug", 16777216L),
            ("ddonpach", "DoDonPachi", 8388608L),
            ("mvsc", "Marvel vs Capcom", 33554432L),
            ("kof2002", "KOF 2002", 67108864L),
            ("twinbee", "TwinBee", 131072L),
            ("gradius", "Gradius", 131072L),
            ("outrun", "Out Run", 262144L),
            ("parodius", "Parodius", 524288L),
            ("raiden", "Raiden", 1048576L),
            ("darius", "Darius", 2097152L),
            ("turtles", "Turtles in Time", 2097152L),
            ("punkshot", "Punk Shot", 1048576L),
            ("ssf2t", "Super SF2 Turbo", 4194304L),
            ("msh", "Marvel Super Heroes", 33554432L),
        };

        // Parent sets
        foreach (var (rom, name, size) in arcadeGames)
        {
            var id = NextId("gc", "ARCADE", "parent");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{rom}.zip",
                    Extension = ".zip",
                    SizeBytes = size,
                    Directory = "arcade",
                },
                Tags = ["clean-reference", "parent", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Game",
                    Confidence = 90,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["ArchiveContent", "DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }

        // Clone sets
        var clones = new[] {
            ("sf2ce", "sf2"), ("sf2hf", "sf2"), ("mslug2t", "mslug2"),
            ("pacmanf", "pacman"), ("dkongj", "dkong"),
            ("galagao", "galaga"), ("1942a", "1942"), ("1942b", "1942"),
            ("bublbob1", "bublbobl"), ("tmnt2", "tmnt"),
            ("xmen2p", "xmen"), ("simpsonj", "simpsons"),
            ("garoubl", "garou"), ("mslug2x", "mslug2"),
            ("mslugx", "mslug"), ("ddonpchj", "ddonpach"),
            ("mvscj", "mvsc"), ("kof2002m", "kof2002"),
            ("gradius2", "gradius"), ("outrundx", "outrun"),
            ("raiden2", "raiden"), ("dariusg", "darius"),
            ("mshj", "msh"), ("ssf2ta", "ssf2t"),
            ("sf2rb", "sf2"),
        };

        foreach (var (clone, parent) in clones)
        {
            var id = NextId("gc", "ARCADE", "clone");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{clone}.zip",
                    Extension = ".zip",
                    SizeBytes = 65536,
                    Directory = "arcade",
                },
                Tags = ["clone", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Game",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo { CloneOf = parent }
            });
        }

        // Neo Geo parent sets
        var neogeoGames = new[] {
            ("kof97", 20971520L), ("kof99", 33554432L), ("samsho2", 16777216L),
            ("mslug3", 67108864L), ("mslug4", 67108864L), ("mslug5", 67108864L),
            ("fatfury2", 8388608L), ("rbff1", 16777216L), ("aof3", 33554432L),
            ("matrim", 67108864L), ("lastblad", 33554432L), ("blazstar", 16777216L),
        };

        foreach (var (rom, size) in neogeoGames)
        {
            var id = NextId("gc", "NEOGEO", "parent");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{rom}.zip",
                    Extension = ".zip",
                    SizeBytes = size,
                    Directory = "neogeo",
                },
                Tags = ["clean-reference", "parent", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "NEOGEO",
                    Category = "Game",
                    Confidence = 90,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["ArchiveContent", "DatLookup"],
                    AcceptableConsoleKeys = ["ARCADE"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }

        // Neo Geo clone sets
        var neogeoClones = new[]
        {
            ("kof97a", "kof97"), ("kof99e", "kof99"), ("samsho2k", "samsho2"),
            ("mslug3h", "mslug3"), ("mslug4h", "mslug4"), ("mslug5h", "mslug5"),
            ("fatfury2a", "fatfury2"), ("rbff1a", "rbff1"), ("aof3k", "aof3"),
            ("matrimbl", "matrim"),
        };

        foreach (var (clone, parent) in neogeoClones)
        {
            var id = NextId("gc", "NEOGEO", "clone");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{clone}.zip",
                    Extension = ".zip",
                    SizeBytes = 33554432,
                    Directory = "neogeo",
                },
                Tags = ["clone", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "NEOGEO",
                    Category = "Game",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"],
                    AcceptableConsoleKeys = ["ARCADE"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo { CloneOf = parent }
            });
        }

        // Arcade split/merged/nonmerged variants
        foreach (var variant in new[] { "arcade-split", "arcade-merged", "arcade-non-merged" })
        {
            for (int i = 0; i < 15; i++)
            {
                var id = NextId("gc", "ARCADE", variant.Replace("arcade-", ""));
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"game_{variant}_{i:D2}.zip",
                        Extension = ".zip",
                        SizeBytes = 1048576 * (i + 1),
                        Directory = "arcade",
                    },
                    Tags = ["parent", variant, "dat-mame"],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = "ARCADE",
                        Category = "Game",
                        Confidence = 88,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = "mame",
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "FolderName",
                        AcceptableAlternatives = ["DatLookup"]
                    },
                    FileModel = new FileModelInfo { Type = "archive" },
                    Relationships = new RelationshipInfo()
                });
            }
        }

        // Arcade CHD supplement
        foreach (var chdGame in new[] { "area51", "kinst", "kinst2", "sfiii3", "mvsc2", "capvssnk", "sfex", "tekken3" })
        {
            var id = NextId("gc", "ARCADE", "chd");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{chdGame}.chd",
                    Extension = ".chd",
                    SizeBytes = 734003200,
                    Directory = $"arcade/{chdGame}",
                },
                Tags = ["arcade-chd", "dat-mame"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Game",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateMultiDiscEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var discSystems = Systems.Where(s => s.DiscBased).ToArray();
        foreach (var sys in discSystems)
        {
            var id = NextId("gr", sys.Key, "mdisc");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA) (Disc 1).chd",
                    Extension = ".chd",
                    SizeBytes = 734003200,
                    Directory = sys.FolderAlias,
                    InnerFiles = null,
                },
                Tags = ["multi-disc", "clean-reference", sys.DatEcosystem],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 92,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "multi-disc", DiscCount = 2 },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateMultiFileEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var discSystems = Systems.Where(s => s.DiscBased).ToArray();
        foreach (var sys in discSystems)
        {
            // CUE+BIN variant
            var id = NextId("gr", sys.Key, "mfile");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA).cue",
                    Extension = ".cue",
                    SizeBytes = 256,
                    Directory = sys.FolderAlias,
                    InnerFiles =
                    [
                        new InnerFileInfo { Name = $"{sys.SampleGames[0]} (USA) (Track 1).bin", SizeBytes = 734003200 },
                        new InnerFileInfo { Name = $"{sys.SampleGames[0]} (USA) (Track 2).bin", SizeBytes = 10240000 },
                    ]
                },
                Tags = ["multi-file", "clean-reference"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 92,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                },
                FileModel = new FileModelInfo
                {
                    Type = "multi-file-set",
                    SetFiles = [$"{sys.SampleGames[0]} (USA).cue", $"{sys.SampleGames[0]} (USA) (Track 1).bin"]
                },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateArchiveInnerEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems.Where(s => !s.DiscBased && s.UniqueExts.Length > 0).Take(35))
        {
            var ext = sys.UniqueExts[0];
            var id = NextId("gr", sys.Key, "arcinner");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA).zip",
                    Extension = ".zip",
                    SizeBytes = sys.TypicalSize / 2,
                    Directory = sys.FolderAlias,
                    InnerFiles =
                    [
                        new InnerFileInfo { Name = $"{sys.SampleGames[0]} (USA){ext}", SizeBytes = sys.TypicalSize }
                    ]
                },
                Tags = ["archive-inner", "clean-reference"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 95,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "ArchiveContent",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateDirectoryEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var dirSystems = new[] { "WIIU", "3DS", "DOS", "SWITCH", "VITA" };
        foreach (var key in dirSystems)
        {
            var sys = Systems.FirstOrDefault(s => s.Key == key);
            if (sys is null) continue;

            for (int i = 0; i < 3; i++)
            {
                var id = NextId("gr", key, "dir");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"game_{key.ToLowerInvariant()}_{i:D2}",
                        Extension = key switch
                        {
                            "WIIU" => ".rpx",
                            "3DS" => ".3ds",
                            "SWITCH" => ".nsp",
                            "VITA" => ".vpk",
                            _ => ".exe"
                        },
                        SizeBytes = 0,
                        Directory = $"{sys.FolderAlias}/game_{i:D2}",
                    },
                    Tags = ["directory-based", "clean-reference"],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = key,
                        Category = "Game",
                        Confidence = 80,
                        HasConflict = false,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "FolderName",
                    },
                    FileModel = new FileModelInfo { Type = "directory" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateUnknownExpected(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files that look like ROMs but can't be identified
        var unknownExts = new[] { ".rom", ".dat", ".img", ".raw", ".dmp", ".dump",
            ".unknown", ".bak", ".old", ".tmp", ".bin", ".data" };

        for (int i = 0; i < unknownExts.Length; i++)
        {
            var id = NextId("nc", "UNK", $"exp{i:D2}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"mystery_file_{i:D3}{unknownExts[i]}",
                    Extension = unknownExts[i],
                    SizeBytes = 65536 * (i + 1),
                    Directory = "unsorted",
                },
                Tags = ["expected-unknown"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Heuristic",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }

        // ROM-like files in non-system folders
        foreach (var sys in Systems.Where(s => s.UniqueExts.Length > 0).Take(18))
        {
            var ext = sys.UniqueExts[0];
            var id = NextId("nc", sys.Key, "expunk");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"empty_file{ext}",
                    Extension = ext,
                    SizeBytes = 0,
                },
                Tags = ["expected-unknown"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Heuristic",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "Empty file with valid ROM extension — should be rejected"
            });
        }
    }

    private void GenerateAmbiguousEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Disc systems that share .iso/.bin/.chd extensions
        var discSystems = Systems.Where(s => s.DiscBased && s.AmbigExts.Length > 0).ToArray();
        foreach (var sys in discSystems)
        {
            var id = NextId("ec", sys.Key, "ambig");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"Game (USA).iso",
                    Extension = ".iso",
                    SizeBytes = 734003200,
                    Directory = "roms", // Neutral folder
                },
                Tags = ["ambiguous"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 50,
                    HasConflict = true,
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader",
                    AcceptableAlternatives = ["Heuristic"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateNegativeControls(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var negatives = new[]
        {
            (".doc",   "document.doc",      102400L,   "D0CF11E0"),
            (".mp3",   "music.mp3",         5242880L,  "494433"),
            (".avi",   "video.avi",          10485760L, "52494646"),
            (".png",   "image.png",         65536L,    "89504E47"),
            (".gif",   "animation.gif",     32768L,    "47494638"),
            (".bmp",   "picture.bmp",       786432L,   "424D"),
            (".html",  "readme.html",       8192L,     null as string),
            (".xml",   "config.xml",        4096L,     null),
            (".csv",   "database.csv",      16384L,    null),
            (".log",   "debug.log",         32768L,    null),
            (".ini",   "settings.ini",      1024L,     null),
            (".bat",   "autorun.bat",       512L,      null),
            (".ps1",   "script.ps1",        2048L,     null),
            (".json",  "metadata.json",     4096L,     null),
            (".nfo",   "release.nfo",       8192L,     null),
            (".sfv",   "verify.sfv",        1024L,     null),
            (".torrent","download.torrent",  16384L,    null),
            (".url",   "website.url",        256L,      null),
            (".lnk",   "shortcut.lnk",      1024L,     null),
            (".dll",   "library.dll",        65536L,    "4D5A"),
            (".sys",   "driver.sys",         32768L,    "4D5A"),
            (".msi",   "installer.msi",      2097152L,  "D0CF11E0"),
            (".dmg",   "macos.dmg",          4194304L,  null),
            (".apk",   "android.apk",        8388608L,  "504B0304"),
            (".ipa",   "ios.ipa",            16777216L, "504B0304"),
            (".pdf",   "manual.pdf",         1048576L,  "25504446"),
            (".xlsx",  "spreadsheet.xlsx",   524288L,   "504B0304"),
            (".pptx",  "slides.pptx",        2097152L,  "504B0304"),
            (".ogg",   "soundtrack.ogg",     3145728L,  "4F676753"),
            (".flac",  "lossless.flac",      10485760L, "664C6143"),
            (".wav",   "sample.wav",         4194304L,  "52494646"),
            (".ttf",   "font.ttf",           131072L,   "00010000"),
        };

        for (int i = 0; i < negatives.Length; i++)
        {
            var (ext, name, size, _) = negatives[i];
            var id = NextId("nc", "NONE", $"neg{i:D2}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name,
                    Extension = ext,
                    SizeBytes = size,
                },
                Tags = ["negative-control"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateRepairBlocked(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // For common systems, generate a sort-blocked entry
        foreach (var sys in Systems)
        {
            var ext = GetPrimaryExtension(sys);

            // Low confidence → block
            var id = NextId("rs", sys.Key, "lowconf");
            if (!_existingIds.Contains(id))
            {
                Add(result, "repair-safety.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"maybe_{sys.Key.ToLowerInvariant()}_game{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize / 2,
                        Directory = "unsorted",
                    },
                    Tags = ["sort-blocked", "repair-safety"],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 40,
                        HasConflict = false,
                        SortDecision = "block"
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo(),
                    Notes = "Low confidence — system should block automatic sorting"
                });
            }
        }
    }

    private void GenerateCrossSystem(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var pairs = new[]
        {
            ("PS1", "PS2", "disc"), ("PS2", "PS3", "disc"),
            ("GB", "GBC", "cart"), ("MD", "32X", "cart"),
            ("ARCADE", "NEOGEO", "arcade"), ("NEOCD", "NEOGEO", "disc-arcade"),
            ("PCE", "PCECD", "cart-disc"), ("GC", "WII", "disc"),
            ("NDS", "3DS", "cart"), ("SMS", "GG", "cart"),
            ("SG1000", "SMS", "cart"), ("GB", "GBA", "cart"),
            ("WII", "WIIU", "disc"),
        };

        foreach (var (sysA, sysB, type) in pairs)
        {
            var defA = Systems.First(s => s.Key == sysA);

            // File that is system A but could be confused for system B
            var id = NextId("ec", sysA, $"xs{sysB.ToLowerInvariant()}");
            if (_existingIds.Contains(id)) continue;

            var ext = GetPrimaryExtension(defA);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"CrossTest ({sysA} vs {sysB}){ext}",
                    Extension = ext,
                    SizeBytes = defA.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["cross-system"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysA,
                    Category = "Game",
                    Confidence = 75,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = defA.PrimaryDetection,
                    AcceptableConsoleKeys = [sysB]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });

            // Reverse direction
            var defB = Systems.First(s => s.Key == sysB);
            var id2 = NextId("ec", sysB, $"xs{sysA.ToLowerInvariant()}");
            if (_existingIds.Contains(id2)) continue;

            var ext2 = GetPrimaryExtension(defB);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id2,
                Source = new SourceInfo
                {
                    FileName = $"CrossTest ({sysB} vs {sysA}){ext2}",
                    Extension = ext2,
                    SizeBytes = defB.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["cross-system"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysB,
                    Category = "Game",
                    Confidence = 75,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = defB.PrimaryDetection,
                    AcceptableConsoleKeys = [sysA]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateJunkEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var junkTags = new[] {
            ("Demo", "demo"), ("Beta", "beta"), ("Proto", "prototype"),
            ("Sample", "sample"), ("Hack", "hack"),
        };

        foreach (var sys in Systems)
        {
            var ext = GetPrimaryExtension(sys);

            foreach (var (label, tag) in junkTags.Take(GetTierCount(sys.Key, 5, 3, 2, 1)))
            {
                var id = NextId("gr", sys.Key, $"junk{tag}");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"Game ({label}) (USA){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize / 4,
                        Directory = sys.FolderAlias,
                    },
                    Tags = [tag == "hack" ? "non-game" : "junk"],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = tag == "hack" ? "NonGame" : "Junk",
                        Confidence = 90,
                        HasConflict = false,
                        SortDecision = "sort"
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateBrokenEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        foreach (var sys in Systems.Where(s => s.UniqueExts.Length > 0).Take(35))
        {
            var ext = sys.UniqueExts[0];

            // Truncated
            var id = NextId("cm", sys.Key, "trunc");
            if (!_existingIds.Contains(id))
            {
                Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"broken_rom{ext}",
                        Extension = ext,
                        SizeBytes = 16,
                    },
                    Tags = ["truncated"],
                    Difficulty = "adversarial",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = null,
                        Category = "Unknown",
                        Confidence = 0,
                        HasConflict = false,
                        SortDecision = "skip"
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }

            // Corrupt (valid header but garbage body)
            var id2 = NextId("cm", sys.Key, "corrupt");
            if (!_existingIds.Contains(id2))
            {
                Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
                {
                    Id = id2,
                    Source = new SourceInfo
                    {
                        FileName = $"corrupt_game{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize,
                    },
                    Tags = ["corrupt"],
                    Difficulty = "adversarial",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 50,
                        HasConflict = false,
                        SortDecision = "block"
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GeneratePsDisambiguation(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // PS1 ↔ PS2 ↔ PS3 disambiguation entries
        string[] psSystems = ["PS1", "PS2", "PS3"];
        var psGames = new Dictionary<string, string[]>
        {
            ["PS1"] = ["Crash Bandicoot", "Spyro", "Tekken 3", "Resident Evil", "Tomb Raider",
                        "Silent Hill", "Metal Gear Solid", "Parasite Eve", "Vagrant Story", "Dino Crisis"],
            ["PS2"] = ["God of War", "Shadow of Colossus", "Okami", "Persona 4", "DMC 3",
                        "Ratchet Clank", "Jak and Daxter", "Ico", "Gran Turismo 4", "Metal Gear 3"],
            ["PS3"] = ["Uncharted 2", "Last of Us", "Demon Souls", "LBP", "MGS4",
                        "Infamous", "Heavy Rain", "GT5", "Killzone 2", "Resistance"],
        };

        foreach (var sys in psSystems)
        {
            var games = psGames[sys];
            for (int i = 0; i < games.Length; i++)
            {
                var id = NextId("ec", sys, "psdis");
                if (_existingIds.Contains(id)) continue;

                var otherSys = sys == "PS1" ? "PS2" : sys == "PS2" ? "PS3" : "PS1";
                Add(result, "edge-cases.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{games[i]} (USA).iso",
                        Extension = ".iso",
                        SizeBytes = sys == "PS1" ? 734003200 : sys == "PS2" ? 4700000000 : 25769803776,
                        Directory = "roms",
                    },
                    Tags = ["cross-system", "ps-disambiguation"],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys,
                        Category = "Game",
                        Confidence = 80,
                        HasConflict = true,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "DiscHeader",
                        AcceptableConsoleKeys = [otherSys]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateHeaderlessEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Cartridge systems with headers — generate headerless variants
        var headerSystems = Systems.Where(s => s.HasCartridgeHeader).ToArray();
        foreach (var sys in headerSystems)
        {
            var ext = GetPrimaryExtension(sys);
            for (int i = 0; i < 5; i++)
            {
                var id = NextId("gr", sys.Key, "hless");
                if (_existingIds.Contains(id)) continue;

                var gameName = sys.SampleGames[i % sys.SampleGames.Length];
                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{gameName} (USA){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize - 16,
                        Directory = sys.FolderAlias,
                        Stub = new StubInfo { Generator = "generic-headerless", Variant = "no-header" }
                    },
                    Tags = ["headerless", "clean-reference"],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 80,
                        HasConflict = false,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "UniqueExtension",
                        AcceptableAlternatives = ["FolderName", "DatLookup"]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateChdRawSha1Entries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Disc-based systems with CHD files using raw SHA1
        var chdSystems = Systems.Where(s => s.DiscBased).Take(10).ToArray();
        foreach (var sys in chdSystems)
        {
            var id = NextId("gr", sys.Key, "chdsha");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA).chd",
                    Extension = ".chd",
                    SizeBytes = 734003200,
                    Directory = sys.FolderAlias,
                },
                Tags = ["chd-raw-sha1", "clean-reference", sys.DatEcosystem],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 92,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DatMatch",
                    AcceptableAlternatives = ["DiscHeader"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateExtraArcadeEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Additional arcade entries: dat-exact, wrong-name, junk for ARCADE/NEOGEO
        var arcadeKeys = new[] { "ARCADE", "NEOGEO" };
        var extraGames = new[]
        {
            "qbert", "frogger", "asteroids", "centipede", "defender",
            "robotron", "joust", "tempest", "digdug", "mappy",
            "rallyx", "bosconian", "xevious", "starforce", "gunsmoke",
        };

        foreach (var key in arcadeKeys)
        {
            var sys = Systems.First(s => s.Key == key);
            // Extra dat-exact entries
            for (int i = 0; i < 8; i++)
            {
                var game = extraGames[i % extraGames.Length];
                var id = NextId("dc", key, "datex");
                if (_existingIds.Contains(id)) continue;

                Add(result, "dat-coverage.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{game}_{key.ToLowerInvariant()}_{i}.zip",
                        Extension = ".zip",
                        SizeBytes = 65536 * (i + 1),
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["dat-exact-match", "clean-reference", sys.DatEcosystem],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "DatLookup",
                        AcceptableAlternatives = ["FolderName"]
                    },
                    FileModel = new FileModelInfo { Type = "archive" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateExtraComputerEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Computer systems need more entries to hit the 120 hardFail gate
        var computerGames = new Dictionary<string, string[]>
        {
            ["A800"] = ["Star Raiders", "MULE", "Archon", "Rescue on Fractalus"],
            ["AMIGA"] = ["Turrican II", "Lemmings", "Speedball 2", "Sensible Soccer"],
            ["ATARIST"] = ["Dungeon Master", "Starglider", "Stunt Car Racer", "Captive"],
            ["C64"] = ["Impossible Mission", "Maniac Mansion", "Ghosts n Goblins", "Uridium"],
            ["CPC"] = ["Gryzor", "Rick Dangerous", "Renegade", "R-Type CPC"],
            ["DOS"] = ["DOOM", "Commander Keen", "Prince of Persia", "Wolfenstein 3D"],
            ["MSX"] = ["Nemesis", "Metal Gear", "Vampire Killer", "Penguin Adventure"],
            ["PC98"] = ["Ys IV", "Policenauts", "Snatcher", "Eve Burst Error"],
            ["X68K"] = ["Akumajou Dracula", "Gradius", "Star Wars X68K", "Parodius Da"],
            ["ZX"] = ["Manic Miner", "Jet Set Willy", "Dizzy", "Atic Atac"],
        };

        foreach (var (key, games) in computerGames)
        {
            var sys = Systems.FirstOrDefault(s => s.Key == key);
            if (sys is null) continue;
            var ext = GetPrimaryExtension(sys);

            // Extra clean-reference entries
            for (int i = 0; i < 5; i++)
            {
                var id = NextId("gc", key, "compref");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{games[i % games.Length]} (Europe){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize + (i * 2048),
                        Directory = sys.FolderAlias,
                    },
                    Tags = BuildTags("clean-reference", sys),
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = GetAlternatives(sys)
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    // ═══ PHASE A: CHAOS-MIXED EXPANSION (A1) ════════════════════════════

    private void GenerateUnicodeFilenames(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var unicodeNames = new[]
        {
            ("スーパーマリオ", "NES"), ("ソニック・ザ・ヘッジホッグ", "MD"),
            ("파이널 판타지", "SNES"), ("ポケモン 赤", "GB"),
            ("ゼルダの伝説", "N64"), ("메탈기어 솔리드", "PS1"),
            ("鉄拳3", "PS1"), ("ドラゴンクエスト", "SNES"),
            ("バイオハザード", "PS1"), ("ストリートファイターII", "SNES"),
            ("クラッシュ・バンディクー", "PS1"), ("グランツーリスモ", "PS2"),
            ("ファイナルファンタジーX", "PS2"), ("キングダムハーツ", "PS2"),
            ("ロックマン2", "NES"),
        };

        foreach (var (name, sysKey) in unicodeNames)
        {
            var sys = Systems.First(s => s.Key == sysKey);
            var ext = GetPrimaryExtension(sys);
            var id = NextId("cm", sysKey, "unicode");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["wrong-name"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 75,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = GetAlternatives(sys)
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateFalseExtensionChaos(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // ROMs with misleading generic extensions
        var falseExts = new[] { ".rom", ".game", ".rip", ".dump", ".backup", ".image",
                                ".file", ".dat", ".raw", ".old", ".bak", ".original",
                                ".copy", ".save", ".tmp" };

        foreach (var sys in Systems.Where(s => s.HasCartridgeHeader).Take(15))
        {
            var idx = Array.IndexOf(Systems.Where(s => s.HasCartridgeHeader).ToArray(), sys);
            var falseExt = falseExts[idx % falseExts.Length];
            var id = NextId("cm", sys.Key, "falseext");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"game_{sys.Key.ToLowerInvariant()}{falseExt}",
                    Extension = falseExt,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["wrong-extension", "wrong-name"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 65,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateCorruptArchiveEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var corruptTypes = new[]
        {
            ("truncated_archive.zip", ".zip", 128L, "truncated ZIP header"),
            ("empty_archive.zip", ".zip", 22L, "empty ZIP (EOD only)"),
            ("wrong_magic.zip", ".zip", 4096L, "invalid ZIP magic bytes"),
            ("corrupt_7z.7z", ".7z", 256L, "truncated 7z header"),
            ("nested_broken.zip", ".zip", 512L, "corrupt inner entry"),
            ("password_protected.zip", ".zip", 8192L, "encrypted ZIP"),
            ("bomb_archive.zip", ".zip", 64L, "potential zip bomb header"),
            ("split_part.z01", ".z01", 1024L, "orphaned split archive part"),
            ("rar_as_zip.zip", ".zip", 4096L, "RAR content with ZIP extension"),
            ("zero_byte.7z", ".7z", 0L, "zero-byte 7z file"),
        };

        for (int i = 0; i < corruptTypes.Length; i++)
        {
            var (name, ext, size, note) = corruptTypes[i];
            var id = NextId("cm", "NONE", $"corrarc{i:D2}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name,
                    Extension = ext,
                    SizeBytes = size,
                },
                Tags = ["corrupt-archive"],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo(),
                Notes = note
            });
        }
    }

    private void GenerateMixedArchiveChaos(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Archives containing ROMs from multiple systems
        var mixPairs = new[]
        {
            ("NES", "SNES"), ("GB", "GBA"), ("MD", "SNES"),
            ("PS1", "PS2"), ("NES", "GBA"), ("SNES", "GBA"),
            ("N64", "GBA"), ("SMS", "GG"),
        };

        for (int i = 0; i < mixPairs.Length; i++)
        {
            var (sysA, sysB) = mixPairs[i];
            var defA = Systems.First(s => s.Key == sysA);
            var defB = Systems.First(s => s.Key == sysB);
            var id = NextId("cm", sysA, $"mixarc{sysB.ToLowerInvariant()}");
            if (_existingIds.Contains(id)) continue;

            var extA = GetPrimaryExtension(defA);
            var extB = GetPrimaryExtension(defB);
            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"mixed_roms_{sysA.ToLowerInvariant()}_{sysB.ToLowerInvariant()}.zip",
                    Extension = ".zip",
                    SizeBytes = defA.TypicalSize + defB.TypicalSize,
                    Directory = "roms",
                    InnerFiles =
                    [
                        new InnerFileInfo { Name = $"game{extA}", SizeBytes = defA.TypicalSize },
                        new InnerFileInfo { Name = $"game{extB}", SizeBytes = defB.TypicalSize },
                    ]
                },
                Tags = ["archive-inner", "cross-system"],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysA,
                    Category = "Game",
                    Confidence = 50,
                    HasConflict = true,
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "ArchiveContent",
                    AcceptableConsoleKeys = [sysB]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateFlatFolderChaos(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // ROMs dumped into a flat "roms" folder with no system hint
        foreach (var sys in Systems.Where(s => s.HasCartridgeHeader).Take(15))
        {
            var ext = GetPrimaryExtension(sys);
            var id = NextId("cm", sys.Key, "flatfld");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"game_{sys.SampleGames[0].Replace(" ", "_").ToLowerInvariant()}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["wrong-name"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 70,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.HasCartridgeHeader ? "CartridgeHeader" : "UniqueExtension",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateDuplicateNameVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var variants = new[]
        {
            ("Super Mario Bros (U) [!]", "NES"), ("super_mario_bros.NES", "NES"),
            ("ZELDA (1)", "NES"), ("zelda_v2", "NES"),
            ("sonic (genesis)", "MD"), ("Sonic.The" + ".Hedgehog", "MD"),
            ("Tetris (W) [T-Eng]", "GB"), ("TETRIS_GB", "GB"),
            ("pokemon red (UE) [S]", "GB"), ("pokemon_rouge", "GB"),
        };

        foreach (var (name, sysKey) in variants)
        {
            var sys = Systems.First(s => s.Key == sysKey);
            var ext = GetPrimaryExtension(sys);
            var id = NextId("cm", sysKey, "dupnam");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["wrong-name"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = GetAlternatives(sys)
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateRenamedExtensions(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files renamed with random/wrong extensions
        foreach (var sys in Systems.Where(s => s.HasCartridgeHeader))
        {
            var id = NextId("cm", sys.Key, "renext");
            if (_existingIds.Contains(id)) continue;

            // ROM file with a completely wrong extension
            var wrongExt = sys.Key switch
            {
                "NES" => ".sfc",
                "SNES" => ".nes",
                "N64" => ".gba",
                "GBA" => ".n64",
                "GB" => ".gbc",
                "GBC" => ".gb",
                "MD" => ".sfc",
                _ => ".rom"
            };

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]}{wrongExt}",
                    Extension = wrongExt,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["wrong-extension", "header-conflict"],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 65,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = $"Header says {sys.Key} but extension says another system"
            });
        }
    }

    // ═══ PHASE A: GOLDEN-REALWORLD EXPANSION (A2) ═══════════════════════

    private void GenerateRegionVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        string[] regions = ["(USA)", "(Europe)", "(Japan)", "(World)", "(USA, Europe)",
                           "(Japan, USA)", "(Europe, Australia)", "(Korea)"];

        foreach (var sys in Systems.Where(s => IsTier1Or2(s.Key)))
        {
            var ext = GetPrimaryExtension(sys);
            foreach (var region in regions.Take(GetTierCount(sys.Key, 4, 2, 1, 1)))
            {
                var id = NextId("gr", sys.Key, "rgn");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{sys.SampleGames[0]} {region}{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize,
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["clean-reference", "region-variant"],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = GetAlternatives(sys)
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateRevisionVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        string[] revisions = ["(Rev A)", "(Rev B)", "(Rev 1)", "(v1.1)", "(v1.0)", "(v2.0)"];

        foreach (var sys in Systems.Where(s => Tier1.Contains(s.Key)))
        {
            var ext = GetPrimaryExtension(sys);
            foreach (var rev in revisions.Take(3))
            {
                var id = NextId("gr", sys.Key, "rev");
                if (_existingIds.Contains(id)) continue;

                var gameName = sys.SampleGames[1 % sys.SampleGames.Length];
                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{gameName} (USA) {rev}{ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize + 512,
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["clean-reference", "revision-variant"],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = GetAlternatives(sys)
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateNoIntroNaming(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // No-Intro naming convention: "Title (Region) (Languages) (Rev X).ext"
        var noIntroEntries = new[]
        {
            ("Super Mario Bros 3 (USA) (En,Fr,De)", "NES"),
            ("Legend of Zelda, The - A Link to the Past (USA) (En,Fr,Es)", "SNES"),
            ("Pokemon - Emerald Version (USA, Europe) (En,Fr,De,Es,It)", "GBA"),
            ("Metroid - Zero Mission (USA)", "GBA"),
            ("Castlevania - Circle of the Moon (USA, Europe) (En,Fr,De)", "GBA"),
            ("Super Mario 64 (USA) (Rev 2)", "N64"),
            ("Mario Kart 64 (USA) (Rev 1)", "N64"),
            ("Sonic The Hedgehog (USA, Europe)", "MD"),
            ("Streets of Rage 2 (USA)", "MD"),
            ("Tetris (World) (Rev 1)", "GB"),
        };

        foreach (var (name, sysKey) in noIntroEntries)
        {
            var sys = Systems.First(s => s.Key == sysKey);
            var ext = GetPrimaryExtension(sys);
            var id = NextId("gr", sysKey, "nointro");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["clean-reference", "dat-nointro"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 98,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "no-intro",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateRedumpNaming(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Redump naming convention for disc systems
        var redumpEntries = new[]
        {
            ("Final Fantasy VII (USA) (Disc 1)", "PS1"),
            ("Resident Evil 2 (USA) (Disc 1)", "PS1"),
            ("Metal Gear Solid (USA) (Disc 1) (Rev 1)", "PS1"),
            ("Gran Turismo 4 (USA)", "PS2"),
            ("Kingdom Hearts II (USA) (En,Fr)", "PS2"),
            ("Shenmue (USA) (Disc 1 of 3)", "DC"),
            ("Panzer Dragoon Saga (USA) (Disc 1)", "SAT"),
            ("Metroid Prime (USA) (Rev 2)", "GC"),
            ("Super Smash Bros. Melee (USA) (Rev 2)", "GC"),
            ("Mario Galaxy (USA)", "WII"),
        };

        foreach (var (name, sysKey) in redumpEntries)
        {
            var sys = Systems.First(s => s.Key == sysKey);
            var ext = sys.Key == "DC" ? ".gdi" : ".chd";
            var id = NextId("gr", sysKey, "redump");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = 734003200,
                    Directory = sys.FolderAlias,
                },
                Tags = ["clean-reference", "dat-redump"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 98,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "redump",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateFolderSortedCollection(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files correctly sorted into system folders with clean names
        foreach (var sys in Systems.Where(s => s.SampleGames.Length >= 3 && IsTier1Or2(s.Key)))
        {
            var ext = GetPrimaryExtension(sys);
            for (int i = 2; i < Math.Min(sys.SampleGames.Length, 4); i++)
            {
                var id = NextId("gr", sys.Key, "fldsrt");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{sys.SampleGames[i]} (USA){ext}",
                        Extension = ext,
                        SizeBytes = sys.TypicalSize + (i * 2048),
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["clean-reference"],
                    Difficulty = "easy",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 95,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = GetAlternatives(sys)
                    },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    // ═══ PHASE A: EDGE-CASES EXPANSION (A3) ═════════════════════════════

    private void GenerateGbGbcAmbiguity(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // GB/GBC CGB flag dual-mode ROMs
        var gbGbcEntries = new[]
        {
            ("Pokemon Yellow (USA) (CGB+DMG)", ".gb", "GB", 0x80, "CGB dual-mode (0x80)"),
            ("Links Awakening DX (USA) (CGB)", ".gbc", "GBC", 0xC0, "CGB-only (0xC0)"),
            ("Tetris (World) (DMG)", ".gb", "GB", 0x00, "DMG-only (0x00)"),
            ("Pokemon Crystal (USA) (CGB)", ".gbc", "GBC", 0xC0, "CGB-only (0xC0)"),
            ("Harvest Moon GBC (USA) (CGB+DMG)", ".gb", "GB", 0x80, "dual-mode tagged as GB"),
            ("Wario Land 3 (USA) (CGB+DMG)", ".gbc", "GBC", 0x80, "dual-mode tagged as GBC"),
            ("Mario Tennis (USA) (CGB)", ".gbc", "GBC", 0xC0, "CGB-only"),
            ("Dragon Warrior (USA) (DMG+CGB)", ".gb", "GB", 0x80, "dual-mode old naming"),
            ("Mega Man Xtreme (USA) (CGB)", ".gbc", "GBC", 0xC0, "CGB-only"),
            ("Bionic Commando (USA) (DMG)", ".gb", "GB", 0x00, "pure DMG"),
        };

        for (int i = 0; i < gbGbcEntries.Length; i++)
        {
            var (name, ext, expectedSys, cgbFlag, note) = gbGbcEntries[i];
            var id = NextId("ec", expectedSys, "gbcambig");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == expectedSys);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "gameboy",
                    Stub = new StubInfo
                    {
                        Generator = "gb-header",
                        Variant = cgbFlag == 0xC0 ? "cgb-only" : cgbFlag == 0x80 ? "cgb-dual" : "dmg",
                    }
                },
                Tags = ["cross-system", "gb-gbc-ambiguity"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = expectedSys,
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = cgbFlag == 0x80,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader",
                    AcceptableConsoleKeys = expectedSys == "GB" ? ["GBC"] : ["GB"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = note
            });
        }
    }

    private void GenerateMd32xAmbiguity(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var md32xEntries = new[]
        {
            ("Doom (32X)", "32X", ".32x"), ("Knuckles Chaotix (32X)", "32X", ".32x"),
            ("Star Wars Arcade (32X)", "32X", ".32x"), ("Virtua Racing Deluxe (32X)", "32X", ".32x"),
            ("Sonic 3 mislabeled as 32X", "MD", ".bin"), ("Streets of Rage 32X hack", "MD", ".bin"),
            ("Pitfall 32X (USA)", "32X", ".bin"), ("Cosmic Carnage (USA)", "32X", ".bin"),
        };

        for (int i = 0; i < md32xEntries.Length; i++)
        {
            var (name, expectedSys, ext) = md32xEntries[i];
            var id = NextId("ec", expectedSys, "md32x");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == expectedSys);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["cross-system", "md-32x-ambiguity"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = expectedSys,
                    Category = "Game",
                    Confidence = 70,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = expectedSys == "32X" ? "UniqueExtension" : "CartridgeHeader",
                    AcceptableConsoleKeys = expectedSys == "32X" ? ["MD"] : ["32X"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosEdgeCases(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files that look like BIOS but are games, or BIOS-like files in unexpected places
        var biosEdges = new[]
        {
            ("BIOS Boot Disc (USA)", "PS1", "Game", false, "Game named like BIOS"),
            ("[BIOS] System Launcher (USA)", "PS2", "Bios", true, "Actual BIOS"),
            ("bios_test_rom (PD)", "NES", "Game", false, "Homebrew with BIOS in name"),
            ("SCPH-1001.BIN (unknown)", "PS1", "Bios", true, "BIOS with serial-like name"),
            ("System Card (USA)", "PCECD", "Bios", true, "System Card BIOS"),
            ("gba_bios.bin (mislabeled)", "GBA", "Bios", true, "BIOS without [BIOS] tag"),
            ("Boot ROM (Japan)", "GBC", "Bios", true, "Boot ROM"),
            ("[BIOS] NeoGeo (World)", "NEOGEO", "Bios", true, "Neo Geo shared BIOS"),
            ("firmware_update (USA)", "3DS", "Bios", true, "Firmware as BIOS"),
            ("System Menu v4.3 (USA)", "WII", "Bios", true, "Wii System Menu"),
        };

        for (int i = 0; i < biosEdges.Length; i++)
        {
            var (name, sysKey, category, isBios, note) = biosEdges[i];
            var sys = Systems.First(s => s.Key == sysKey);
            var id = NextId("ec", sysKey, "biosedge");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}.bin",
                    Extension = ".bin",
                    SizeBytes = 524288,
                    Directory = sys.FolderAlias,
                },
                Tags = isBios ? ["bios", "bios-edge"] : ["bios-negative"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = category,
                    Confidence = isBios ? 90 : 70,
                    HasConflict = !isBios,
                    SortDecision = isBios ? "block" : "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = isBios ? new RelationshipInfo { BiosSystemKeys = [sysKey] } : new RelationshipInfo(),
                Notes = note
            });
        }
    }

    private void GenerateDatCollisions(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files that match DATs from multiple ecosystems
        var collisions = new[]
        {
            ("NES", "no-intro", "tosec", "Pac-Man (USA) (TOSEC)", ".nes"),
            ("SNES", "no-intro", "tosec", "Tetris Attack (USA) (TOSEC variant)", ".sfc"),
            ("MD", "no-intro", "tosec", "Columns (USA) (TOSEC-named)", ".md"),
            ("GB", "no-intro", "tosec", "Dr. Mario (World) (TOSEC)", ".gb"),
            ("PS1", "redump", "tosec", "Ridge Racer (USA) (TOSEC)", ".bin"),
            ("SAT", "redump", "tosec", "Virtua Fighter 2 (USA) (TOSEC)", ".bin"),
            ("AMIGA", "tosec", "no-intro", "Lemmings (TOSEC) (Amiga)", ".adf"),
            ("C64", "tosec", "no-intro", "Boulder Dash (TOSEC) (C64)", ".d64"),
        };

        for (int i = 0; i < collisions.Length; i++)
        {
            var (sysKey, eco1, eco2, name, ext) = collisions[i];
            var id = NextId("ec", sysKey, "datcoll");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["dat-exact-match", "cross-system"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 85,
                    HasConflict = true,
                    DatMatchLevel = "exact",
                    DatEcosystem = eco1,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DatLookup",
                    AcceptableAlternatives = [sys.PrimaryDetection]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = $"DAT match in both {eco1} and {eco2}"
            });
        }
    }

    private void GenerateAdditionalPsDisambiguation(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Additional PS disambiguation entries for PSP and PS3
        var pspGames = new[] { "Crisis Core", "Dissidia", "Monster Hunter FU", "Patapon", "LocoRoco" };
        foreach (var game in pspGames)
        {
            var id = NextId("ec", "PSP", "psdis");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game} (USA).iso",
                    Extension = ".iso",
                    SizeBytes = 1800000000,
                    Directory = "roms",
                },
                Tags = ["cross-system", "ps-disambiguation"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "PSP",
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = true,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader",
                    AcceptableConsoleKeys = ["PS2"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE A: NEGATIVE-CONTROLS EXPANSION (A4) ══════════════════════

    private void GenerateFastRomNegatives(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Random bytes with ROM extension but not real ROMs
        var fastRomNegs = new[]
        {
            (".nes", "NES", 40976L), (".sfc", "SNES", 524288L),
            (".gba", "GBA", 16777216L), (".n64", "N64", 8388608L),
            (".gb", "GB", 32768L), (".md", "MD", 1048576L),
        };

        for (int i = 0; i < fastRomNegs.Length; i++)
        {
            var (ext, _, size) = fastRomNegs[i];
            var id = NextId("nc", "NONE", $"fastrom{i:D2}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"random_data_{i:D2}{ext}",
                    Extension = ext,
                    SizeBytes = size,
                    Stub = new StubInfo { Generator = "random-bytes", Params = new Dictionary<string, object> { ["seed"] = 42 + i } }
                },
                Tags = ["negative-control"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "Random bytes with valid ROM extension — header check should reject"
            });
        }
    }

    private void GenerateHomebrewNegatives(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Homebrew/hack files that should be classified as NonGame
        var homebrewNegs = new[]
        {
            ("Snake (Homebrew) (PD)", "NES", ".nes"), ("Flappy Bird (Hack)", "GB", ".gb"),
            ("Test ROM (PD)", "SNES", ".sfc"), ("Memory Test (PD)", "GBA", ".gba"),
            ("Color Test (PD)", "MD", ".md"), ("Sound Test ROM (PD)", "N64", ".n64"),
        };

        for (int i = 0; i < homebrewNegs.Length; i++)
        {
            var (name, sysKey, ext) = homebrewNegs[i];
            var id = NextId("nc", sysKey, "homebrew");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize / 8,
                    Directory = sys.FolderAlias,
                },
                Tags = ["negative-control", "homebrew"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "NonGame",
                    Confidence = 85,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.PrimaryDetection,
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateIrrelevantNegatives(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files with misleading names that aren't ROMs
        var irrelevants = new[]
        {
            ("Super Mario Bros.txt", ".txt", 1024L, "Text file with ROM-like name"),
            ("Zelda Guide.pdf", ".pdf", 2097152L, "PDF with game name"),
            ("Sonic Screenshot.png", ".png", 65536L, "Screenshot named after game"),
            ("Pokemon Save.sav", ".sav", 32768L, "Save file"),
            ("ROM Collection.xlsx", ".xlsx", 524288L, "Spreadsheet about ROMs"),
            ("NES Emulator.exe", ".exe", 4194304L, "Emulator binary"),
        };

        for (int i = 0; i < irrelevants.Length; i++)
        {
            var (name, ext, size, note) = irrelevants[i];
            var id = NextId("nc", "NONE", $"irrel{i:D2}");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name,
                    Extension = ext,
                    SizeBytes = size,
                },
                Tags = ["negative-control"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null,
                    Category = "Unknown",
                    Confidence = 0,
                    HasConflict = false,
                    SortDecision = "skip"
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = note
            });
        }
    }

    // ═══ PHASE A: REPAIR-SAFETY EXPANSION (A5) ══════════════════════════

    private void GenerateRepairHighConfidence(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // High confidence + DAT match = safe for automatic repair/sort
        foreach (var sys in Systems.Where(s => IsTier1Or2(s.Key)).Take(10))
        {
            var ext = GetPrimaryExtension(sys);
            var id = NextId("rs", sys.Key, "hiconf");
            if (_existingIds.Contains(id)) continue;

            Add(result, "repair-safety.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{sys.SampleGames[0]} (USA){ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["repair-safety", "sort-blocked"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 95,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort",
                    RepairSafe = true
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "High confidence + DAT match → safe for automatic sort"
            });
        }
    }

    private void GenerateRepairConflict(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Conflict + DAT match → needs review
        foreach (var sys in Systems.Where(s => s.HasCartridgeHeader).Take(10))
        {
            var ext = GetPrimaryExtension(sys);
            var id = NextId("rs", sys.Key, "conflict");
            if (_existingIds.Contains(id)) continue;

            Add(result, "repair-safety.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"conflicted_{sys.Key.ToLowerInvariant()}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "wrong_folder",
                },
                Tags = ["repair-safety", "sort-blocked", "confidence-borderline"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 65,
                    HasConflict = true,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "review",
                    RepairSafe = false
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "Conflict despite DAT match → needs manual review"
            });
        }
    }

    private void GenerateRepairFolderOnly(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Folder-only detection without header/DAT confirmation → not safe
        string[] folderOnlySystems = ["ARCADE", "NEOGEO", "DOS", "CPC", "PC98", "X68K", "NGPC", "SUPERVISION"];
        foreach (var key in folderOnlySystems)
        {
            var sys = Systems.FirstOrDefault(s => s.Key == key);
            if (sys is null) continue;

            var ext = GetPrimaryExtension(sys);
            var id = NextId("rs", key, "fldonly");
            if (_existingIds.Contains(id)) continue;

            Add(result, "repair-safety.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"unknown_game{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["repair-safety", "sort-blocked", "folder-only-detection", "confidence-low"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = key,
                    Category = "Game",
                    Confidence = 45,
                    HasConflict = false,
                    DatMatchLevel = "none",
                    SortDecision = "block",
                    RepairSafe = false
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "Folder-only detection with no DAT match → block"
            });
        }
    }

    private void GenerateRepairWeakMatch(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Weak DAT match → needs review, not safe for auto-sort
        foreach (var sys in Systems.Where(s => IsTier1Or2(s.Key)).Take(7))
        {
            var ext = GetPrimaryExtension(sys);
            var id = NextId("rs", sys.Key, "weaksrt");
            if (_existingIds.Contains(id)) continue;

            Add(result, "repair-safety.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"uncertain_{sys.Key.ToLowerInvariant()}_title{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["repair-safety", "sort-blocked", "confidence-borderline"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Key,
                    Category = "Game",
                    Confidence = 55,
                    HasConflict = false,
                    DatMatchLevel = "weak",
                    SortDecision = "review",
                    RepairSafe = false
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo(),
                Notes = "Weak DAT match → review only, not safe for automatic sorting"
            });
        }
    }

    // ═══ PHASE B: ARCADE DEPTH (B5) ═════════════════════════════════════

    private void GenerateArcadeDepthExpansion(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Additional arcade parents with various boards/manufacturers
        var additionalArcadeGames = new[]
        {
            ("galaga88", "Galaga '88", 262144L), ("bubble_memories", "Bubble Memories", 8388608L),
            ("dodonpachi", "Esp Ra De", 8388608L), ("progear", "Progear", 8388608L),
            ("armed_police_unit", "Armed Police Unit", 4194304L), ("battle_garegga", "Battle Garegga", 4194304L),
            ("esp_rade", "EspRaDe custom", 8388608L), ("mushihimesama", "Mushihimesama", 16777216L),
            ("ibara", "Ibara", 16777216L), ("ketsui", "Ketsui", 16777216L),
            ("deathsmiles", "Deathsmiles", 16777216L), ("akai_katana", "Akai Katana", 16777216L),
            ("gigawing", "Giga Wing", 8388608L), ("mars_matrix", "Mars Matrix", 8388608L),
            ("blazblue", "BlazBlue CT", 67108864L),
        };

        foreach (var (rom, name, size) in additionalArcadeGames)
        {
            var id = NextId("gc", "ARCADE", "depthpar");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{rom}.zip",
                    Extension = ".zip",
                    SizeBytes = size,
                    Directory = "arcade",
                },
                Tags = ["clean-reference", "parent", "dat-mame"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Game",
                    Confidence = 90,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }

        // Additional arcade device sets
        var arcadeDevices = new[] { "namcos2", "taitogn", "stv", "naomi", "naomi2", "atomiswave",
                                    "cps1", "cps2board", "cps3board", "pgm2" };
        foreach (var device in arcadeDevices)
        {
            var id = NextId("gc", "ARCADE", "device");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{device}.zip",
                    Extension = ".zip",
                    SizeBytes = 262144,
                    Directory = "arcade",
                },
                Tags = ["arcade-device", "dat-mame"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Bios",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo
                {
                    BiosSystemKeys = ["ARCADE"]
                }
            });
        }

        // Arcade junk entries (bootlegs, world versions)
        var arcadeJunk = new[] { "sf2mdt", "mslugboot", "kof2001b", "samsho5b", "garouh", "pacmanbl" };
        foreach (var bootleg in arcadeJunk)
        {
            var id = NextId("gc", "ARCADE", "arcjunk");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{bootleg}.zip",
                    Extension = ".zip",
                    SizeBytes = 2097152,
                    Directory = "arcade",
                },
                Tags = ["arcade-junk", "junk", "dat-mame"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE",
                    Category = "Junk",
                    Confidence = 85,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "mame",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DatLookup"]
                },
                FileModel = new FileModelInfo { Type = "archive" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE C: DIRECTORY-BASED GAMES (C3) ════════════════════════════

    private void GenerateDirectoryGameSamples(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // DOS directory games
        var dosGames = new[]
        {
            "DOOM", "KEEN4", "WOLF3D", "DUKE3D", "QUAKE",
            "DESCENT", "XCOM", "DAGGER", "BLOOD", "HERETIC"
        };

        foreach (var game in dosGames)
        {
            var id = NextId("gr", "DOS", "dosdir");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game}.EXE",
                    Extension = ".exe",
                    SizeBytes = 1048576,
                    Directory = $"dos/{game}",
                },
                Tags = ["directory-based", "clean-reference"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "DOS",
                    Category = "Game",
                    Confidence = 75,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                },
                FileModel = new FileModelInfo { Type = "directory" },
                Relationships = new RelationshipInfo()
            });
        }

        // Wii U title-ID structured directories
        var wiiuGames = new[] { "Mario Kart 8", "Splatoon", "Bayonetta 2", "Pikmin 3", "Xenoblade X" };
        foreach (var game in wiiuGames)
        {
            var id = NextId("gr", "WIIU", "wiiudir");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game.Replace(" ", "_")}.rpx",
                    Extension = ".rpx",
                    SizeBytes = 25769803776,
                    Directory = $"wiiu/{game.Replace(" ", "_")}/code",
                },
                Tags = ["directory-based", "clean-reference"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "WIIU",
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "UniqueExtension",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "directory" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE C: TOSEC COVERAGE (C5) ═══════════════════════════════════

    private void GenerateTosecCoverage(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // TOSEC naming convention entries
        var tosecEntries = new[]
        {
            ("Turrican II (1991)(Factor 5)(Disk 1 of 2)[cr Paranoid]", "AMIGA", ".adf"),
            ("Lemmings (1991)(DMA Design)(Disk 1 of 1)", "AMIGA", ".adf"),
            ("Dungeon Master (1987)(FTL Games)(Disk 1 of 2)", "ATARIST", ".st"),
            ("Impossible Mission (1984)(Epyx)(Side A)[a]", "C64", ".d64"),
            ("DOOM (1993)(id Software)(Disk 1 of 5)", "DOS", ".exe"),
            ("Nemesis (1986)(Konami)[a]", "MSX", ".mx1"),
            ("Manic Miner (1983)(Bug-Byte Software)", "ZX", ".tzx"),
            ("Star Raiders (1979)(Atari)(NTSC)", "A800", ".atr"),
            ("Ys IV - The Dawn of Ys (1993)(Hudson Soft)", "PC98", ".exe"),
            ("Akumajou Dracula (1993)(Konami)", "X68K", ".exe"),
            ("Boulder Dash (1984)(First Star Software)", "C64", ".d64"),
            ("Rick Dangerous (1989)(Core Design)", "CPC", ".dsk"),
            ("Moon Patrol (1983)(Atari)", "A52", ".a52"),
            ("Hockey (1977)(Fairchild)", "CHANNELF", ".bin"),
            ("Mine Storm (1982)(GCE)", "VECTREX", ".vec"),
        };

        foreach (var (name, sysKey, ext) in tosecEntries)
        {
            var id = NextId("dc", sysKey, "tosec");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "dat-coverage.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                },
                Tags = ["dat-exact-match", "dat-tosec"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 95,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = "tosec",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DatLookup",
                    AcceptableAlternatives = [sys.PrimaryDetection]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE C: ADDITIONAL EXPANSION (C6) ═════════════════════════════

    private void GenerateAdditionalMultiDiscVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Additional multi-disc entries: Disc 2, Disc 3 variants
        var discSystems = Systems.Where(s => s.DiscBased && IsTier1Or2(s.Key)).ToArray();
        foreach (var sys in discSystems)
        {
            for (int disc = 2; disc <= 3; disc++)
            {
                var id = NextId("gr", sys.Key, $"mdisc{disc}");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{sys.SampleGames[0]} (USA) (Disc {disc}).chd",
                        Extension = ".chd",
                        SizeBytes = 734003200,
                        Directory = sys.FolderAlias,
                    },
                    Tags = ["multi-disc", "clean-reference", sys.DatEcosystem],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Key,
                        Category = "Game",
                        Confidence = 92,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort",
                        DiscNumber = disc
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.PrimaryDetection,
                        AcceptableAlternatives = ["FolderName"]
                    },
                    FileModel = new FileModelInfo { Type = "multi-disc", DiscCount = disc },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateContainerFormats(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Various container formats: CSO, WIA, RVZ, WBFS
        var containers = new[]
        {
            ("God of War Chains of Olympus (USA).cso", ".cso", "PSP", "container-cso"),
            ("Lumines (USA).cso", ".cso", "PSP", "container-cso"),
            ("Mario Galaxy (USA).rvz", ".rvz", "WII", "container-rvz"),
            ("Smash Brawl (USA).rvz", ".rvz", "WII", "container-rvz"),
            ("Melee (USA).rvz", ".rvz", "GC", "container-rvz"),
            ("Twilight Princess Wii (USA).wbfs", ".wbfs", "WII", "container-wbfs"),
            ("Mario Kart Wii (USA).wbfs", ".wbfs", "WII", "container-wbfs"),
        };

        foreach (var (name, ext, sysKey, tag) in containers)
        {
            var id = NextId("gr", sysKey, "container");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name,
                    Extension = ext,
                    SizeBytes = sys.TypicalSize / 2,
                    Directory = sys.FolderAlias,
                },
                Tags = ["clean-reference", tag],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 92,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "UniqueExtension",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateSerialNumberEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files detectable by serial number in filename
        var serials = new[]
        {
            ("SLUS-01163 (USA)", "PS1", ".bin"), ("SLUS-20001 (USA)", "PS2", ".iso"),
            ("BCUS-98100 (USA)", "PS3", ".iso"), ("UCUS-98601 (USA)", "PSP", ".iso"),
            ("NTR-AMCE-USA", "NDS", ".nds"), ("CTR-ARKE-USA", "3DS", ".3ds"),
        };

        foreach (var (serial, sysKey, ext) in serials)
        {
            var id = NextId("gr", sysKey, "serial");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{serial}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["clean-reference", "serial-number"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 85,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "SerialNumber",
                    AcceptableAlternatives = [sys.PrimaryDetection],
                    AcceptableConsoleKeys = sysKey == "PS1" ? ["PS2"] : null
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateKeywordDetection(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Files with console keywords in filename
        var keywordEntries = new[]
        {
            ("Game [PS1]", "PS1", ".bin"), ("Game [GBA]", "GBA", ".gba"),
            ("Game [SNES]", "SNES", ".sfc"), ("Game [N64]", "N64", ".z64"),
            ("Game [NES]", "NES", ".nes"), ("Game [Genesis]", "MD", ".md"),
            ("Game [PS2]", "PS2", ".iso"), ("Game (Game Boy)", "GB", ".gb"),
        };

        foreach (var (name, sysKey, ext) in keywordEntries)
        {
            var id = NextId("gr", sysKey, "keyword");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{name}{ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["clean-reference", "keyword-detection"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 80,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Keyword",
                    AcceptableAlternatives = [sys.PrimaryDetection]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE S1 — Disc-Format-Tiefe ══════════════════════════════════

    private void GenerateDiscFormatEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        GenerateCueBinEntries(result);
        GenerateGdiTracksEntries(result);
        GenerateCcdImgEdgeCases(result);
        GenerateMdsMdfEdgeCases(result);
        GenerateM3uPlaylistEntries(result);
        GenerateExtendedSerialEntries(result);
        GenerateExtendedContainerVariants(result);
    }

    private void GenerateCueBinEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 20 CUE/BIN entries across 5 disc-based systems (4 per system)
        var cueSystems = new[]
        {
            ("PS1",  new[] { "Crash Bandicoot", "Spyro The Dragon", "Tekken 3", "Gran Turismo" }),
            ("SAT",  new[] { "Nights into Dreams", "Panzer Dragoon Saga", "Radiant Silvergun", "Guardian Heroes" }),
            ("SCD",  new[] { "Sonic CD", "Lunar The Silver Star", "Snatcher", "Popful Mail" }),
            ("PCECD",new[] { "Ys Book I II", "Rondo of Blood", "Lords of Thunder", "Gate of Thunder" }),
            ("DC",   new[] { "Sonic Adventure", "Shenmue", "Jet Grind Radio", "Power Stone" }),
        };

        foreach (var (sysKey, games) in cueSystems)
        {
            var sys = Systems.First(s => s.Key == sysKey);
            for (int i = 0; i < games.Length; i++)
            {
                var id = NextId("gr", sysKey, "cue");
                if (_existingIds.Contains(id)) continue;

                var binName = $"{games[i]} (USA) (Track 01).bin";
                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo
                    {
                        FileName = $"{games[i]} (USA).cue",
                        Extension = ".cue",
                        SizeBytes = sys.TypicalSize + (i * 2048),
                        Directory = sys.FolderAlias,
                        InnerFiles = [new InnerFileInfo { Name = binName, SizeBytes = sys.TypicalSize }],
                    },
                    Tags = ["clean-reference", "cue-bin", "disc-header", sys.DatEcosystem],
                    Difficulty = i < 2 ? "easy" : "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sysKey,
                        Category = "Game",
                        Confidence = 90,
                        HasConflict = false,
                        DatMatchLevel = "exact",
                        DatEcosystem = sys.DatEcosystem,
                        SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "DiscHeader",
                        AcceptableAlternatives = ["FolderName"]
                    },
                    FileModel = new FileModelInfo
                    {
                        Type = "multi-file-set",
                        SetFiles = [$"{games[i]} (USA).cue", binName],
                    },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GenerateGdiTracksEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 8 GDI+Tracks entries (Dreamcast only: 5 single-disc + 3 multi-disc)
        var gdiGames = new[]
        {
            ("Crazy Taxi",          1, "easy"),
            ("Soulcalibur",         1, "easy"),
            ("Rez",                 1, "easy"),
            ("Marvel vs Capcom 2",  1, "medium"),
            ("Skies of Arcadia",    1, "medium"),
            ("Shenmue Disc 1",      3, "medium"),
            ("Shenmue Disc 2",      3, "medium"),
            ("D2 Disc 1",           4, "hard"),
        };

        var sys = Systems.First(s => s.Key == "DC");
        foreach (var (game, discCount, diff) in gdiGames)
        {
            var id = NextId("gr", "DC", "gdi");
            if (_existingIds.Contains(id)) continue;

            var gdiName = $"{game} (USA).gdi";
            var trackNames = Enumerable.Range(1, 3)
                .Select(t => $"{game} (USA) (Track {t:D2}).bin").ToArray();
            var tracks = trackNames.Select(t => new InnerFileInfo { Name = t, SizeBytes = sys.TypicalSize / 3 }).ToArray();

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = gdiName,
                    Extension = ".gdi",
                    SizeBytes = sys.TypicalSize + tracks.Length * 1024,
                    Directory = sys.FolderAlias,
                    InnerFiles = tracks,
                },
                Tags = ["clean-reference", "gdi-tracks", "disc-header", sys.DatEcosystem],
                Difficulty = diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = "DC",
                    Category = "Game",
                    Confidence = 92,
                    HasConflict = false,
                    DatMatchLevel = "exact",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "UniqueExtension",
                    AcceptableAlternatives = ["DiscHeader", "FolderName"]
                },
                FileModel = new FileModelInfo
                {
                    Type = discCount > 1 ? "multi-disc" : "multi-file-set",
                    SetFiles = [gdiName, ..trackNames],
                    DiscCount = discCount > 1 ? discCount : null,
                },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateCcdImgEdgeCases(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 5 CCD/IMG entries in edge-cases
        var ccdEntries = new[]
        {
            ("Ridge Racer (USA)", "PS1"),
            ("Silent Hill (USA)", "PS1"),
            ("Sega Rally Championship (USA)", "SAT"),
            ("Virtua Fighter 2 (USA)", "SAT"),
            ("Sonic CD (USA)", "SCD"),
        };

        foreach (var (game, sysKey) in ccdEntries)
        {
            var id = NextId("ec", sysKey, "ccd");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game}.ccd",
                    Extension = ".ccd",
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                    InnerFiles = [new InnerFileInfo { Name = $"{game}.img", SizeBytes = sys.TypicalSize }, new InnerFileInfo { Name = $"{game}.sub", SizeBytes = sys.TypicalSize / 10 }],
                },
                Tags = ["ccd-img", "disc-header", sys.DatEcosystem],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 85,
                    HasConflict = false,
                    DatMatchLevel = "none",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo
                {
                    Type = "multi-file-set",
                    SetFiles = [$"{game}.ccd", $"{game}.img", $"{game}.sub"],
                },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateMdsMdfEdgeCases(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 5 MDS/MDF entries in edge-cases
        var mdsEntries = new[]
        {
            ("Tekken 3 (Europe)", "PS1"),
            ("Tomb Raider (Europe)", "PS1"),
            ("Devil May Cry (USA)", "PS2"),
            ("ICO (USA)", "PS2"),
            ("Crazy Taxi (Europe)", "DC"),
        };

        foreach (var (game, sysKey) in mdsEntries)
        {
            var id = NextId("ec", sysKey, "mds");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game}.mds",
                    Extension = ".mds",
                    SizeBytes = sys.TypicalSize,
                    Directory = sys.FolderAlias,
                    InnerFiles = [new InnerFileInfo { Name = $"{game}.mdf", SizeBytes = sys.TypicalSize }],
                },
                Tags = ["mds-mdf", "disc-header", sys.DatEcosystem],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 82,
                    HasConflict = false,
                    DatMatchLevel = "none",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo
                {
                    Type = "multi-file-set",
                    SetFiles = [$"{game}.mds", $"{game}.mdf"],
                },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateM3uPlaylistEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 8 M3U playlists for multi-disc games
        var m3uEntries = new[]
        {
            ("Final Fantasy VII",      "PS1", 3),
            ("Final Fantasy VIII",     "PS1", 4),
            ("Resident Evil 2",        "PS1", 2),
            ("Xenosaga Episode I",     "PS2", 2),
            ("Final Fantasy X-2",      "PS2", 2),
            ("Panzer Dragoon Saga",    "SAT", 4),
            ("Shenmue",                "DC",  3),
            ("Ys Book I II",           "PCECD", 1),
        };

        foreach (var (game, sysKey, discs) in m3uEntries)
        {
            var id = NextId("gr", sysKey, "m3u");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            var discFileNames = Enumerable.Range(1, discs)
                .Select(d => $"{game} (USA) (Disc {d}).cue").ToArray();
            var discFiles = discFileNames
                .Select(n => new InnerFileInfo { Name = n, SizeBytes = sys.TypicalSize }).ToArray();

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{game} (USA).m3u",
                    Extension = ".m3u",
                    SizeBytes = discs * 50,
                    Directory = sys.FolderAlias,
                    InnerFiles = discFiles,
                },
                Tags = ["clean-reference", "m3u-playlist", "disc-header", sys.DatEcosystem],
                Difficulty = discs > 2 ? "medium" : "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "weak",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName",
                    AcceptableAlternatives = ["DiscHeader"]
                },
                FileModel = new FileModelInfo
                {
                    Type = "playlist",
                    SetFiles = [$"{game} (USA).m3u", ..discFileNames],
                    DiscCount = discs,
                },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateExtendedSerialEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 10 additional serial-number entries across disc-based systems
        var serials = new[]
        {
            ("SLPS-01222", "PS1", ".bin", "easy"),
            ("SCPS-10050", "PS1", ".bin", "easy"),
            ("SLPS-91001", "PS1", ".bin", "medium"),
            ("SLPS-25001", "PS2", ".iso", "easy"),
            ("SCPS-55002", "PS2", ".iso", "medium"),
            ("SLPS-25401", "PS2", ".iso", "medium"),
            ("UCJS-10001", "PSP", ".iso", "easy"),
            ("ULJM-05001", "PSP", ".iso", "medium"),
            ("MK-81001", "SAT", ".bin", "hard"),
            ("T-8101N", "DC", ".gdi", "hard"),
        };

        foreach (var (serial, sysKey, ext, diff) in serials)
        {
            var id = NextId("gr", sysKey, "serial");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{serial} (USA){ext}",
                    Extension = ext,
                    SizeBytes = sys.TypicalSize,
                    Directory = "roms",
                },
                Tags = ["clean-reference", "serial-number"],
                Difficulty = diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 85,
                    HasConflict = false,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "SerialNumber",
                    AcceptableAlternatives = [sys.PrimaryDetection],
                    AcceptableConsoleKeys = sysKey == "PS1" ? ["PS2"] : null
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateExtendedContainerVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 9 additional container format entries in edge-cases
        var containers = new[]
        {
            ("Crisis Core FF VII (USA).cso",   ".cso",  "PSP",  "container-cso", "medium"),
            ("Monster Hunter FU (USA).cso",    ".cso",  "PSP",  "container-cso", "medium"),
            ("Xenoblade Chronicles (USA).wia",  ".wia",  "WII",  "container-wia", "hard"),
            ("Mario Galaxy 2 (USA).wia",       ".wia",  "WII",  "container-wia", "hard"),
            ("Smash Bros Melee (USA).rvz",     ".rvz",  "GC",   "container-rvz", "medium"),
            ("Luigi Mansion (USA).rvz",        ".rvz",  "GC",   "container-rvz", "medium"),
            ("Mario Kart Wii (USA).wbfs",      ".wbfs", "WII",  "container-wbfs","easy"),
            ("Wii Sports Resort (USA).wbfs",   ".wbfs", "WII",  "container-wbfs","easy"),
            ("New Super Mario Bros U (USA).wux",".wux", "WIIU", "container-wux", "hard"),
        };

        foreach (var (name, ext, sysKey, tag, diff) in containers)
        {
            var id = NextId("ec", sysKey, "container");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name,
                    Extension = ext,
                    SizeBytes = sys.TypicalSize / 3,
                    Directory = sys.FolderAlias,
                },
                Tags = ["container-variant", tag],
                Difficulty = diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey,
                    Category = "Game",
                    Confidence = 88,
                    HasConflict = false,
                    DatMatchLevel = "none",
                    DatEcosystem = sys.DatEcosystem,
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "UniqueExtension",
                    AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE S2 — BIOS-Fehlermodi ════════════════════════════════════

    private void GenerateBiosErrorModes(Dictionary<string, List<GroundTruthEntry>> result)
    {
        GenerateBiosWrongNameEntries(result);
        GenerateBiosWrongFolderEntries(result);
        GenerateBiosFalsePositives(result);
        GenerateBiosFalseNegatives(result);
        GenerateBiosNegativeControls(result);
        GenerateSharedBiosEntries(result);
    }

    private void GenerateBiosWrongNameEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("scph-1001.bin", "PS1", "redump", 524288L, "DiscHeader"),
            ("ps2bios_39001.bin", "PS2", "redump", 4194304L, "DiscHeader"),
            ("dc_boot_rom.bin", "DC", "redump", 2097152L, "FolderName"),
            ("saturn_bios_v1.bin", "SAT", "redump", 524288L, "FolderName"),
            ("gba_biosfile.bin", "GBA", "no-intro", 16384L, "FolderName"),
        };

        foreach (var (name, sysKey, datEco, size, primary) in entries)
        {
            var id = NextId("cm", sysKey, "bioswrongname");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ".bin", SizeBytes = size, Directory = sys.FolderAlias,
                },
                Tags = ["bios", "bios-wrong-name", "wrong-name", datEco],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey, Category = "Bios", Confidence = 65,
                    HasConflict = false, DatMatchLevel = "none", DatEcosystem = datEco, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = primary, AcceptableAlternatives = [primary == "DiscHeader" ? "FolderName" : "DiscHeader"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosWrongFolderEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("scph5500.bin", "PS1", "ps2", "redump", 524288L),
            ("scph70012.bin", "PS2", "ps1", "redump", 4194304L),
            ("dc_bios.bin", "DC", "sat", "redump", 2097152L),
            ("saturn_bios.bin", "SAT", "dc", "redump", 524288L),
            ("gba_bios.bin", "GBA", "nds", "no-intro", 16384L),
        };

        foreach (var (name, sysKey, wrongDir, datEco, size) in entries)
        {
            var id = NextId("cm", sysKey, "bioswrongfolder");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ".bin", SizeBytes = size, Directory = wrongDir,
                },
                Tags = ["bios", "bios-wrong-folder", "folder-header-conflict", datEco],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey, Category = "Bios", Confidence = 55,
                    HasConflict = true, DatMatchLevel = "none", DatEcosystem = datEco, SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader", AcceptableAlternatives = ["FolderName"],
                    AcceptableConsoleKeys = [wrongDir.ToUpperInvariant()]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosFalsePositives(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("BIOS Agent (USA).bin", ".bin", "PS1", "redump", 734003200L, "DiscHeader"),
            ("BIOS Fear (USA).iso", ".iso", "PS2", "redump", 4700000000L, "DiscHeader"),
            ("BIOS Hazard (USA).gba", ".gba", "GBA", "no-intro", 16777216L, "CartridgeHeader"),
            ("Bio Senshi Dan (Japan).nes", ".nes", "NES", "no-intro", 40976L, "CartridgeHeader"),
            ("BioMetal (USA).sfc", ".sfc", "SNES", "no-intro", 524288L, "CartridgeHeader"),
        };

        foreach (var (name, ext, sysKey, datEco, size, primary) in entries)
        {
            var id = NextId("ec", sysKey, "biosfp");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ext, SizeBytes = size, Directory = sys.FolderAlias,
                },
                Tags = ["bios-false-positive", "clean-reference", datEco],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey, Category = "Game", Confidence = 80,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = datEco, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = primary,
                    AcceptableAlternatives = primary == "CartridgeHeader" ? ["UniqueExtension"] : ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosFalseNegatives(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("System ROM v2.2 (USA).bin", "PS1", "DiscHeader"),
            ("Boot ROM v1.01d (Japan).bin", "DC", "FolderName"),
            ("IPL ROM v1.00 (Japan).bin", "SAT", "FolderName"),
        };

        foreach (var (name, sysKey, primary) in entries)
        {
            var id = NextId("ec", sysKey, "biosfn");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ".bin", SizeBytes = sys.TypicalSize / 1000,
                    Directory = sys.FolderAlias,
                },
                Tags = ["bios", "bios-edge", sys.DatEcosystem],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey, Category = "Bios", Confidence = 60,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = sys.DatEcosystem, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = primary, AcceptableAlternatives = [primary == "DiscHeader" ? "FolderName" : "DiscHeader"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateBiosNegativeControls(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("BIOS Update Guide.pdf", ".pdf", 245760L),
            ("bios_readme.txt", ".txt", 4096L),
            ("BIOS_Checker.exe", ".exe", 1048576L),
        };

        foreach (var (name, ext, size) in entries)
        {
            var id = NextId("nc", "UNK", "biosneg");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ext, SizeBytes = size, Directory = "docs",
                },
                Tags = ["negative-control", "bios-negative", "non-game"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    Category = "Unknown", Confidence = 0,
                    HasConflict = false, DatMatchLevel = "none", SortDecision = "skip"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Heuristic"
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateSharedBiosEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            ("neogeo.zip", ".zip", "ARCADE", "mame", 1048576L, new[] { "ARCADE", "NEOGEO" }),
            ("scph5500.bin", ".bin", "PS1", "redump", 524288L, new[] { "PS1" }),
            ("dc_bios.bin", ".bin", "DC", "redump", 2097152L, new[] { "DC" }),
            ("saturn_bios.bin", ".bin", "SAT", "redump", 524288L, new[] { "SAT" }),
            ("gba_bios.bin", ".bin", "GBA", "no-intro", 16384L, new[] { "GBA" }),
        };

        foreach (var (name, ext, sysKey, datEco, size, sharedKeys) in entries)
        {
            var id = NextId("gc", sysKey, "biosshared");
            if (_existingIds.Contains(id)) continue;

            var sys = Systems.First(s => s.Key == sysKey);
            var tags = sysKey == "ARCADE"
                ? new[] { "bios", "bios-shared", "arcade-bios", datEco }
                : new[] { "bios", "bios-shared", datEco };

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = name, Extension = ext, SizeBytes = size, Directory = sys.FolderAlias,
                },
                Tags = tags,
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sysKey, Category = "Bios", Confidence = 90,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = datEco, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName", AcceptableAlternatives = sysKey == "ARCADE" ? [] : ["DiscHeader"]
                },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE S3 — Arcade-Konfusion ═══════════════════════════════════

    private void GenerateArcadeConfusion(Dictionary<string, List<GroundTruthEntry>> result)
    {
        GenerateArcadeSplitMerged(result);
        GenerateArcadeMergedNonMerged(result);
        GenerateArcadeChdGames(result);
        GenerateDiscArcadeEntries(result);
    }

    private void GenerateArcadeSplitMerged(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new[]
        {
            ("kof98", 33554432L), ("mslug", 33554432L), ("samsho2", 33554432L), ("garou", 33554432L),
            ("rbff2", 33554432L), ("kof2002", 33554432L), ("mslug3", 33554432L), ("lastblad", 33554432L),
        };

        foreach (var (name, size) in games)
        {
            var id = NextId("ec", "ARCADE", "splitmerge");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{name}.zip", Extension = ".zip", SizeBytes = size, Directory = "arcade" },
                Tags = ["arcade-confusion-split-merged", "arcade-split", "mame"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE", Category = "Game", Confidence = 75,
                    HasConflict = true, DatMatchLevel = "exact", DatEcosystem = "mame", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateArcadeMergedNonMerged(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new[]
        {
            ("mslug_merged", 67108864L), ("kof98_merged", 67108864L), ("samsho5_merged", 67108864L),
            ("garoupm", 67108864L), ("rbff2_merged", 67108864L), ("kof2k2mp", 67108864L),
            ("mslug3_merged", 67108864L), ("svc_merged", 67108864L),
        };

        foreach (var (name, size) in games)
        {
            var id = NextId("ec", "ARCADE", "mergednonm");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{name}.zip", Extension = ".zip", SizeBytes = size, Directory = "arcade/merged" },
                Tags = ["arcade-confusion-merged-nonmerged", "arcade-merged", "mame"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE", Category = "Game", Confidence = 70,
                    HasConflict = true, DatMatchLevel = "weak", DatEcosystem = "mame", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateArcadeChdGames(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new[]
        {
            ("area51", 536870912L), ("kinst", 268435456L), ("kinst2", 268435456L), ("sfiii3rd", 134217728L),
            ("cps3boot", 67108864L), ("gauntdl", 536870912L), ("crusnwld", 268435456L), ("calspeed", 134217728L),
        };

        foreach (var (name, size) in games)
        {
            var id = NextId("gr", "ARCADE", "chd");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{name}.chd", Extension = ".chd", SizeBytes = size, Directory = "arcade/chd" },
                Tags = ["arcade-game-chd", "arcade-chd", "mame"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE", Category = "Game", Confidence = 85,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = "mame", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = ["UniqueExtension"] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateDiscArcadeEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new (string id, string name, string ext, string dir, string ck, long size, string tag, string altKey)[]
        {
            ("ec-ARCADE-discarcade", "ikaruga.zip", ".zip", "naomi", "ARCADE", 33554432L, "naomi", "NAOMI"),
            ("ec-ARCADE-discarcade", "crazytaxi.zip", ".zip", "naomi", "ARCADE", 67108864L, "naomi", "NAOMI"),
            ("ec-ARCADE-discarcade", "vf4.chd", ".chd", "naomi", "ARCADE", 536870912L, "naomi", "NAOMI"),
            ("ec-ARCADE-discarcade", "demofist.zip", ".zip", "atomiswave", "ARCADE", 33554432L, "atomiswave", "AWAVE"),
            ("ec-ARCADE-discarcade", "ggisuka.zip", ".zip", "atomiswave", "ARCADE", 33554432L, "atomiswave", "AWAVE"),
        };

        foreach (var (idPrefix, name, ext, dir, ck, size, tag, altKey) in games)
        {
            var id = NextId("ec", "ARCADE", "discarcade");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = name, Extension = ext, SizeBytes = size, Directory = dir },
                Tags = ["arcade-confusion-split-merged", "disc-arcade", tag],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = ck, Category = "Game", Confidence = 65,
                    HasConflict = true, DatMatchLevel = "weak", DatEcosystem = "mame", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [], AcceptableConsoleKeys = [altKey] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ PHASE S4 — Header-vs-Headerless + Cross-System ════════════════

    private void GenerateHeaderVsHeaderlessPairs(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Systems with cartridge headers: NES, SNES, MD, A78, LYNX, GB, GBA, N64
        var pairs = new (string sys, string ext, string game, long size)[]
        {
            ("NES",  ".nes", "Super Mario Bros", 40976),
            ("SNES", ".sfc", "Chrono Trigger", 4194304),
            ("MD",   ".md",  "Sonic The Hedgehog", 524288),
            ("A78",  ".a78", "Asteroids", 65536),
            ("LYNX", ".lnx", "California Games", 262144),
            ("GB",   ".gb",  "Tetris", 32768),
            ("N64",  ".z64", "Super Mario 64", 8388608),
        };

        foreach (var (sys, ext, game, size) in pairs)
        {
            var sysInfo = Systems.First(s => s.Key == sys);

            // Headed version
            var headedId = NextId("ec", sys, "headerless");
            if (!_existingIds.Contains(headedId))
            {
                Add(result, "edge-cases.jsonl", new GroundTruthEntry
                {
                    Id = headedId,
                    Source = new SourceInfo { FileName = $"{game} (USA){ext}", Extension = ext, SizeBytes = size, Directory = sysInfo.FolderAlias },
                    Tags = ["cartridge-header", "header-vs-headerless-pair", sysInfo.DatEcosystem],
                    Difficulty = "medium",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys, Category = "Game", Confidence = 95,
                        HasConflict = false, DatMatchLevel = "exact", DatEcosystem = sysInfo.DatEcosystem, SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations { PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["UniqueExtension"] },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }

            // Headerless version
            var headerlessId = NextId("ec", sys, "headerless");
            if (!_existingIds.Contains(headerlessId))
            {
                Add(result, "edge-cases.jsonl", new GroundTruthEntry
                {
                    Id = headerlessId,
                    Source = new SourceInfo { FileName = $"{game} (USA).bin", Extension = ".bin", SizeBytes = size, Directory = sysInfo.FolderAlias },
                    Tags = ["headerless", "header-vs-headerless-pair", sysInfo.DatEcosystem],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys, Category = "Game", Confidence = 60,
                        HasConflict = true, DatMatchLevel = "fuzzy", DatEcosystem = sysInfo.DatEcosystem, SortDecision = "review"
                    },
                    DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [] },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }

        // GBA headerless only (GBA always has header)
        var gbaId = NextId("ec", "GBA", "headerless");
        if (!_existingIds.Contains(gbaId))
        {
            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = gbaId,
                Source = new SourceInfo { FileName = "Pokemon FireRed (USA).bin", Extension = ".bin", SizeBytes = 16777216, Directory = "gba" },
                Tags = ["headerless", "header-vs-headerless-pair", "no-intro"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "GBA", Category = "Game", Confidence = 55,
                    HasConflict = true, DatMatchLevel = "fuzzy", DatEcosystem = "no-intro", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateNewCrossSystemPairs(Dictionary<string, List<GroundTruthEntry>> result)
    {
        GenerateSatDcCrossSystem(result);
        GeneratePcePcecdDisambiguation(result);
        GenerateNesInesVariants(result);
    }

    // ═══ PHASE S5 — Golden-Core Hard Entries ═════════════════════════════

    private void GenerateGoldenCoreHardEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Tier-1: 5 hard entries per system (9 systems × 5 = 45)
        var tier1Systems = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 40976L, Dir = "nes", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Zelda", "DuckTales", "Kirby Adventure", "Ninja Gaiden", "Double Dragon" } },
            new { Sys = "SNES", Ext = ".sfc", Size = 524288L, Dir = "snes", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Earthbound", "Donkey Kong Country", "F-Zero", "Star Fox", "Mega Man X" } },
            new { Sys = "N64", Ext = ".z64", Size = 8388608L, Dir = "n64", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Banjo-Kazooie", "Perfect Dark", "Mario Kart 64", "Diddy Kong Racing", "Wave Race 64" } },
            new { Sys = "GBA", Ext = ".gba", Size = 16777216L, Dir = "gba", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Advance Wars", "Fire Emblem", "Golden Sun", "Castlevania Aria", "Kirby Nightmare" } },
            new { Sys = "GB", Ext = ".gb", Size = 32768L, Dir = "gb", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Links Awakening", "Kirby Dream Land", "Wario Land", "Gargoyles Quest", "Mega Man V" } },
            new { Sys = "GBC", Ext = ".gbc", Size = 2097152L, Dir = "gbc", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Shantae", "Dragon Warrior III", "Metal Gear Solid GBC", "Zelda Oracle Ages", "Zelda Oracle Seasons" } },
            new { Sys = "MD", Ext = ".md", Size = 1048576L, Dir = "md", Detect = "CartridgeHeader", Dat = "no-intro",
                  Games = new[] { "Phantasy Star IV", "Shining Force II", "Comix Zone", "Vectorman", "Thunder Force IV" } },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, Dir = "ps1", Detect = "DiscHeader", Dat = "redump",
                  Games = new[] { "Tekken 3", "Spyro", "RE3 Nemesis", "GT2", "Vagrant Story" } },
            new { Sys = "PS2", Ext = ".iso", Size = 4700000000L, Dir = "ps2", Detect = "DiscHeader", Dat = "redump",
                  Games = new[] { "Okami", "RE4", "Persona 3", "Ratchet Clank", "Jak and Daxter" } }
        };

        var hardVariants = new[]
        {
            new { Tag = "wrong-name", Conf = 75, Conflict = false, DatMatch = "exact", Decision = "sort" },
            new { Tag = "folder-vs-header-conflict", Conf = 75, Conflict = true, DatMatch = "exact", Decision = "sort" },
            new { Tag = "headerless", Conf = 60, Conflict = true, DatMatch = "fuzzy", Decision = "review" },
            new { Tag = "extension-conflict", Conf = 70, Conflict = true, DatMatch = "exact", Decision = "sort" },
            new { Tag = "wrong-name", Conf = 75, Conflict = false, DatMatch = "exact", Decision = "sort" }
        };

        foreach (var sys in tier1Systems)
        {
            for (int i = 0; i < 5; i++)
            {
                var variant = hardVariants[i];
                var id = NextId("gc", sys.Sys, "hardcore");
                if (_existingIds.Contains(id)) continue;

                var fileName = variant.Tag == "headerless"
                    ? $"{sys.Games[i]} (USA).bin"
                    : $"{sys.Games[i]} (USA){sys.Ext}";
                var ext = variant.Tag == "headerless" ? ".bin" : sys.Ext;

                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo { FileName = fileName, Extension = ext, SizeBytes = sys.Size, Directory = sys.Dir },
                    Tags = [variant.Tag, sys.Dat],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Sys, Category = "Game", Confidence = variant.Conf,
                        HasConflict = variant.Conflict, DatMatchLevel = variant.DatMatch,
                        DatEcosystem = sys.Dat, SortDecision = variant.Decision
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.Detect, AcceptableAlternatives = ["FolderName"]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" }
                });
            }
        }

        // Tier-2: 15 hard entries across secondary systems
        var tier2Systems = new[]
        {
            new { Sys = "DC", Ext = ".gdi", Size = 1073741824L, Dir = "dreamcast", Detect = "UniqueExtension", Dat = "redump", Game = "Soul Calibur" },
            new { Sys = "SAT", Ext = ".bin", Size = 734003200L, Dir = "saturn", Detect = "DiscHeader", Dat = "redump", Game = "Virtua Fighter 2" },
            new { Sys = "PSP", Ext = ".iso", Size = 1800000000L, Dir = "psp", Detect = "DiscHeader", Dat = "redump", Game = "Crisis Core FF7" },
            new { Sys = "WII", Ext = ".wbfs", Size = 4699979776L, Dir = "wii", Detect = "UniqueExtension", Dat = "redump", Game = "Xenoblade Chronicles" },
            new { Sys = "GC", Ext = ".iso", Size = 1459617792L, Dir = "gc", Detect = "DiscHeader", Dat = "redump", Game = "Eternal Darkness" },
            new { Sys = "SWITCH", Ext = ".nsp", Size = 16106127360L, Dir = "switch", Detect = "UniqueExtension", Dat = "no-intro", Game = "Xenoblade 2" },
            new { Sys = "NDS", Ext = ".nds", Size = 134217728L, Dir = "nds", Detect = "UniqueExtension", Dat = "no-intro", Game = "Chrono Trigger DS" },
            new { Sys = "32X", Ext = ".32x", Size = 3145728L, Dir = "32x", Detect = "UniqueExtension", Dat = "no-intro", Game = "Kolibri" },
            new { Sys = "PCE", Ext = ".pce", Size = 524288L, Dir = "pce", Detect = "UniqueExtension", Dat = "no-intro", Game = "Blazing Lazers" },
            new { Sys = "SMS", Ext = ".sms", Size = 262144L, Dir = "sms", Detect = "UniqueExtension", Dat = "no-intro", Game = "Wonder Boy III" },
            new { Sys = "GG", Ext = ".gg", Size = 262144L, Dir = "gg", Detect = "UniqueExtension", Dat = "no-intro", Game = "Sonic Triple Trouble" },
            new { Sys = "A78", Ext = ".a78", Size = 65536L, Dir = "a78", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Ballblazer" },
            new { Sys = "LYNX", Ext = ".lnx", Size = 262144L, Dir = "lynx", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Blue Lightning" },
            new { Sys = "A26", Ext = ".a26", Size = 4096L, Dir = "a26", Detect = "UniqueExtension", Dat = "no-intro", Game = "River Raid" },
            new { Sys = "AMIGA", Ext = ".adf", Size = 901120L, Dir = "amiga", Detect = "UniqueExtension", Dat = "tosec", Game = "Cannon Fodder" }
        };

        int t2Index = 0;
        foreach (var sys in tier2Systems)
        {
            var variant = hardVariants[t2Index % hardVariants.Length];
            t2Index++;
            var id = NextId("gc", sys.Sys, "hardcore");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{sys.Game} (USA){sys.Ext}", Extension = sys.Ext, SizeBytes = sys.Size, Directory = sys.Dir },
                Tags = [variant.Tag, sys.Dat],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys.Sys, Category = "Game", Confidence = 70,
                    HasConflict = true, DatMatchLevel = "exact",
                    DatEcosystem = sys.Dat, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = sys.Detect, AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateGoldenCoreAdversarialEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // Per Tier-1 system: 2 adversarial cases (triple-threat + impossible)
        var tier1Adversarial = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 40976L, WrongDir = "snes", WrongExt = ".sfc", Detect = "CartridgeHeader",
                  Game1 = "Castlevania III", Game2 = "Mega Man 6" },
            new { Sys = "SNES", Ext = ".sfc", Size = 524288L, WrongDir = "nes", WrongExt = ".nes", Detect = "CartridgeHeader",
                  Game1 = "Secret of Mana", Game2 = "Chrono Trigger" },
            new { Sys = "N64", Ext = ".z64", Size = 8388608L, WrongDir = "ps1", WrongExt = ".bin", Detect = "CartridgeHeader",
                  Game1 = "F-Zero X", Game2 = "Star Wars Rogue" },
            new { Sys = "GBA", Ext = ".gba", Size = 16777216L, WrongDir = "nds", WrongExt = ".nds", Detect = "CartridgeHeader",
                  Game1 = "Metroid Zero", Game2 = "Wario Ware" },
            new { Sys = "GB", Ext = ".gb", Size = 32768L, WrongDir = "gbc", WrongExt = ".gbc", Detect = "CartridgeHeader",
                  Game1 = "Castlevania Legends", Game2 = "Donkey Kong Land" },
            new { Sys = "GBC", Ext = ".gbc", Size = 2097152L, WrongDir = "gb", WrongExt = ".gb", Detect = "CartridgeHeader",
                  Game1 = "Pokemon Crystal", Game2 = "Mario Golf" },
            new { Sys = "MD", Ext = ".md", Size = 1048576L, WrongDir = "snes", WrongExt = ".sfc", Detect = "CartridgeHeader",
                  Game1 = "Gunstar Heroes", Game2 = "Ristar" },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, WrongDir = "ps2", WrongExt = ".iso", Detect = "DiscHeader",
                  Game1 = "Crash Team Racing", Game2 = "Legend of Mana" },
            new { Sys = "PS2", Ext = ".iso", Size = 4700000000L, WrongDir = "ps1", WrongExt = ".bin", Detect = "DiscHeader",
                  Game1 = "Devil May Cry", Game2 = "Ico" }
        };

        foreach (var sys in tier1Adversarial)
        {
            // Triple-threat: wrong ext + wrong folder + detectable by header
            var id1 = NextId("gc", sys.Sys, "adversarial");
            if (!_existingIds.Contains(id1))
            {
                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id1,
                    Source = new SourceInfo { FileName = $"{sys.Game1} (USA){sys.WrongExt}", Extension = sys.WrongExt, SizeBytes = sys.Size, Directory = sys.WrongDir },
                    Tags = ["cross-system-conflict", "wrong-extension", "wrong-folder", "adversarial-triple"],
                    Difficulty = "adversarial",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Sys, Category = "Game", Confidence = 55,
                        HasConflict = true, DatMatchLevel = "none",
                        DatEcosystem = "unknown", SortDecision = "review"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = sys.Detect, AcceptableAlternatives = ["DatMatch", "Heuristic"],
                        AcceptableConsoleKeys = [sys.WrongDir.ToUpperInvariant()]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" }
                });
            }

            // Impossible: headerless + no DAT + ambiguous .bin
            var id2 = NextId("gc", sys.Sys, "adversarial");
            if (!_existingIds.Contains(id2))
            {
                Add(result, "golden-core.jsonl", new GroundTruthEntry
                {
                    Id = id2,
                    Source = new SourceInfo { FileName = $"{sys.Game2} (USA).bin", Extension = ".bin", SizeBytes = sys.Size, Directory = sys.WrongDir },
                    Tags = ["headerless", "no-dat", "ambiguous-extension", "adversarial-impossible"],
                    Difficulty = "adversarial",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = sys.Sys, Category = "Game", Confidence = 40,
                        HasConflict = true, DatMatchLevel = "none",
                        DatEcosystem = "unknown", SortDecision = "review"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = "Heuristic", AcceptableAlternatives = ["FolderName", "FileSize"],
                        AcceptableConsoleKeys = [sys.WrongDir.ToUpperInvariant()]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" }
                });
            }
        }

        // Arcade in cartridge folder
        var arcadeId = NextId("gc", "ARCADE", "adversarial");
        if (!_existingIds.Contains(arcadeId))
        {
            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = arcadeId,
                Source = new SourceInfo { FileName = "mslug.zip", Extension = ".zip", SizeBytes = 15728640, Directory = "snes" },
                Tags = ["cross-system-conflict", "wrong-folder", "arcade-in-cartridge-folder", "adversarial-triple"],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "ARCADE", Category = "Game", Confidence = 50,
                    HasConflict = true, DatMatchLevel = "none",
                    DatEcosystem = "unknown", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "ArchiveContent", AcceptableAlternatives = ["DatMatch", "Heuristic"],
                    AcceptableConsoleKeys = ["SNES"]
                },
                FileModel = new FileModelInfo { Type = "archive" }
            });
        }

        // DC disc in PS1 folder
        var dcId = NextId("gc", "DC", "adversarial");
        if (!_existingIds.Contains(dcId))
        {
            Add(result, "golden-core.jsonl", new GroundTruthEntry
            {
                Id = dcId,
                Source = new SourceInfo { FileName = "Shenmue (USA).bin", Extension = ".bin", SizeBytes = 734003200, Directory = "ps1" },
                Tags = ["cross-system-conflict", "wrong-folder", "disc-in-wrong-disc-folder", "adversarial-triple"],
                Difficulty = "adversarial",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "DC", Category = "Game", Confidence = 45,
                    HasConflict = true, DatMatchLevel = "none",
                    DatEcosystem = "unknown", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "DiscHeader", AcceptableAlternatives = ["DatMatch", "Heuristic"],
                    AcceptableConsoleKeys = ["PS1"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    // ═══ PHASE S6 — Negative Controls + NonGame Upgrade ══════════════════

    private void GenerateNonRomNegativeControls(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var files = new[]
        {
            new { Name = "Game Manual.pdf", Ext = ".pdf", Size = 2457600L, Dir = "manuals" },
            new { Name = "readme.pdf", Ext = ".pdf", Size = 102400L, Dir = "roms/nes" },
            new { Name = "boxart.jpg", Ext = ".jpg", Size = 512000L, Dir = "images" },
            new { Name = "screenshot.jpeg", Ext = ".jpeg", Size = 256000L, Dir = "screenshots" },
            new { Name = "thumb.png", Ext = ".png", Size = 65536L, Dir = "media" },
            new { Name = "cover.png", Ext = ".png", Size = 1048576L, Dir = "artwork" },
            new { Name = "readme.txt", Ext = ".txt", Size = 4096L, Dir = "roms" },
            new { Name = "release.nfo", Ext = ".nfo", Size = 8192L, Dir = "roms/snes" },
            new { Name = "filelist.txt", Ext = ".txt", Size = 2048L, Dir = "roms/gba" },
            new { Name = "checksum.sfv", Ext = ".sfv", Size = 1024L, Dir = "roms" },
            new { Name = "verify.crc", Ext = ".crc", Size = 512L, Dir = "roms/md" },
            new { Name = "emulator.exe", Ext = ".exe", Size = 5242880L, Dir = "tools" },
            new { Name = "plugin.dll", Ext = ".dll", Size = 1048576L, Dir = "tools" },
            new { Name = "soundtrack.mp3", Ext = ".mp3", Size = 4194304L, Dir = "music" },
            new { Name = "intro.mp3", Ext = ".mp3", Size = 2097152L, Dir = "roms/ps1" }
        };

        foreach (var f in files)
        {
            var id = NextId("nc", "UNK", "nonrom");
            if (_existingIds.Contains(id)) continue;

            var conf = f.Ext is ".nfo" or ".sfv" or ".crc" ? 90 : 95;
            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = f.Name, Extension = f.Ext, SizeBytes = f.Size, Directory = f.Dir },
                Tags = ["negative-control", "non-game", "non-rom-content"],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = null, Category = "Junk", Confidence = conf,
                    HasConflict = false, DatMatchLevel = "none",
                    DatEcosystem = "unknown", SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Heuristic", AcceptableAlternatives = ["MagicBytes"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateDemoEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var demos = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 24592L, Dir = "nes", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Super Mario Bros (Demo) (USA)" },
            new { Sys = "NES", Ext = ".nes", Size = 40976L, Dir = "nes", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Zelda (Demo Kiosk) (USA)" },
            new { Sys = "SNES", Ext = ".sfc", Size = 524288L, Dir = "snes", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Star Fox (Demo) (USA)" },
            new { Sys = "SNES", Ext = ".sfc", Size = 524288L, Dir = "snes", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Donkey Kong Country (Not for Resale) (USA)" },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, Dir = "ps1", Detect = "DiscHeader", Dat = "redump", Game = "Crash Bandicoot (Demo) (USA)" },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, Dir = "ps1", Detect = "DiscHeader", Dat = "redump", Game = "Metal Gear Solid (Demo) (USA)" },
            new { Sys = "GBA", Ext = ".gba", Size = 16777216L, Dir = "gba", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Pokemon FireRed (Demo) (USA)" },
            new { Sys = "GBA", Ext = ".gba", Size = 16777216L, Dir = "gba", Detect = "CartridgeHeader", Dat = "no-intro", Game = "Metroid Fusion (Not for Resale) (USA)" }
        };

        foreach (var d in demos)
        {
            var id = NextId("gr", d.Sys, "demo");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{d.Game}{d.Ext}", Extension = d.Ext, SizeBytes = d.Size, Directory = d.Dir },
                Tags = ["demo", "non-game", d.Dat],
                Difficulty = "easy",
                Expected = new ExpectedResult
                {
                    ConsoleKey = d.Sys, Category = "NonGame", Confidence = 85,
                    HasConflict = false, DatMatchLevel = "exact",
                    DatEcosystem = d.Dat, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = d.Detect, AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateHackEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var hacks = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 524288L, Dir = "nes", Game = "Super Mario Bros 3Mix (Hack) (USA)" },
            new { Sys = "NES", Ext = ".nes", Size = 524288L, Dir = "nes", Game = "Rockman 4 Minus Infinity (Hack)" },
            new { Sys = "SNES", Ext = ".sfc", Size = 3145728L, Dir = "snes", Game = "Super Metroid Redesign (Hack)" },
            new { Sys = "SNES", Ext = ".sfc", Size = 3145728L, Dir = "snes", Game = "Hyper Metroid (Hack)" }
        };

        foreach (var h in hacks)
        {
            var id = NextId("gr", h.Sys, "hack");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{h.Game}{h.Ext}", Extension = h.Ext, SizeBytes = h.Size, Directory = h.Dir },
                Tags = ["hack", "non-game", "no-intro"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = h.Sys, Category = "NonGame", Confidence = 75,
                    HasConflict = false, DatMatchLevel = "none",
                    DatEcosystem = "unknown", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["FolderName", "Keyword"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateUtilityEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var utils = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 32768L, Dir = "nes", Game = "Action Replay (USA)" },
            new { Sys = "GB", Ext = ".gb", Size = 32768L, Dir = "gb", Game = "Game Genie (USA)" },
            new { Sys = "SNES", Ext = ".sfc", Size = 65536L, Dir = "snes", Game = "Pro Action Replay (Europe)" }
        };

        foreach (var u in utils)
        {
            var id = NextId("nc", "UNK", "utility");
            if (_existingIds.Contains(id)) continue;

            Add(result, "negative-controls.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{u.Game}{u.Ext}", Extension = u.Ext, SizeBytes = u.Size, Directory = u.Dir },
                Tags = ["non-game", "negative-control", "no-intro"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = u.Sys, Category = "NonGame", Confidence = 80,
                    HasConflict = false, DatMatchLevel = "exact",
                    DatEcosystem = "no-intro", SortDecision = "block"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["DatMatch"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    // ═══ PHASE M1 — Computer-Format-Tiefe ════════════════════════════════

    private void GenerateComputerFormatDepth(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 8 Amiga WHDLoad directory-based entries
        var whdGames = new[]
        {
            new { Name = "Turrican II", Diff = "medium" },
            new { Name = "Another World", Diff = "medium" },
            new { Name = "Lemmings", Diff = "medium" },
            new { Name = "Speedball 2", Diff = "medium" },
            new { Name = "Sensible Soccer", Diff = "hard" },
            new { Name = "Wings", Diff = "hard" },
            new { Name = "Cannon Fodder", Diff = "hard" },
            new { Name = "Shadow of Beast", Diff = "hard" }
        };

        foreach (var g in whdGames)
        {
            var id = NextId("gr", "AMIGA", "whdload");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo
                {
                    FileName = $"{g.Name}.slave", Extension = ".slave", SizeBytes = 65536,
                    Directory = $"amiga/{g.Name}",
                    InnerFiles = [new InnerFileInfo { Name = $"{g.Name}.slave", SizeBytes = 65536 }, new InnerFileInfo { Name = "data/game.dat", SizeBytes = 1048576 }]
                },
                Tags = ["directory-based", "folder-only-detection", "tosec"],
                Difficulty = g.Diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = "AMIGA", Category = "Game", Confidence = 70,
                    HasConflict = false, DatMatchLevel = "weak", DatEcosystem = "tosec", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "FolderName", AcceptableAlternatives = ["Keyword"]
                },
                FileModel = new FileModelInfo { Type = "directory" }
            });
        }

        // Computer format variants
        var computerFormats = new[]
        {
            // C64
            new { Sys = "C64", Game = "Boulder Dash (USA)", Ext = ".d64", Size = 174848L, Dir = "c64", Tag = "unique-extension", Dat = "no-intro" },
            new { Sys = "C64", Game = "Bubble Bobble (USA)", Ext = ".t64", Size = 32768L, Dir = "c64", Tag = "unique-extension", Dat = "no-intro" },
            new { Sys = "C64", Game = "Last Ninja II (USA)", Ext = ".crt", Size = 524288L, Dir = "c64", Tag = "unique-extension", Dat = "no-intro" },
            new { Sys = "C64", Game = "Impossible Mission (USA)", Ext = ".prg", Size = 16384L, Dir = "c64", Tag = "ambiguous-extension", Dat = "no-intro" },
            new { Sys = "C64", Game = "Ghostbusters (USA)", Ext = ".tap", Size = 65536L, Dir = "c64", Tag = "ambiguous-extension", Dat = "no-intro" },
            // ZX Spectrum
            new { Sys = "ZX", Game = "Manic Miner (Europe)", Ext = ".tzx", Size = 32768L, Dir = "zx", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "ZX", Game = "Jet Set Willy (Europe)", Ext = ".tap", Size = 49152L, Dir = "zx", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "ZX", Game = "R-Type (Europe)", Ext = ".z80", Size = 49152L, Dir = "zx", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "ZX", Game = "Knight Lore (Europe)", Ext = ".sna", Size = 49179L, Dir = "zx", Tag = "unique-extension", Dat = "tosec" },
            // MSX
            new { Sys = "MSX", Game = "Metal Gear (Japan)", Ext = ".rom", Size = 131072L, Dir = "msx", Tag = "unique-extension", Dat = "no-intro" },
            new { Sys = "MSX", Game = "Nemesis (Japan)", Ext = ".dsk", Size = 737280L, Dir = "msx", Tag = "unique-extension", Dat = "no-intro" },
            new { Sys = "MSX", Game = "Penguin Adventure (Japan)", Ext = ".cas", Size = 65536L, Dir = "msx", Tag = "unique-extension", Dat = "no-intro" },
            // Atari ST
            new { Sys = "ATARIST", Game = "Dungeon Master (Europe)", Ext = ".st", Size = 737280L, Dir = "atarist", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "ATARIST", Game = "Xenon 2 (Europe)", Ext = ".stx", Size = 737280L, Dir = "atarist", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "ATARIST", Game = "Stunt Car Racer (Europe)", Ext = ".msa", Size = 737280L, Dir = "atarist", Tag = "unique-extension", Dat = "tosec" },
            // PC-98
            new { Sys = "PC98", Game = "Touhou Reiiden (Japan)", Ext = ".fdi", Size = 1261568L, Dir = "pc98", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "PC98", Game = "Rusty (Japan)", Ext = ".hdi", Size = 4194304L, Dir = "pc98", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "PC98", Game = "Ys III (Japan)", Ext = ".d88", Size = 1261568L, Dir = "pc98", Tag = "unique-extension", Dat = "tosec" },
            // X68K
            new { Sys = "X68K", Game = "Akumajou Dracula (Japan)", Ext = ".dim", Size = 1261568L, Dir = "x68000", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "X68K", Game = "Gradius (Japan)", Ext = ".xdf", Size = 1261568L, Dir = "x68000", Tag = "unique-extension", Dat = "tosec" },
            new { Sys = "X68K", Game = "Star Wars (Japan)", Ext = ".2hd", Size = 1261568L, Dir = "x68000", Tag = "unique-extension", Dat = "tosec" }
        };

        foreach (var f in computerFormats)
        {
            var id = NextId("ec", f.Sys, "format");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{f.Game}{f.Ext}", Extension = f.Ext, SizeBytes = f.Size, Directory = f.Dir },
                Tags = [f.Tag, f.Dat],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = f.Sys, Category = "Game", Confidence = f.Tag == "ambiguous-extension" ? 65 : 75,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = f.Dat, SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "UniqueExtension", AcceptableAlternatives = ["FolderName"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateKeywordOnlyDetection(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            new { Sys = "AMIGA", Game = "Turrican", Kw = "amiga", Ext = ".adf", Size = 901120L, AltKeys = Array.Empty<string>() },
            new { Sys = "AMIGA", Game = "The Settlers", Kw = "amiga", Ext = ".adf", Size = 901120L, AltKeys = Array.Empty<string>() },
            new { Sys = "C64", Game = "Impossible Mission", Kw = "c64", Ext = ".prg", Size = 16384L, AltKeys = Array.Empty<string>() },
            new { Sys = "C64", Game = "Maniac Mansion", Kw = "c64", Ext = ".d64", Size = 174848L, AltKeys = Array.Empty<string>() },
            new { Sys = "ZX", Game = "Jet Set Willy", Kw = "spectrum", Ext = ".tap", Size = 49152L, AltKeys = Array.Empty<string>() },
            new { Sys = "ZX", Game = "Manic Miner", Kw = "zxspectrum", Ext = ".tzx", Size = 32768L, AltKeys = Array.Empty<string>() },
            new { Sys = "MSX", Game = "Metal Gear", Kw = "msx", Ext = ".rom", Size = 131072L, AltKeys = Array.Empty<string>() },
            new { Sys = "MSX", Game = "Space Manbow", Kw = "msx2", Ext = ".rom", Size = 262144L, AltKeys = new[] { "MSX2" } },
            new { Sys = "ATARIST", Game = "Dungeon Master", Kw = "atarist", Ext = ".st", Size = 737280L, AltKeys = Array.Empty<string>() },
            new { Sys = "ATARIST", Game = "Carrier Command", Kw = "atari-st", Ext = ".stx", Size = 737280L, AltKeys = Array.Empty<string>() },
            new { Sys = "PC98", Game = "Touhou", Kw = "pc98", Ext = ".fdi", Size = 1261568L, AltKeys = Array.Empty<string>() },
            new { Sys = "X68K", Game = "Akumajou Dracula", Kw = "x68000", Ext = ".dim", Size = 1261568L, AltKeys = Array.Empty<string>() }
        };

        foreach (var e in entries)
        {
            var id = NextId("gr", e.Sys, "keyword");
            if (_existingIds.Contains(id)) continue;

            Add(result, "golden-realworld.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{e.Game}{e.Ext}", Extension = e.Ext, SizeBytes = e.Size, Directory = $"roms/{e.Kw}/games" },
                Tags = ["keyword-detection", "tosec"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = e.Sys, Category = "Game", Confidence = 55,
                    HasConflict = false, DatMatchLevel = "none", DatEcosystem = "tosec", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "Keyword",
                    AcceptableAlternatives = ["FolderName"],
                    AcceptableConsoleKeys = e.AltKeys.Length > 0 ? e.AltKeys : null
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    // ═══ PHASE M3 — Region/Revision + Corrupt ════════════════════════════

    private void GenerateM3RegionVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 262160L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Super Mario Bros 3" },
            new { Sys = "SNES", Ext = ".sfc", Size = 1048576L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "The Legend of Zelda - A Link to the Past" },
            new { Sys = "N64", Ext = ".z64", Size = 16777216L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "GoldenEye 007" },
            new { Sys = "GBA", Ext = ".gba", Size = 8388608L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Advance Wars" },
            new { Sys = "GB", Ext = ".gb", Size = 32768L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Tetris" },
            new { Sys = "GBC", Ext = ".gbc", Size = 2097152L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Pokemon Crystal" },
            new { Sys = "MD", Ext = ".md", Size = 2097152L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Streets of Rage 2" },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, Detect = "DiscHeader", Dat = "redump", Name = "Tekken 3" },
            new { Sys = "PS2", Ext = ".iso", Size = 4700000000L, Detect = "DiscHeader", Dat = "redump", Name = "God of War" },
            new { Sys = "SNES", Ext = ".sfc", Size = 3145728L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Final Fantasy VI" }
        };
        var regions = new[] { ("USA", "easy"), ("Europe", "easy"), ("Japan", "medium") };

        foreach (var g in games)
        {
            foreach (var (region, diff) in regions)
            {
                var id = NextId("gr", g.Sys, "region");
                if (_existingIds.Contains(id)) continue;

                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo { FileName = $"{g.Name} ({region}){g.Ext}", Extension = g.Ext, SizeBytes = g.Size, Directory = g.Sys.ToLower() },
                    Tags = ["region-variant", "clean-reference", g.Dat],
                    Difficulty = diff,
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = g.Sys, Category = "Game", Confidence = 95,
                        HasConflict = false, DatMatchLevel = "exact", DatEcosystem = g.Dat, SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = g.Detect, AcceptableAlternatives = ["FolderName", "DatMatch"]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" }
                });
            }
        }
    }

    private void GenerateM3RevisionVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var games = new[]
        {
            new { Sys = "NES", Ext = ".nes", Size = 40976L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Super Mario Bros" },
            new { Sys = "SNES", Ext = ".sfc", Size = 2621440L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Street Fighter II Turbo" },
            new { Sys = "MD", Ext = ".md", Size = 524288L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Sonic the Hedgehog" },
            new { Sys = "GBA", Ext = ".gba", Size = 16777216L, Detect = "CartridgeHeader", Dat = "no-intro", Name = "Pokemon Emerald" },
            new { Sys = "PS1", Ext = ".bin", Size = 734003200L, Detect = "DiscHeader", Dat = "redump", Name = "Gran Turismo" }
        };
        var revisions = new[] { ("", "easy"), ("(Rev 1)", "easy"), ("(Rev 2)", "medium") };

        foreach (var g in games)
        {
            foreach (var (rev, diff) in revisions)
            {
                var id = NextId("gr", g.Sys, "revision");
                if (_existingIds.Contains(id)) continue;

                var name = string.IsNullOrEmpty(rev) ? $"{g.Name} (USA)" : $"{g.Name} (USA) {rev}";
                Add(result, "golden-realworld.jsonl", new GroundTruthEntry
                {
                    Id = id,
                    Source = new SourceInfo { FileName = $"{name}{g.Ext}", Extension = g.Ext, SizeBytes = g.Size, Directory = g.Sys.ToLower() },
                    Tags = ["revision-variant", "clean-reference", g.Dat],
                    Difficulty = diff,
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = g.Sys, Category = "Game", Confidence = 95,
                        HasConflict = false, DatMatchLevel = "exact", DatEcosystem = g.Dat, SortDecision = "sort"
                    },
                    DetectionExpectations = new DetectionExpectations
                    {
                        PrimaryMethod = g.Detect, AcceptableAlternatives = ["FolderName", "DatMatch"]
                    },
                    FileModel = new FileModelInfo { Type = "single-file" }
                });
            }
        }
    }

    private void GenerateM3CorruptEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            new { Sys = "NES", Game = "Super Mario Bros (Corrupt Header)", Ext = ".nes", Size = 40976L, Dir = "nes", Tag = "corrupt-archive", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "SNES", Game = "Zelda (Truncated 50%)", Ext = ".sfc", Size = 524288L, Dir = "snes", Tag = "truncated-rom", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "GBA", Game = "Pokemon (Zero-filled)", Ext = ".gba", Size = 16777216L, Dir = "gba", Tag = "corrupt-archive", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "MD", Game = "Sonic (Random Corrupt)", Ext = ".md", Size = 524288L, Dir = "md", Tag = "corrupt-archive", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "PS1", Game = "Crash Bandicoot (Truncated)", Ext = ".bin", Size = 367001600L, Dir = "ps1", Tag = "truncated-rom", Diff = "adversarial", Det = "DiscHeader", DatLevel = "none" },
            new { Sys = "N64", Game = "Mario 64 (Header Mangled)", Ext = ".z64", Size = 8388608L, Dir = "n64", Tag = "corrupt-archive", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "GB", Game = "Tetris (Zero-filled ROM)", Ext = ".gb", Size = 32768L, Dir = "gb", Tag = "corrupt-archive", Diff = "adversarial", Det = "CartridgeHeader", DatLevel = "none" },
            new { Sys = "PS2", Game = "GTA SA (Incomplete ISO)", Ext = ".iso", Size = 2350000000L, Dir = "ps2", Tag = "truncated-rom", Diff = "adversarial", Det = "DiscHeader", DatLevel = "none" },
            new { Sys = "SNES", Game = "FF6 (Bad Checksum)", Ext = ".sfc", Size = 3145728L, Dir = "snes", Tag = "corrupt-archive", Diff = "hard", Det = "CartridgeHeader", DatLevel = "weak" },
            new { Sys = "GBC", Game = "Pokemon Gold (Bit Rot)", Ext = ".gbc", Size = 1048576L, Dir = "gbc", Tag = "corrupt-archive", Diff = "hard", Det = "CartridgeHeader", DatLevel = "weak" }
        };

        foreach (var e in entries)
        {
            var id = NextId("cm", e.Sys, "corrfile");
            if (_existingIds.Contains(id)) continue;

            Add(result, "chaos-mixed.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{e.Game}{e.Ext}", Extension = e.Ext, SizeBytes = e.Size, Directory = e.Dir },
                Tags = [e.Tag, "broken-set", "no-intro"],
                Difficulty = e.Diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = e.Sys, Category = "Game", Confidence = 30,
                    HasConflict = false, DatMatchLevel = e.DatLevel, DatEcosystem = "unknown", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = e.Det, AcceptableAlternatives = ["FolderName", "Heuristic"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    // ═══ PHASE M4 — SNES ROM-Types + Copier-Header ══════════════════════

    private void GenerateSnesRomTypes(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            new { Name = "Super Mario World (USA)", Size = 524288L, Rom = "LoROM", Diff = "medium", Tag = "lorom-mapping" },
            new { Name = "Donkey Kong Country (USA)", Size = 4194304L, Rom = "LoROM", Diff = "medium", Tag = "lorom-mapping" },
            new { Name = "The Legend of Zelda - A Link to the Past (USA)", Size = 1048576L, Rom = "HiROM", Diff = "medium", Tag = "hirom-mapping" },
            new { Name = "Chrono Trigger (USA)", Size = 4194304L, Rom = "HiROM", Diff = "medium", Tag = "hirom-mapping" },
            new { Name = "Tales of Phantasia (Japan)", Size = 6291456L, Rom = "ExHiROM", Diff = "hard", Tag = "exhirom-mapping" },
            new { Name = "Star Ocean (Japan)", Size = 6291456L, Rom = "ExHiROM", Diff = "hard", Tag = "exhirom-mapping" }
        };

        foreach (var e in entries)
        {
            var slug = e.Rom.ToLower().Replace("exhirom", "exhirom");
            var id = NextId("ec", "SNES", slug);
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{e.Name}.sfc", Extension = ".sfc", SizeBytes = e.Size, Directory = "snes" },
                Tags = ["cartridge-header", e.Tag, "snes-rom-type"],
                Difficulty = e.Diff,
                Expected = new ExpectedResult
                {
                    ConsoleKey = "SNES", Category = "Game", Confidence = 90,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = "no-intro", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["FolderName", "DatMatch"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateCopierHeaderEntries(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new[]
        {
            new { Sys = "NES", Name = "Super Mario Bros (USA) [Copier]", Ext = ".nes", Size = 16896L, Dir = "nes" },
            new { Sys = "NES", Name = "Mega Man 2 (USA) [Copier]", Ext = ".nes", Size = 131584L, Dir = "nes" },
            new { Sys = "SNES", Name = "Super Mario World (USA) [Copier]", Ext = ".smc", Size = 524800L, Dir = "snes" },
            new { Sys = "SNES", Name = "Donkey Kong Country (USA) [Copier]", Ext = ".smc", Size = 4194816L, Dir = "snes" },
            new { Sys = "MD", Name = "Sonic the Hedgehog (USA) [Copier]", Ext = ".md", Size = 524800L, Dir = "md" },
            new { Sys = "MD", Name = "Streets of Rage 2 (USA) [Copier]", Ext = ".md", Size = 2097664L, Dir = "md" },
            new { Sys = "SNES", Name = "Zelda - ALttP (USA) [Copier]", Ext = ".sfc", Size = 1049088L, Dir = "snes" },
            new { Sys = "SNES", Name = "Chrono Trigger (USA) [Copier]", Ext = ".sfc", Size = 4194816L, Dir = "snes" }
        };

        foreach (var e in entries)
        {
            var id = NextId("ec", e.Sys, "copier");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{e.Name}{e.Ext}", Extension = e.Ext, SizeBytes = e.Size, Directory = e.Dir },
                Tags = ["copier-header", "cartridge-header", "512-byte-prefix"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = e.Sys, Category = "Game", Confidence = 85,
                    HasConflict = false, DatMatchLevel = "fuzzy", DatEcosystem = "no-intro", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["FolderName", "DatMatch"]
                },
                FileModel = new FileModelInfo { Type = "single-file" }
            });
        }
    }

    private void GenerateSatDcCrossSystem(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var satGames = new[] { "Nights into Dreams", "Panzer Dragoon Saga", "Radiant Silvergun", "Burning Rangers" };
        var dcGames = new[] { "Sonic Adventure", "Jet Grind Radio", "Shenmue", "Power Stone" };

        for (int i = 0; i < 4; i++)
        {
            var satId = NextId("ec", "SAT", "crossiso");
            if (!_existingIds.Contains(satId))
            {
                Add(result, "edge-cases.jsonl", new GroundTruthEntry
                {
                    Id = satId,
                    Source = new SourceInfo { FileName = $"{satGames[i]} (USA).iso", Extension = ".iso", SizeBytes = 734003200, Directory = "saturn" },
                    Tags = ["cross-system-ambiguity", "disc-header", "redump"],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = "SAT", Category = "Game", Confidence = 70,
                        HasConflict = true, DatMatchLevel = "exact", DatEcosystem = "redump", SortDecision = "review"
                    },
                    DetectionExpectations = new DetectionExpectations { PrimaryMethod = "DiscHeader", AcceptableAlternatives = ["FolderName"] },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }

            var dcId = NextId("ec", "DC", "crossiso");
            if (!_existingIds.Contains(dcId))
            {
                Add(result, "edge-cases.jsonl", new GroundTruthEntry
                {
                    Id = dcId,
                    Source = new SourceInfo { FileName = $"{dcGames[i]} (USA).iso", Extension = ".iso", SizeBytes = 1073741824, Directory = "dreamcast" },
                    Tags = ["cross-system-ambiguity", "disc-header", "redump"],
                    Difficulty = "hard",
                    Expected = new ExpectedResult
                    {
                        ConsoleKey = "DC", Category = "Game", Confidence = 70,
                        HasConflict = true, DatMatchLevel = "exact", DatEcosystem = "redump", SortDecision = "review"
                    },
                    DetectionExpectations = new DetectionExpectations { PrimaryMethod = "DiscHeader", AcceptableAlternatives = ["FolderName"] },
                    FileModel = new FileModelInfo { Type = "single-file" },
                    Relationships = new RelationshipInfo()
                });
            }
        }
    }

    private void GeneratePcePcecdDisambiguation(Dictionary<string, List<GroundTruthEntry>> result)
    {
        var entries = new (string sys, string name, string ext, string dir, long size, int conf, string datMatch, string decision)[]
        {
            ("PCE",   "R-Type (USA).pce",            ".pce", "pce",   524288,     80, "exact", "sort"),
            ("PCE",   "Bonk-s Adventure (USA).pce",   ".pce", "turbografx", 524288, 70, "exact", "review"),
            ("PCECD", "Ys Book I II (USA).bin",       ".bin", "pcecd", 734003200,  70, "exact", "review"),
            ("PCECD", "Rondo of Blood (JPN).iso",     ".iso", "pcecd", 734003200,  75, "exact", "sort"),
            ("PCECD", "Gate of Thunder (USA).bin",     ".bin", "pce",   734003200,  60, "fuzzy", "review"),
        };

        foreach (var (sys, name, ext, dir, size, conf, datMatch, decision) in entries)
        {
            var id = NextId("ec", sys, "pcedisambig");
            if (_existingIds.Contains(id)) continue;

            var primary = sys == "PCE" ? "UniqueExtension" : "DiscHeader";
            var alts = sys == "PCE" && dir == "pce" ? new[] { "FolderName" } : sys == "PCECD" ? new[] { "FolderName" } : Array.Empty<string>();
            var datEco = sys == "PCE" ? "no-intro" : "redump";
            var tags = new List<string> { "cross-system-ambiguity", "pce-pcecd-disambiguation" };
            if (sys == "PCECD") tags.Add("disc-header");
            tags.Add(datEco);

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = name, Extension = ext, SizeBytes = size, Directory = dir },
                Tags = tags.ToArray(),
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = sys, Category = "Game", Confidence = conf,
                    HasConflict = true, DatMatchLevel = datMatch, DatEcosystem = datEco, SortDecision = decision
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = primary, AcceptableAlternatives = alts },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    private void GenerateNesInesVariants(Dictionary<string, List<GroundTruthEntry>> result)
    {
        // 3x iNES v1
        var inesGames = new[] { "Mega Man 2", "Castlevania", "Metroid" };
        foreach (var game in inesGames)
        {
            var id = NextId("ec", "NES", "inesvar");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{game} (USA).nes", Extension = ".nes", SizeBytes = 262160, Directory = "nes" },
                Tags = ["cartridge-header", "no-intro"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "NES", Category = "Game", Confidence = 95,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = "no-intro", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["UniqueExtension"] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }

        // 3x NES 2.0
        var nes2Games = new[] { "Castlevania III", "Battletoads", "Contra" };
        foreach (var game in nes2Games)
        {
            var id = NextId("ec", "NES", "inesvar");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{game} (USA).nes", Extension = ".nes", SizeBytes = 524304, Directory = "nes" },
                Tags = ["cartridge-header", "no-intro"],
                Difficulty = "medium",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "NES", Category = "Game", Confidence = 95,
                    HasConflict = false, DatMatchLevel = "exact", DatEcosystem = "no-intro", SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "CartridgeHeader", AcceptableAlternatives = ["UniqueExtension"] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }

        // 2x headerless NES raw
        var rawGames = new[] { "Duck Hunt", "Excitebike" };
        foreach (var game in rawGames)
        {
            var id = NextId("ec", "NES", "inesvar");
            if (_existingIds.Contains(id)) continue;

            Add(result, "edge-cases.jsonl", new GroundTruthEntry
            {
                Id = id,
                Source = new SourceInfo { FileName = $"{game} (USA).bin", Extension = ".bin", SizeBytes = 32768, Directory = "nes" },
                Tags = ["headerless", "no-intro"],
                Difficulty = "hard",
                Expected = new ExpectedResult
                {
                    ConsoleKey = "NES", Category = "Game", Confidence = 55,
                    HasConflict = true, DatMatchLevel = "fuzzy", DatEcosystem = "no-intro", SortDecision = "review"
                },
                DetectionExpectations = new DetectionExpectations { PrimaryMethod = "FolderName", AcceptableAlternatives = [] },
                FileModel = new FileModelInfo { Type = "single-file" },
                Relationships = new RelationshipInfo()
            });
        }
    }

    // ═══ HELPERS ═════════════════════════════════════════════════════════

    private string NextId(string prefix, string system, string subclass)
    {
        var key = $"{prefix}-{system}-{subclass}";
        var counter = _idCounters.GetValueOrDefault(key, 0) + 1;
        _idCounters[key] = counter;
        return $"{prefix}-{system}-{subclass}-{counter:D3}";
    }

    private static void Add(Dictionary<string, List<GroundTruthEntry>> result,
        string file, GroundTruthEntry entry)
    {
        if (!result.TryGetValue(file, out var list))
        {
            list = [];
            result[file] = list;
        }
        list.Add(entry);
    }

    private static string GetPrimaryExtension(SystemDef sys)
    {
        if (sys.UniqueExts.Length > 0) return sys.UniqueExts[0];
        if (sys.AmbigExts.Length > 0) return sys.AmbigExts[0];
        return ".bin";
    }

    private static string[] BuildTags(string? primaryTag, SystemDef sys)
    {
        var tags = new List<string>();
        if (primaryTag is not null) tags.Add(primaryTag);

        if (sys.HasCartridgeHeader) tags.Add("cartridge-header");
        else if (sys.DiscBased) tags.Add("disc-header");
        if (sys.UniqueExts.Length > 0) tags.Add("unique-extension");

        tags.Add(sys.DatEcosystem);
        return tags.ToArray();
    }

    private static string[] GetAlternatives(SystemDef sys)
    {
        var alts = new List<string>();
        if (sys.UniqueExts.Length > 0 && sys.PrimaryDetection != "UniqueExtension")
            alts.Add("UniqueExtension");
        if (sys.PrimaryDetection != "FolderName")
            alts.Add("FolderName");
        return alts.ToArray();
    }

    private static int GetTierCount(string key, int t1, int t2, int t3, int t4)
    {
        if (Tier1.Contains(key)) return t1;
        if (Tier2.Contains(key)) return t2;
        if (Tier3.Contains(key)) return t3;
        return t4;
    }

    private static bool IsTier1Or2(string key) => Tier1.Contains(key) || Tier2.Contains(key);

    private static readonly HashSet<string> Tier1 = new(StringComparer.Ordinal)
    { "NES", "SNES", "N64", "GBA", "GB", "GBC", "MD", "PS1", "PS2" };

    private static readonly HashSet<string> Tier2 = new(StringComparer.Ordinal)
    { "32X", "PSP", "SAT", "DC", "GC", "WII", "SMS", "GG", "PCE", "LYNX",
      "A78", "A26", "NDS", "3DS", "SWITCH", "AMIGA" };

    private static readonly HashSet<string> Tier3 = new(StringComparer.Ordinal)
    { "PCECD", "PCFX", "SCD", "NEOCD", "CD32", "CDI", "JAGCD", "FMTOWNS",
      "3DO", "ATARIST", "C64", "MSX", "ZX", "COLECO", "INTV", "VB",
      "VECTREX", "A52", "NGP", "WS", "WSC", "ODYSSEY2" };

    private static SystemDef[] BuildSystemCatalog()
    {
        return
        [
            S("3DO",   true, [],                    [".iso",".bin",".chd"],  "3do",        "DiscHeader",       "redump",  false, ["Road Rash","Need for Speed"],                      734003200),
            S("3DS",   false,[".3ds",".cia"],        [],                     "3ds",        "UniqueExtension",  "no-intro",false, ["Pokemon X","Animal Crossing New Leaf"],             2147483648),
            S("32X",   false,[".32x"],               [],                     "32x",        "UniqueExtension",  "no-intro",true,  ["Doom","Knuckles Chaotix","Star Wars Arcade"],       3145728),
            S("A26",   false,[".a26"],               [],                     "a26",        "UniqueExtension",  "no-intro",false, ["Combat","Pitfall","Adventure"],                     4096),
            S("A52",   false,[".a52"],               [],                     "a52",        "UniqueExtension",  "no-intro",false, ["Pac-Man","Moon Patrol"],                            16384),
            S("A78",   false,[".a78"],               [],                     "a78",        "UniqueExtension",  "no-intro",true,  ["Asteroids","Dig Dug","Food Fight"],                 65536),
            S("A800",  false,[".atr",".xex",".xfd"], [],                     "a800",       "UniqueExtension",  "tosec",   false, ["Star Raiders","MULE","Archon"],                     131072),
            S("AMIGA", false,[".adf"],               [],                     "amiga",      "UniqueExtension",  "tosec",   false, ["Turrican II","Lemmings","Speedball 2"],             901120),
            S("ARCADE",false,[],                     [],                     "arcade",     "FolderName",       "mame",    false, ["pacman","dkong","galaga","sf2","tmnt"],              3145728),
            S("ATARIST",false,[".st",".stx"],        [],                     "atarist",    "UniqueExtension",  "tosec",   false, ["Dungeon Master","Starglider"],                     737280),
            S("C64",   false,[".d64",".t64"],        [],                     "c64",        "UniqueExtension",  "tosec",   false, ["Impossible Mission","Maniac Mansion"],              174848),
            S("CD32",  true, [],                     [".iso",".bin",".chd"], "cd32",       "DiscHeader",       "tosec",   false, ["Microcosm","Super Stardust"],                       734003200),
            S("CDI",   true, [".cdi"],               [".iso",".bin",".chd"], "cdi",        "UniqueExtension",  "redump",  false, ["Hotel Mario","Zelda Wand of Gamelon"],              734003200),
            S("CHANNELF",false,[],                   [],                     "channelf",   "FolderName",       "no-intro",false, ["Hockey","Tennis"],                                  2048),
            S("COLECO",false,[".col"],               [],                     "coleco",     "UniqueExtension",  "no-intro",false, ["Donkey Kong","Zaxxon"],                             16384),
            S("CPC",   false,[],                     [],                     "cpc",        "FolderName",       "tosec",   false, ["Gryzor","Rick Dangerous"],                          65536),
            S("DC",    true, [".gdi"],               [".iso",".bin",".chd"], "dc",         "UniqueExtension",  "redump",  false, ["Sonic Adventure","Shenmue","Jet Grind Radio"],      1073741824),
            S("DOS",   false,[],                     [],                     "dos",        "FolderName",       "tosec",   false, ["DOOM","Commander Keen","Prince of Persia"],          1048576),
            S("FMTOWNS",true,[],                    [".iso",".bin",".chd"], "fmtowns",    "DiscHeader",       "tosec",   false, ["Zak McKracken FM","After Burner"],                  734003200),
            S("GB",    false,[".gb"],                [],                     "gb",         "CartridgeHeader",  "no-intro",true,  ["Tetris","Super Mario Land","Pokemon Red"],           32768),
            S("GBA",   false,[".gba"],               [],                     "gba",        "CartridgeHeader",  "no-intro",true,  ["Pokemon FireRed","Metroid Fusion","Minish Cap"],    16777216),
            S("GBC",   false,[".gbc"],               [],                     "gbc",        "CartridgeHeader",  "no-intro",true,  ["Pokemon Crystal","Links Awakening DX"],             2097152),
            S("GC",    true, [],                     [".iso",".gcz",".rvz"], "gc",         "DiscHeader",       "redump",  false, ["Melee","Wind Waker","Metroid Prime"],               1459617792),
            S("GG",    false,[".gg"],                [],                     "gg",         "UniqueExtension",  "no-intro",false, ["Sonic Chaos","Columns","Shinobi"],                  262144),
            S("INTV",  false,[".int"],               [],                     "intv",       "UniqueExtension",  "no-intro",false, ["Astrosmash","Utopia"],                              16384),
            S("JAG",   false,[".j64"],               [],                     "jag",        "UniqueExtension",  "no-intro",false, ["Tempest 2000","Rayman"],                            4194304),
            S("JAGCD", true, [],                     [".iso",".bin",".chd"], "jaguarcd",   "DiscHeader",       "redump",  false, ["Myst","Battlemorph"],                               734003200),
            S("LYNX",  false,[".lnx"],               [],                     "lynx",       "CartridgeHeader",  "no-intro",true,  ["California Games","Todd-s Adventures","Chips Challenge"], 262144),
            S("MD",    false,[".md",".gen"],          [],                     "md",         "CartridgeHeader",  "no-intro",true,  ["Sonic The Hedgehog","Streets of Rage 2","Gunstar Heroes"], 1048576),
            S("MSX",   false,[".mx1",".mx2"],        [],                     "msx",        "UniqueExtension",  "tosec",   false, ["Nemesis","Metal Gear"],                             131072),
            S("N64",   false,[".n64",".z64",".v64"], [],                     "n64",        "CartridgeHeader",  "no-intro",true,  ["Super Mario 64","Ocarina of Time","GoldenEye 007"], 8388608),
            S("NDS",   false,[".nds"],               [],                     "nds",        "UniqueExtension",  "no-intro",false, ["New Super Mario Bros","Pokemon Diamond"],            134217728),
            S("NEOCD", true, [],                     [".iso",".bin",".chd"], "neocd",      "DiscHeader",       "redump",  false, ["Viewpoint","Samurai Shodown RPG"],                  734003200),
            S("NEOGEO",false,[],                     [],                     "neogeo",     "FolderName",       "mame",    false, ["kof98","mslug","samsho2","garou"],                   33554432),
            S("NES",   false,[".nes"],               [],                     "nes",        "CartridgeHeader",  "no-intro",true,  ["Super Mario Bros","Zelda","Mega Man 2","Castlevania","Metroid"], 40976),
            S("NGP",   false,[".ngp"],               [],                     "ngp",        "UniqueExtension",  "no-intro",false, ["SNK vs Capcom Match","Neo Turf Masters"],           1048576),
            S("NGPC",  false,[],                     [],                     "ngpc",       "FolderName",       "no-intro",false, ["SNK vs Capcom Card","Metal Slug 1st"],               2097152),
            S("ODYSSEY2",false,[".o2"],              [],                     "odyssey2",   "UniqueExtension",  "no-intro",false, ["Quest for Rings","KC Munchkin"],                    4096),
            S("PC98",  false,[],                     [],                     "pc98",       "FolderName",       "tosec",   false, ["Ys IV","Policenauts"],                               1048576),
            S("PCE",   false,[".pce"],               [],                     "pce",        "UniqueExtension",  "no-intro",false, ["R-Type","Bonk-s Adventure"],                         524288),
            S("PCECD", true, [],                     [".iso",".bin",".chd"], "pcecd",      "DiscHeader",       "redump",  false, ["Ys Book I II","Rondo of Blood"],                    734003200),
            S("PCFX",  true, [".pcfx"],              [".iso",".bin",".chd"], "pcfx",       "UniqueExtension",  "redump",  false, ["Battle Heat","Zenki"],                              734003200),
            S("POKEMINI",false,[".min"],             [],                     "pokemini",   "UniqueExtension",  "no-intro",false, ["Pokemon Party Mini","Pokemon Zany Cards"],           65536),
            S("PS1",   true, [],                     [".iso",".bin",".chd"], "ps1",        "DiscHeader",       "redump",  false, ["Crash Bandicoot","FF VII","MGS","Castlevania SOTN","Resident Evil 2"], 734003200),
            S("PS2",   true, [],                     [".iso",".bin",".chd"], "ps2",        "DiscHeader",       "redump",  false, ["GTA San Andreas","FF X","Kingdom Hearts","Shadow of the Colossus","MGS 3"], 4700000000),
            S("PS3",   true, [],                     [".iso",".bin",".chd"], "ps3",        "DiscHeader",       "redump",  false, ["Uncharted 2","The Last of Us"],                     25769803776),
            S("PSP",   true, [],                     [".iso",".cso"],        "psp",        "DiscHeader",       "redump",  false, ["God of War Chains of Olympus","Lumines"],            1800000000),
            S("SAT",   true, [],                     [".iso",".bin",".chd"], "sat",        "DiscHeader",       "redump",  false, ["Nights into Dreams","Panzer Dragoon Saga"],          734003200),
            S("SCD",   true, [],                     [".iso",".bin",".chd"], "scd",        "DiscHeader",       "redump",  false, ["Sonic CD","Lunar"],                                 734003200),
            S("SG1000",false,[".sg",".sc"],          [],                     "sg1000",     "UniqueExtension",  "no-intro",false, ["Congo Bongo","Gulkave"],                            16384),
            S("SMS",   false,[".sms"],               [],                     "sms",        "UniqueExtension",  "no-intro",false, ["Alex Kidd","Phantasy Star","Sonic the Hedgehog MS"], 262144),
            S("SNES",  false,[".sfc",".smc"],        [],                     "snes",       "CartridgeHeader",  "no-intro",true,  ["Super Mario World","Link to the Past","Super Metroid","Chrono Trigger","FF VI"], 524288),
            S("SUPERVISION",false,[],                [],                     "supervision","FolderName",       "no-intro",false, ["Crystball","Alien"],                                32768),
            S("SWITCH",false,[".nsp",".xci"],        [],                     "switch",     "UniqueExtension",  "no-intro",false, ["BotW","Mario Odyssey","Animal Crossing NH"],         16106127360),
            S("VB",    false,[".vb"],                [],                     "vb",         "UniqueExtension",  "no-intro",false, ["Virtual Boy Wario Land","Mario Clash"],              1048576),
            S("VECTREX",false,[".vec"],              [],                     "vectrex",    "UniqueExtension",  "no-intro",false, ["Mine Storm","Scramble"],                             8192),
            S("VITA",  false,[".vpk"],               [],                     "vita",       "UniqueExtension",  "no-intro",false, ["Persona 4 Golden","Tearaway"],                      4294967296),
            S("WII",   true, [".wbfs",".wad"],       [".iso",".gcz",".rvz"], "wii",        "UniqueExtension",  "redump",  false, ["Mario Galaxy","Twilight Princess Wii","Smash Brawl"], 4699979776),
            S("WIIU",  true, [".wux",".rpx"],        [".iso",".bin",".chd"], "wiiu",       "UniqueExtension",  "redump",  false, ["BotW WiiU","Mario Kart 8"],                         25769803776),
            S("WS",    false,[".ws"],                [],                     "ws",         "UniqueExtension",  "no-intro",false, ["Gunpey","Final Fantasy WS"],                        4194304),
            S("WSC",   false,[".wsc"],               [],                     "wsc",        "UniqueExtension",  "no-intro",false, ["Final Fantasy I WSC","SD Gundam"],                  4194304),
            S("X360",  true, [],                     [".iso",".bin",".chd"], "x360",       "DiscHeader",       "redump",  false, ["Halo 3","Gears of War"],                            7835492352),
            S("X68K",  false,[],                     [],                     "x68k",       "FolderName",       "tosec",   false, ["Akumajou Dracula","Gradius"],                        1048576),
            S("XBOX",  true, [],                     [".iso",".bin",".chd"], "xbox",       "DiscHeader",       "redump",  false, ["Halo CE","Fable"],                                  4700000000),
            S("ZX",    false,[".tzx"],               [],                     "zx",         "UniqueExtension",  "tosec",   false, ["Manic Miner","Jet Set Willy"],                      32768),
            // ── Phase 2 expansion: priority systems with uniqueExts ──
            S("BSX",   false,[".bs"],                [],                     "bsx",        "UniqueExtension",  "no-intro",false, ["BS Zelda","Radical Dreamers"],                       262144),
            S("FDS",   false,[".fds"],               [],                     "fds",        "UniqueExtension",  "no-intro",false, ["Zelda no Densetsu","Metroid FDS"],                   65536),
            S("GAMECOM",false,[".tgc"],              [],                     "gamecom",    "UniqueExtension",  "no-intro",false, ["Sonic Jam","Resident Evil 2 GC"],                    524288),
            S("GP32",  false,[".gxb"],               [],                     "gp32",       "UniqueExtension",  "no-intro",false, ["Pinball Dreams","Bubblex"],                          4194304),
            S("N64DD", false,[".ndd"],               [],                     "n64dd",      "UniqueExtension",  "no-intro",false, ["Mario Artist","SimCity 64 DD"],                      67108864),
            S("SGX",   false,[".sgx"],               [],                     "sgx",        "UniqueExtension",  "no-intro",false, ["Aldynes","1941 Counter Attack"],                     524288),
            S("GP2X",  false,[".gpe"],               [],                     "gp2x",       "UniqueExtension",  "no-intro",false, ["Sqdef","Vektar"],                                   4194304),
            S("STUDIO2",false,[".st2"],              [],                     "studio2",    "UniqueExtension",  "no-intro",false, ["SpacEwar","Doodle"],                                2048),
            S("NDSI",  false,[".dsi"],               [".nds"],               "dsi",        "UniqueExtension",  "no-intro",false, ["WarioWare Snapped","Zelda Four Swords AE"],          134217728),
            S("PS4",   true, [".pkg"],               [".iso",".bin"],        "ps4",        "UniqueExtension",  "redump",  false, ["Bloodborne","Uncharted 4"],                          50000000000),
            S("MSX2",  false,[],                     [".dsk",".cas"],        "msx2",       "FolderName",       "tosec",   false, ["Space Manbow","Snatcher MSX2"],                      131072),
            S("NAOMI", true, [],                     [".iso",".bin",".chd"], "naomi",      "FolderName",       "mame",    false, ["Crazy Taxi","Marvel vs Capcom 2"],                   734003200),
            S("AWAVE", false,[],                     [],                     "atomiswave", "FolderName",       "mame",    false, ["Dolphin Blue","Metal Slug 6"],                       134217728),
        ];
    }

    private static SystemDef S(string key, bool disc, string[] uExts, string[] aExts,
        string folder, string detection, string datEco, bool hasHeader,
        string[] games, long size)
        => new(key, disc, uExts, aExts, folder, detection, datEco, hasHeader, games, size);

    /// <summary>
    /// Writes expanded entries to JSONL files, merging with existing entries.
    /// </summary>
    public static void WriteToFiles(
        Dictionary<string, List<GroundTruthEntry>> existingByFile,
        Dictionary<string, List<GroundTruthEntry>> generated)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };

        var allFiles = existingByFile.Keys.Union(generated.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var file in allFiles)
        {
            var entries = new List<GroundTruthEntry>();
            if (existingByFile.TryGetValue(file, out var existing))
                entries.AddRange(existing);
            if (generated.TryGetValue(file, out var gen))
                entries.AddRange(gen);

            var path = Path.Combine(BenchmarkPaths.GroundTruthDir, file);
            var lines = entries.Select(e => JsonSerializer.Serialize(e, options));
            File.WriteAllLines(path, lines);
        }
    }
}
