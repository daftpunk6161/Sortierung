namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Defines the realism level of generated stub files.
/// Higher levels produce more realistic byte patterns for improved testing fidelity.
/// </summary>
internal enum StubRealismLevel
{
    /// <summary>
    /// L1 – Minimal header: only essential magic bytes / signatures.
    /// Sufficient for basic header-match detection. Default level.
    /// </summary>
    Minimal = 1,

    /// <summary>
    /// L2 – Realistic padding: valid header fields, realistic file sizes,
    /// plausible checksums, and sector-aligned data regions.
    /// Guards against detectors that rely on structural heuristics.
    /// </summary>
    Realistic = 2,

    /// <summary>
    /// L3 – Full structure: complete file layout including multiple sections,
    /// valid internal offsets, plausible code/data regions, and filesystem metadata.
    /// Closest to a real ROM without copyrighted content.
    /// </summary>
    FullStructure = 3
}

/// <summary>
/// Extension methods for realism-aware stub generation.
/// Wraps any IStubGenerator to apply realism post-processing.
/// </summary>
internal static class StubRealismExtensions
{
    /// <summary>
    /// Generates a stub with the specified realism level.
    /// L1 returns the generator's default output.
    /// L2/L3 apply additional byte patterns for realism.
    /// </summary>
    public static byte[] GenerateWithRealism(
        this IStubGenerator generator,
        StubRealismLevel level,
        string? variant = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var baseData = generator.Generate(variant, parameters);

        return level switch
        {
            StubRealismLevel.Minimal => baseData,
            StubRealismLevel.Realistic => ApplyRealisticPadding(baseData, generator.Extension),
            StubRealismLevel.FullStructure => ApplyFullStructure(baseData, generator.Extension),
            _ => baseData
        };
    }

    /// <summary>
    /// L2: Adds sector-aligned padding, plausible fill patterns, and
    /// realistic file sizes to make stubs pass structural heuristics.
    /// </summary>
    private static byte[] ApplyRealisticPadding(byte[] data, string extension)
    {
        // Ensure minimum size based on format expectations
        int targetSize = GetRealisticMinSize(extension);
        if (data.Length >= targetSize)
            return data;

        var padded = new byte[targetSize];
        Array.Copy(data, padded, data.Length);

        // Fill padding with a realistic pattern (0xFF is common for blank ROM areas)
        for (int i = data.Length; i < targetSize; i++)
        {
            padded[i] = (byte)(i % 256 == 0 ? 0x00 : 0xFF);
        }

        return padded;
    }

    /// <summary>
    /// L3: Builds a more complete file structure with internal sections,
    /// valid offsets, and plausible data distribution.
    /// </summary>
    private static byte[] ApplyFullStructure(byte[] data, string extension)
    {
        // Start with L2 padding
        var realistic = ApplyRealisticPadding(data, extension);

        // Add a plausible "code section" pattern near the beginning
        int codeStart = Math.Min(realistic.Length, 0x200);
        int codeEnd = Math.Min(realistic.Length, 0x1000);
        var random = new Random(42); // Deterministic for reproducibility
        for (int i = codeStart; i < codeEnd && i < realistic.Length; i++)
        {
            // Simulate instruction-like byte patterns
            if (realistic[i] == 0xFF)
                realistic[i] = (byte)random.Next(256);
        }

        // Add a plausible "data section" with varied content
        int dataStart = Math.Min(realistic.Length, codeEnd);
        int dataEnd = Math.Min(realistic.Length, dataStart + 0x4000);
        for (int i = dataStart; i < dataEnd && i < realistic.Length; i += 4)
        {
            if (realistic[i] == 0xFF)
            {
                realistic[i] = (byte)(i & 0xFF);
                if (i + 1 < dataEnd) realistic[i + 1] = (byte)((i >> 8) & 0xFF);
            }
        }

        return realistic;
    }

    private static int GetRealisticMinSize(string extension) => extension.ToLowerInvariant() switch
    {
        ".nes" => 40976,         // 32KB PRG + 8KB CHR + 16 byte header
        ".sfc" or ".smc" => 32768, // 32KB minimum SNES ROM
        ".z64" or ".n64" => 131072, // 128KB minimum N64
        ".gba" => 32768,         // 32KB minimum GBA
        ".gb" or ".gbc" => 32768, // 32KB minimum GB
        ".md" or ".gen" or ".32x" => 131072, // 128KB minimum MD
        ".sms" or ".gg" => 32768,
        ".pce" => 32768,
        ".lnx" => 32832,        // 32KB + 64 byte Lynx header
        ".a78" => 16512,        // 16KB + 128 byte A7800 header
        ".bin" or ".iso" => 34816, // 17 sectors for disc image
        ".nds" => 131072,
        ".a26" => 4096,
        _ => 16384
    };
}
