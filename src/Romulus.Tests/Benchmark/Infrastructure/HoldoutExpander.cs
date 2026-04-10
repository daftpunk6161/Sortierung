using System.Text.Json;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Expands holdout dataset to ~200 entries with ≥30% chaos/hard entries.
/// Holdout entries use "ho-" prefix and "holdout" tag to distinguish from
/// main ground-truth sets. Deterministic output via fixed seed.
/// </summary>
internal sealed class HoldoutExpander
{
    private static readonly string[] Systems =
    [
        "NES", "SNES", "N64", "GBA", "GB", "GBC", "MD", "PS1", "PS2",
        "32X", "PSP", "SAT", "DC", "GC", "WII", "SMS", "GG", "PCE",
        "LYNX", "A78", "A26", "NDS", "3DS", "SWITCH",
        "PCECD", "SCD", "NEOCD", "3DO",
        "ATARIST", "C64", "MSX", "AMIGA", "DOS", "ZX",
        "ARCADE", "NEOGEO"
    ];

    private static readonly (string FileName, string Ext, string Dir, string System, string Category, string[] Tags, string Difficulty)[] ChaosTemplates =
    [
        ("Random File (Beta).nes", ".nes", "nes", "NES", "Junk", new[] { "junk", "chaos", "holdout" }, "medium"),
        ("Weird Hack v2 [h].sfc", ".sfc", "snes", "SNES", "Junk", new[] { "hack", "chaos", "holdout" }, "hard"),
        ("Unknown Archive.zip", ".zip", "unsorted", "ARCADE", "Game", new[] { "arcade-parent", "chaos", "holdout" }, "hard"),
        ("Demo Version (Europe).gba", ".gba", "gba", "GBA", "NonGame", new[] { "demo", "chaos", "holdout" }, "medium"),
        ("BIOS (Japan).bin", ".bin", "ps1", "PS1", "Bios", new[] { "bios", "chaos", "holdout" }, "hard"),
        ("Wrong Folder Game.z64", ".z64", "snes", "N64", "Game", new[] { "folder-vs-header-conflict", "chaos", "holdout" }, "hard"),
        ("PS2 Game (USA).iso", ".iso", "ps1", "PS2", "Game", new[] { "ps-disambiguation", "chaos", "holdout" }, "hard"),
        ("Corrupted ROM.nes", ".nes", "nes", "NES", "Junk", new[] { "truncated-rom", "chaos", "holdout" }, "adversarial"),
        ("Homebrew Game (PD).gb", ".gb", "gb", "GB", "NonGame", new[] { "homebrew", "chaos", "holdout" }, "medium"),
        ("Multi Disc 1.cue", ".cue", "ps1", "PS1", "Game", new[] { "multi-disc", "cue-bin", "chaos", "holdout" }, "hard"),
    ];

    /// <summary>
    /// Generates additional holdout entries to reach targetTotal.
    /// Returns only the NEW entries (does not include existing ones).
    /// </summary>
    public List<GroundTruthEntry> GenerateExpansion(int existingCount, int targetTotal = 200)
    {
        var needed = targetTotal - existingCount;
        if (needed <= 0) return [];

        var entries = new List<GroundTruthEntry>(needed);
        var random = new Random(1337); // Deterministic

        // Ensure ≥30% of NEW entries are chaos/hard
        int chaosTarget = (int)(needed * 0.35);
        int chaosCount = 0;
        int seq = existingCount + 1;

        for (int i = 0; i < needed; i++)
        {
            bool makeChaos = chaosCount < chaosTarget && (random.NextDouble() < 0.4 || i >= needed - (chaosTarget - chaosCount));

            if (makeChaos)
            {
                entries.Add(BuildChaosEntry(random, seq));
                chaosCount++;
            }
            else
            {
                entries.Add(BuildCleanEntry(random, seq));
            }
            seq++;
        }

        return entries;
    }

    /// <summary>
    /// Appends new entries to the holdout JSONL file.
    /// </summary>
    public int WriteExpansion(List<GroundTruthEntry> newEntries)
    {
        if (newEntries.Count == 0) return 0;

        var dir = Path.GetDirectoryName(BenchmarkPaths.HoldoutJsonlPath)!;
        Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        using var writer = new StreamWriter(BenchmarkPaths.HoldoutJsonlPath, append: true);
        foreach (var entry in newEntries)
        {
            writer.WriteLine(JsonSerializer.Serialize(entry, options));
        }

        return newEntries.Count;
    }

    private GroundTruthEntry BuildChaosEntry(Random random, int seq)
    {
        var template = ChaosTemplates[random.Next(ChaosTemplates.Length)];
        var system = template.System;
        var ext = template.Ext;

        return new GroundTruthEntry
        {
            Id = $"ho-{system}-chaos-{seq:D4}",
            Source = new SourceInfo
            {
                FileName = $"{template.FileName.Replace(".nes", "").Replace(".sfc", "").Replace(".zip", "").Replace(".gba", "").Replace(".bin", "").Replace(".z64", "").Replace(".iso", "").Replace(".gb", "").Replace(".cue", "")} {seq}{ext}",
                Extension = ext,
                SizeBytes = 1024 * (1 + random.Next(32768)),
                Directory = template.Dir,
                Stub = GetStubForSystem(system) is { } gen ? new StubInfo { Generator = gen } : null
            },
            Tags = template.Tags,
            Difficulty = template.Difficulty,
            Expected = new ExpectedResult
            {
                ConsoleKey = system,
                Category = template.Category,
                HasConflict = template.Tags.Contains("folder-vs-header-conflict") || template.Tags.Contains("ps-disambiguation"),
                SortDecision = template.Category is "Junk" or "NonGame" ? "skip" : "sort"
            },
            SchemaVersion = "2.0.0",
            AddedInVersion = "0.2.0",
            LastVerified = "2026-03-25"
        };
    }

    private GroundTruthEntry BuildCleanEntry(Random random, int seq)
    {
        var system = Systems[random.Next(Systems.Length)];
        var ext = GetExtensionForSystem(system);
        var region = random.Next(4) switch { 0 => "USA", 1 => "Europe", 2 => "Japan", _ => "World" };

        return new GroundTruthEntry
        {
            Id = $"ho-{system}-clean-{seq:D4}",
            Source = new SourceInfo
            {
                FileName = $"Holdout Game {seq} ({region}){ext}",
                Extension = ext,
                SizeBytes = 1024 * (1 + random.Next(65536)),
                Directory = system.ToLowerInvariant(),
                Stub = GetStubForSystem(system) is { } gen ? new StubInfo { Generator = gen } : null
            },
            Tags = ["clean-reference", "holdout"],
            Difficulty = random.NextDouble() < 0.3 ? "medium" : "easy",
            Expected = new ExpectedResult
            {
                ConsoleKey = system,
                Category = "Game",
                HasConflict = false,
                SortDecision = "sort"
            },
            SchemaVersion = "2.0.0",
            AddedInVersion = "0.2.0",
            LastVerified = "2026-03-25"
        };
    }

    private static string GetExtensionForSystem(string system) => system switch
    {
        "NES" => ".nes", "SNES" => ".sfc", "N64" => ".z64", "GBA" => ".gba",
        "GB" => ".gb", "GBC" => ".gbc", "MD" or "32X" => ".md",
        "PS1" or "PS2" or "SAT" or "DC" or "3DO" or "NEOCD" or "SCD" or "PCECD" => ".bin",
        "PSP" => ".cso", "GC" => ".gcm", "WII" => ".wbfs",
        "SMS" => ".sms", "GG" => ".gg", "PCE" => ".pce",
        "LYNX" => ".lnx", "A78" => ".a78", "A26" => ".a26",
        "NDS" => ".nds", "3DS" => ".3ds", "SWITCH" => ".nsp",
        "ATARIST" => ".st", "C64" => ".d64", "MSX" => ".rom",
        "AMIGA" => ".adf", "DOS" => ".exe", "ZX" => ".tap",
        "ARCADE" or "NEOGEO" => ".zip",
        _ => ".bin"
    };

    private static string? GetStubForSystem(string system) => system switch
    {
        "NES" => "nes-ines", "SNES" => "snes-header", "N64" => "n64-header",
        "GBA" => "gba-header", "GB" or "GBC" => "gb-header", "MD" or "32X" => "md-header",
        "PS1" => "ps1-pvd", "PS2" => "ps2-pvd",
        "SAT" or "DC" or "SCD" => "sega-ipbin",
        "GC" or "WII" => "nintendo-disc",
        "LYNX" => "lynx-header", "A78" => "a7800-header",
        _ => null
    };
}
