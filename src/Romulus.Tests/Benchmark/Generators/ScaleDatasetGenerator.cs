using System.Text.Json;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Generates a large-scale synthetic dataset (5000+ entries) for throughput and
/// performance regression testing. Entries are NOT added to the main ground-truth
/// sets; they go into a dedicated performance-scale.jsonl file.
/// </summary>
internal sealed class ScaleDatasetGenerator
{
    private static readonly string[] AllSystems =
    [
        "NES", "SNES", "N64", "GBA", "GB", "GBC", "MD", "PS1", "PS2",
        "32X", "PSP", "SAT", "DC", "GC", "WII", "SMS", "GG", "PCE",
        "LYNX", "A78", "A26", "NDS", "3DS", "SWITCH",
        "PCECD", "PCFX", "SCD", "NEOCD", "3DO",
        "ATARIST", "C64", "MSX", "AMIGA", "DOS", "ZX",
        "COLECO", "INTV", "VB", "VECTREX", "A52", "NGP", "WS", "WSC"
    ];

    private static readonly string[] Extensions =
    [
        ".nes", ".sfc", ".z64", ".gba", ".gb", ".gbc", ".md", ".bin", ".iso",
        ".32x", ".cso", ".cue", ".gdi", ".gcm", ".wbfs", ".sms", ".gg",
        ".pce", ".lnx", ".a78", ".a26", ".nds", ".3ds", ".nsp",
        ".cdi", ".img", ".adf", ".d64", ".dsk", ".tap",
        ".col", ".int", ".vb", ".vec", ".a52", ".ngp", ".ws", ".wsc"
    ];

    private static readonly string[] Regions =
        ["USA", "Europe", "Japan", "World", "Korea", "Germany", "France", "Spain"];

    private static readonly string[] Difficulties = ["easy", "easy", "easy", "medium", "medium", "hard"];

    public string OutputPath => Path.Combine(BenchmarkPaths.GroundTruthDir, "performance-scale.jsonl");

    /// <summary>
    /// Generates a specified number of synthetic entries for scale testing.
    /// </summary>
    public IReadOnlyList<GroundTruthEntry> Generate(int count = 5000)
    {
        var entries = new List<GroundTruthEntry>(count);
        var random = new Random(42); // Deterministic seed
        var perSystemSeq = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 1; i <= count; i++)
        {
            string system = AllSystems[random.Next(AllSystems.Length)];
            string region = Regions[random.Next(Regions.Length)];
            string difficulty = Difficulties[random.Next(Difficulties.Length)];
            string ext = GetExtensionForSystem(system);
            string gameName = $"Scale Test Game {i:D5} ({region})";

            perSystemSeq.TryGetValue(system, out int sysSeq);
            sysSeq++;
            perSystemSeq[system] = sysSeq;

            string? stubGen = GetStubForSystem(system);
            var entry = new GroundTruthEntry
            {
                Id = $"ps-{system}-scale-{sysSeq:D4}",
                Source = new SourceInfo
                {
                    FileName = $"{gameName}{ext}",
                    Extension = ext,
                    SizeBytes = 1024 * (1 + random.Next(65536)),
                    Stub = stubGen is not null ? new StubInfo { Generator = stubGen } : null
                },
                Tags = ["clean-reference"],
                Difficulty = difficulty,
                Expected = new ExpectedResult
                {
                    ConsoleKey = system,
                    Category = "Game",
                    SortDecision = "sort"
                },
                DetectionExpectations = new DetectionExpectations
                {
                    PrimaryMethod = GetPrimaryMethodForSystem(system)
                },
                Notes = $"Auto-generated scale entry #{i}"
            };

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Generates and writes the scale dataset to disk.
    /// </summary>
    public int WriteToFile(int count = 5000)
    {
        var entries = Generate(count);
        var dir = Path.GetDirectoryName(OutputPath)!;
        Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(OutputPath, append: false);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        foreach (var entry in entries)
        {
            writer.WriteLine(JsonSerializer.Serialize(entry, options));
        }

        return entries.Count;
    }

    private static string GetExtensionForSystem(string system) => system switch
    {
        "NES" => ".nes",
        "SNES" => ".sfc",
        "N64" => ".z64",
        "GBA" => ".gba",
        "GB" => ".gb",
        "GBC" => ".gbc",
        "MD" => ".md",
        "PS1" or "PS2" or "SAT" or "DC" or "3DO" or "NEOCD" or "SCD" or "PCECD" or "PCFX" => ".bin",
        "PSP" => ".cso",
        "GC" => ".gcm",
        "WII" => ".wbfs",
        "SMS" => ".sms",
        "GG" => ".gg",
        "PCE" => ".pce",
        "LYNX" => ".lnx",
        "A78" => ".a78",
        "A26" => ".a26",
        "NDS" => ".nds",
        "3DS" => ".3ds",
        "SWITCH" => ".nsp",
        "32X" => ".32x",
        "ATARIST" => ".st",
        "C64" => ".d64",
        "MSX" => ".rom",
        "AMIGA" => ".adf",
        "DOS" => ".exe",
        "ZX" => ".tap",
        "COLECO" => ".col",
        "INTV" => ".int",
        "VB" => ".vb",
        "VECTREX" => ".vec",
        "A52" => ".a52",
        "NGP" => ".ngp",
        "WS" => ".ws",
        "WSC" => ".wsc",
        _ => ".bin"
    };

    private static string? GetStubForSystem(string system) => system switch
    {
        "NES" => "nes-ines",
        "SNES" => "snes-header",
        "N64" => "n64-header",
        "GBA" => "gba-header",
        "GB" or "GBC" => "gb-header",
        "MD" or "32X" => "md-header",
        "PS1" => "ps1-pvd",
        "PS2" => "ps2-pvd",
        "SAT" or "DC" or "SCD" => "sega-ipbin",
        "GC" or "WII" => "nintendo-disc",
        "LYNX" => "lynx-header",
        "A78" => "a7800-header",
        _ => "ext-only"
    };

    private static string GetPrimaryMethodForSystem(string system) => system switch
    {
        "NES" or "SNES" or "N64" or "GBA" or "GB" or "GBC" or "MD" or "32X"
            or "SMS" or "GG" or "PCE" or "LYNX" or "A78" or "A26" or "NDS"
            or "3DS" or "NGP" or "WS" or "WSC" or "COLECO" or "INTV" or "VB"
            or "VECTREX" or "A52" or "SWITCH"
            => "CartridgeHeader",
        "PS1" or "PS2" or "SAT" or "DC" or "GC" or "WII" or "3DO"
            or "NEOCD" or "SCD" or "PCECD" or "PCFX" or "PSP"
            => "DiscHeader",
        "ATARIST" or "C64" or "MSX" or "AMIGA" or "DOS" or "ZX"
            => "UniqueExtension",
        "ARCADE" or "NEOGEO"
            => "FolderName",
        _ => "Heuristic"
    };
}
