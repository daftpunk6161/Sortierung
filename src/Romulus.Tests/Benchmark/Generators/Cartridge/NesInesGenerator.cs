namespace Romulus.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal iNES (.nes) ROM stubs with valid header magic.
/// iNES header: 16 bytes starting with "NES\x1A", followed by PRG/CHR bank counts.
/// </summary>
internal sealed class NesInesGenerator : IStubGenerator
{
    // iNES magic: 4E 45 53 1A
    private static readonly byte[] InesMagic = [0x4E, 0x45, 0x53, 0x1A];

    public string GeneratorId => "nes-ines";
    public string Extension => ".nes";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard", "headerless", "truncated", "copier-header"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "standard";

        return variant switch
        {
            "standard" => GenerateStandard(parameters),
            "headerless" => GenerateHeaderless(parameters),
            "truncated" => GenerateTruncated(),
            "copier-header" => GenerateCopierHeader(parameters),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateStandard(IReadOnlyDictionary<string, string>? parameters)
    {
        // 16-byte iNES header + 16KB PRG ROM (minimum valid)
        int prgBanks = 1; // 16 KB
        int chrBanks = 0;

        if (parameters?.TryGetValue("prgBanks", out var prgStr) == true && int.TryParse(prgStr, out var p))
            prgBanks = p;
        if (parameters?.TryGetValue("chrBanks", out var chrStr) == true && int.TryParse(chrStr, out var c))
            chrBanks = c;

        int totalSize = 16 + (prgBanks * 16384) + (chrBanks * 8192);
        var data = new byte[totalSize];

        // Header
        InesMagic.CopyTo(data, 0);
        data[4] = (byte)prgBanks;
        data[5] = (byte)chrBanks;
        data[6] = 0x00; // Mapper lower nybble, mirroring
        data[7] = 0x00; // Mapper upper nybble

        return data;
    }

    private static byte[] GenerateHeaderless(IReadOnlyDictionary<string, string>? parameters)
    {
        // Raw PRG data without iNES header (32KB default)
        int size = 32768;
        if (parameters?.TryGetValue("size", out var sizeStr) == true && int.TryParse(sizeStr, out var s))
            size = s;

        return new byte[size];
    }

    private static byte[] GenerateTruncated()
    {
        // Valid magic but too short to be a real ROM
        var data = new byte[8];
        InesMagic.CopyTo(data, 0);
        data[4] = 1; // Claims 1 PRG bank but file is too short
        return data;
    }

    private static byte[] GenerateCopierHeader(IReadOnlyDictionary<string, string>? parameters)
    {
        // 512-byte copier header prefix (e.g. from old NES copiers) + standard iNES ROM
        var standard = GenerateStandard(parameters);
        var data = new byte[512 + standard.Length];
        // Copier header is typically 512 zero bytes (or vendor-specific); iNES follows at offset 512
        standard.CopyTo(data, 512);
        return data;
    }
}
