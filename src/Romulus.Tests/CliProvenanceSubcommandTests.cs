using Romulus.CLI;
using Xunit;

namespace Romulus.Tests;

public sealed class CliProvenanceSubcommandTests : IDisposable
{
    private readonly string _tempDir;

    public CliProvenanceSubcommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cli-prov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Provenance_WithFingerprint_ReturnsProvenanceCommand()
    {
        var result = CliArgsParser.Parse(["provenance", "--fingerprint", "abcdef0123456789"]);

        Assert.Equal(CliCommand.Provenance, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("abcdef0123456789", result.Options!.Fingerprint);
    }

    [Fact]
    public void Provenance_PositionalFingerprintAndOutput_Accepted()
    {
        var outputPath = Path.Combine(_tempDir, "trail.json");

        var result = CliArgsParser.Parse(["provenance", "abcdef0123456789", "-o", outputPath]);

        Assert.Equal(CliCommand.Provenance, result.Command);
        Assert.Equal("abcdef0123456789", result.Options!.Fingerprint);
        Assert.Equal(outputPath, result.Options.OutputPath);
    }

    [Fact]
    public void Provenance_MissingFingerprint_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["provenance"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("--fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Provenance_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["provenance", "--fingerprint", "abcdef0123456789", "--bogus"]);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("Unknown flag", StringComparison.OrdinalIgnoreCase));
    }
}
