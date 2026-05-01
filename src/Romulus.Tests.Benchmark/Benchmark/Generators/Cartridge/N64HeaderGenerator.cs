namespace Romulus.Tests.Benchmark.Generators.Cartridge;

/// <summary>
/// Generates minimal N64 ROM stubs (.z64/.n64/.v64).
/// N64 ROMs start with a 4-byte magic indicating endianness:
/// Big-Endian (.z64): 80 37 12 40
/// Byte-Swapped (.n64): 40 12 37 80
/// Little-Endian (.v64): 37 80 40 12
/// </summary>
internal sealed class N64HeaderGenerator : IStubGenerator
{
    private static readonly byte[] MagicBE = [0x80, 0x37, 0x12, 0x40];
    private static readonly byte[] MagicBS = [0x40, 0x12, 0x37, 0x80];
    private static readonly byte[] MagicLE = [0x37, 0x80, 0x40, 0x12];

    public string GeneratorId => "n64-header";
    public string Extension => ".z64";
    public IReadOnlyList<string> SupportedVariants { get; } = ["big-endian", "byte-swapped", "little-endian", "headerless", "truncated"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "big-endian";
        return variant switch
        {
            "big-endian" => GenerateWithMagic(MagicBE),
            "byte-swapped" => GenerateWithMagic(MagicBS),
            "little-endian" => GenerateWithMagic(MagicLE),
            "headerless" => new byte[65536],
            "truncated" => GenerateWithMagic(MagicBE, 16),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateWithMagic(byte[] magic, int totalSize = 65536)
    {
        var data = new byte[totalSize];
        magic.CopyTo(data, 0);
        return data;
    }
}
