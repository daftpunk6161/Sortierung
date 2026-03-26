namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal GameCube/Wii disc image stubs.
/// GC magic: C2339F3D at offset 0x1C
/// Wii magic: 5D1C9EA3 at offset 0x18
/// </summary>
internal sealed class NintendoDiscGenerator : IStubGenerator
{
    private static readonly byte[] GcMagic = [0xC2, 0x33, 0x9F, 0x3D];
    private static readonly byte[] WiiMagic = [0x5D, 0x1C, 0x9E, 0xA3];

    public string GeneratorId => "nintendo-disc";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["gamecube", "wii"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "gamecube";
        return variant switch
        {
            "gamecube" => GenerateGc(parameters),
            "wii" => GenerateWii(parameters),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateGc(IReadOnlyDictionary<string, string>? parameters)
    {
        // Min size: 64 bytes for header area
        var data = new byte[2048];

        // Game code at 0x00 (4 bytes, e.g., "GMSE")
        string gameCode = "GMSE";
        if (parameters?.TryGetValue("gameCode", out var gc) == true)
            gameCode = gc.PadRight(4)[..4];
        System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(data, 0);

        // GC magic at 0x1C
        GcMagic.CopyTo(data, 0x1C);

        // Game name at 0x20 (up to 992 bytes, null-terminated)
        System.Text.Encoding.ASCII.GetBytes("Benchmark GC Game").CopyTo(data, 0x20);

        return data;
    }

    private static byte[] GenerateWii(IReadOnlyDictionary<string, string>? parameters)
    {
        var data = new byte[2048];

        string gameCode = "RMCE";
        if (parameters?.TryGetValue("gameCode", out var gc) == true)
            gameCode = gc.PadRight(4)[..4];
        System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(data, 0);

        // Wii magic at 0x18
        WiiMagic.CopyTo(data, 0x18);

        // Game name at 0x20
        System.Text.Encoding.ASCII.GetBytes("Benchmark Wii Game").CopyTo(data, 0x20);

        return data;
    }
}
