namespace Romulus.Tests.Benchmark.Generators.Disc;

/// <summary>
/// Generates minimal 3DO Opera filesystem disc header stubs.
/// 3DO discs start with 0x01 followed by 5×0x5A at offsets 0-5.
/// </summary>
internal sealed class OperaFsGenerator : IStubGenerator
{
    public string GeneratorId => "3do-opera";
    public string Extension => ".iso";
    public IReadOnlyList<string> SupportedVariants { get; } = ["standard"];

    public byte[] Generate(string? variant = null, IReadOnlyDictionary<string, string>? parameters = null)
    {
        // Opera FS header: 0x01 + 5×0x5A at bytes 0-5, version byte at 6
        var data = new byte[2048];
        data[0] = 0x01;
        data[1] = 0x5A;
        data[2] = 0x5A;
        data[3] = 0x5A;
        data[4] = 0x5A;
        data[5] = 0x5A;
        data[6] = 0x01; // Record version — required by DiscHeaderDetector
        return data;
    }
}
