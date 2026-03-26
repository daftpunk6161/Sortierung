using System.Text;

namespace RomCleanup.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal XDVDFS disc header stubs for Xbox and Xbox 360.
/// The XDVDFS magic "MICROSOFT*XBOX*MEDIA" appears at offset 0x10000.
/// </summary>
internal sealed class XdvdfsGenerator : IStubGenerator
{
    private const int XdvdfsMagicOffset = 0x10000;
    private const string XdvdfsMagic = "MICROSOFT*XBOX*MEDIA";

    public string GeneratorId => "xdvdfs";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["xbox", "x360"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        variant ??= "xbox";
        return variant switch
        {
            "xbox" => GenerateXdvdfs(),
            "x360" => GenerateX360(),
            _ => throw new ArgumentException($"Unknown variant '{variant}' for {GeneratorId}")
        };
    }

    private static byte[] GenerateXdvdfs()
    {
        // Need at least 0x10000 + magic length bytes
        var data = new byte[XdvdfsMagicOffset + 256];
        Encoding.ASCII.GetBytes(XdvdfsMagic).CopyTo(data, XdvdfsMagicOffset);
        return data;
    }

    private static byte[] GenerateX360()
    {
        // X360 also uses XDVDFS magic at same offset, plus XGD2/XGD3 markers
        var data = new byte[XdvdfsMagicOffset + 256];
        Encoding.ASCII.GetBytes(XdvdfsMagic).CopyTo(data, XdvdfsMagicOffset);
        // Add XBOX 360 text marker in header area for ResolveConsoleFromText detection
        Encoding.ASCII.GetBytes("XBOX 360").CopyTo(data, 0x20);
        return data;
    }
}
