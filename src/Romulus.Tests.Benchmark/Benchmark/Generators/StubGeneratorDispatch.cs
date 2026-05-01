using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Dispatches stub generation for a ground-truth entry by inferring the appropriate
/// generator from entry metadata (detection method, console key, extension).
/// Writes the generated file deterministically — same entry always produces identical bytes.
/// </summary>
internal sealed class StubGeneratorDispatch
{
    private readonly StubGeneratorRegistry _registry = new();

    // Mapping: consoleKey → (generatorId, variant)
    private static readonly Dictionary<string, (string GeneratorId, string Variant)> CartridgeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NES"] = ("nes-ines", "standard"),
        ["SNES"] = ("snes-header", "lorom"),
        ["N64"] = ("n64-header", "big-endian"),
        ["GBA"] = ("gba-header", "standard"),
        ["GB"] = ("gb-header", "dmg"),
        ["GBC"] = ("gb-header", "cgb-dual"),
        ["MD"] = ("md-header", "megadrive"),
        ["32X"] = ("md-header", "32x"),
        ["LYNX"] = ("lynx-header", "standard"),
        ["A78"] = ("a7800-header", "standard"),
    };

    private static readonly Dictionary<string, (string GeneratorId, string Variant)> DiscMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PS1"] = ("ps1-pvd", "standard"),
        ["PS2"] = ("ps2-pvd", "standard"),
        ["PSP"] = ("ps2-pvd", "psp"),
        ["PS3"] = ("ps3-pvd", "standard"),
        ["GC"] = ("nintendo-disc", "gamecube"),
        ["WII"] = ("nintendo-disc", "wii"),
        ["SAT"] = ("sega-ipbin", "saturn"),
        ["DC"] = ("sega-ipbin", "dreamcast"),
        ["SCD"] = ("sega-ipbin", "segacd"),
        ["3DO"] = ("3do-opera", "standard"),
        ["CD32"] = ("boot-sector-text", "cd32"),
        ["NEOCD"] = ("boot-sector-text", "neocd"),
        ["PCECD"] = ("boot-sector-text", "pcecd"),
        ["PCFX"] = ("boot-sector-text", "pcfx"),
        ["JAGCD"] = ("boot-sector-text", "jagcd"),
        ["XBOX"] = ("xdvdfs", "xbox"),
        ["X360"] = ("xdvdfs", "x360"),
        ["FMTOWNS"] = ("fmtowns-pvd", "standard"),
        ["CDI"] = ("cdi-disc", "standard"),
    };

    /// <summary>
    /// Generates all stub files from ground-truth entries into the output directory.
    /// Returns the number of files generated.
    /// </summary>
    public int GenerateAll(IReadOnlyList<GroundTruthEntry> entries, string outputDir)
    {
        var resolvedOutput = Path.GetFullPath(outputDir);
        int count = 0;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var relativePath = BuildRelativePath(entry);
            var fullPath = Path.GetFullPath(Path.Combine(resolvedOutput, relativePath));

            // Path-traversal protection: ensure the path stays within outputDir
            if (!fullPath.StartsWith(resolvedOutput, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Path traversal detected for entry '{entry.Id}': '{fullPath}' escapes output root '{resolvedOutput}'");

            // Duplicate-path guard: skip entries that map to an already-written path
            if (!seenPaths.Add(fullPath))
                continue;

            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            var bytes = GenerateStub(entry);
            File.WriteAllBytes(fullPath, bytes);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Gets the relative file path for a ground-truth entry.
    /// Uses {directory}/{entryId}/{fileName} to ensure unique paths even when
    /// multiple entries share the same directory and fileName (TASK-036).
    /// </summary>
    public static string BuildRelativePath(GroundTruthEntry entry)
    {
        var directory = entry.Source.Directory ?? "unsorted";
        var fileName = entry.Source.FileName;

        // Sanitize path components — no parent traversal
        directory = directory.Replace("..", "_").Replace('/', Path.DirectorySeparatorChar);
        fileName = Path.GetFileName(fileName); // Strip any directory components

        // Entry ID as sub-directory ensures uniqueness across entries with same directory/fileName
        var entryDir = entry.Id.Replace("..", "_").Replace('/', '_').Replace('\\', '_');

        return Path.Combine(directory, entryDir, fileName);
    }

    /// <summary>
    /// Generates the byte content for a single ground-truth entry.
    /// Infers generator from source.stub → detectionExpectations.primaryMethod → fallback.
    /// </summary>
    public byte[] GenerateStub(GroundTruthEntry entry)
    {
        // Priority 1: Explicit stub definition
        if (entry.Source.Stub is { } stub)
        {
            // "generic-headerless" is a headerless ROM → just a minimal file with the right extension
            if (string.Equals(stub.Generator, "generic-headerless", StringComparison.OrdinalIgnoreCase))
                return _registry.GetRequired("ext-only").Generate("default");

            var gen = _registry.GetRequired(stub.Generator);
            var parameters = stub.Params?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "",
                StringComparer.OrdinalIgnoreCase);

            // Check for realism level in stub params
            var level = ParseRealismLevel(parameters);
            if (level > StubRealismLevel.Minimal)
                return gen.GenerateWithRealism(level, stub.Variant, parameters as IReadOnlyDictionary<string, string>);

            return gen.Generate(stub.Variant, parameters as IReadOnlyDictionary<string, string>);
        }

        // Priority 2: Infer from primary detection method + console key
        var method = entry.DetectionExpectations?.PrimaryMethod;
        var consoleKey = entry.Expected.ConsoleKey;
        var category = entry.Expected.Category;

        // Negative controls: files that should NOT match any console
        if (consoleKey is null or "UNKNOWN" || category is "NonRom" or "Junk" or "Unknown")
        {
            return InferNegativeStub(entry);
        }

        if (string.Equals(method, "CartridgeHeader", StringComparison.OrdinalIgnoreCase) && consoleKey is not null)
        {
            if (CartridgeMap.TryGetValue(consoleKey, out var cart))
            {
                var gen = _registry.Get(cart.GeneratorId);
                if (gen is not null)
                    return gen.Generate(cart.Variant);
            }
        }

        if (string.Equals(method, "DiscHeader", StringComparison.OrdinalIgnoreCase) && consoleKey is not null)
        {
            if (DiscMap.TryGetValue(consoleKey, out var disc))
            {
                var gen = _registry.Get(disc.GeneratorId);
                if (gen is not null)
                    return gen.Generate(disc.Variant);
            }
        }

        // Priority 2b: Null primaryMethod fallback — infer from consoleKey using disc/cartridge maps
        if (method is null && consoleKey is not null)
        {
            if (DiscMap.TryGetValue(consoleKey, out var disc))
            {
                var gen = _registry.Get(disc.GeneratorId);
                if (gen is not null)
                    return gen.Generate(disc.Variant);
            }

            if (CartridgeMap.TryGetValue(consoleKey, out var cart))
            {
                var gen = _registry.Get(cart.GeneratorId);
                if (gen is not null)
                    return gen.Generate(cart.Variant);
            }
        }

        // Priority 3: Extension-only or folder-only detection → minimal file
        return _registry.GetRequired("ext-only").Generate("default");
    }

    private byte[] InferNegativeStub(GroundTruthEntry entry)
    {
        // Check tags for hints about what kind of negative control this is
        var tags = entry.Tags ?? [];
        var ext = entry.Source.Extension?.ToLowerInvariant();

        // If extension is empty or a non-ROM extension, generate non-ROM content
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp")
            return _registry.GetRequired("non-rom-content").Generate("jfif");
        if (ext is ".pdf")
            return _registry.GetRequired("non-rom-content").Generate("pdf");
        if (ext is ".exe" or ".dll")
            return _registry.GetRequired("non-rom-content").Generate("mz");

        // Files with ROM extension but invalid content (should still be detected as unknown)
        if (tags.Contains("negative-control") || tags.Contains("wrong-magic"))
        {
            // Deterministic random bytes seeded from ID
            var seed = entry.Id.GetHashCode(StringComparison.Ordinal);
            return _registry.GetRequired("random-bytes").Generate("default",
                new Dictionary<string, string>
                {
                    ["sizeBytes"] = Math.Max(64, entry.Source.SizeBytes > 0 && entry.Source.SizeBytes < 4096 ? (int)entry.Source.SizeBytes : 512).ToString(),
                    ["seed"] = seed.ToString()
                });
        }

        // Empty files
        if (entry.Source.SizeBytes == 0)
            return _registry.GetRequired("ext-only").Generate("empty");

        // Default: minimal file
        return _registry.GetRequired("ext-only").Generate("default");
    }

    private static StubRealismLevel ParseRealismLevel(Dictionary<string, string>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("realism", out var value))
            return StubRealismLevel.Minimal;

        return value?.ToUpperInvariant() switch
        {
            "L2" or "REALISTIC" => StubRealismLevel.Realistic,
            "L3" or "FULL" or "FULLSTRUCTURE" => StubRealismLevel.FullStructure,
            _ => StubRealismLevel.Minimal
        };
    }
}
