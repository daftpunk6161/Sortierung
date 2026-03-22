using System.Text.Json;
using System.Text.Json.Serialization;
using RomCleanup.Tests.Benchmark.Models;

namespace RomCleanup.Tests.Benchmark.Infrastructure;

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
                    AcceptableAlternatives = [sys.PrimaryDetection]
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
