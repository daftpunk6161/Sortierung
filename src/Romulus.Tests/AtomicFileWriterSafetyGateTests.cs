using System.Text;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep Dive Audit – Safety / FileSystem / Security – Restpunkt.
///
/// AtomicFileWriter hatte bisher KEINE eingebaute Sicherheits-Validierung
/// fuer das Ziel. ~40 direkte Aufrufer verliessen sich darauf, dass die
/// Ziel-Pfadpruefung extern passiert. Das ist eine zentrale Schwachstelle:
/// jeder neue Aufrufer der die externe Pruefung vergisst, kann in
/// Reparse-Points oder via ADS / Reserved Device Names schreiben.
///
/// Diese Tests sichern den neuen zentralen Gate
/// <c>AtomicFileWriter.EnsureSafeWriteTarget</c>:
///   F4: ADS-Suffix in Dateiname → ArgumentException
///   F5: Windows Reserved Device Name in Dateiname → ArgumentException
///   F6: Ziel-Datei oder Vorfahr ist Reparse Point → IOException
/// </summary>
public sealed class AtomicFileWriterSafetyGateTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileWriterSafetyGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"afw_safety_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ----- F4: ADS-Suffix abweisen -----

    [Fact]
    public void WriteAllText_RejectsAlternateDataStreamSuffix()
    {
        var bad = Path.Combine(_tempDir, "file.txt:hidden");
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.WriteAllText(bad, "x"));
    }

    [Fact]
    public void WriteAllBytes_RejectsAlternateDataStreamSuffix()
    {
        var bad = Path.Combine(_tempDir, "file.bin:stream");
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.WriteAllBytes(bad, [0x00]));
    }

    [Fact]
    public void AppendText_RejectsAlternateDataStreamSuffix()
    {
        var bad = Path.Combine(_tempDir, "log.txt:ads");
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.AppendText(bad, "x"));
    }

    [Fact]
    public void CopyFile_RejectsAlternateDataStreamSuffixOnDestination()
    {
        var src = Path.Combine(_tempDir, "src.bin");
        File.WriteAllBytes(src, [0x01]);
        var bad = Path.Combine(_tempDir, "dest.bin:ads");
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.CopyFile(src, bad, overwrite: true));
    }

    // ----- F5: Reserved Device Name abweisen -----

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("nul.json")]
    [InlineData("COM1.dat")]
    [InlineData("LPT9.log")]
    public void WriteAllText_RejectsWindowsReservedDeviceName(string fileName)
    {
        var bad = Path.Combine(_tempDir, fileName);
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.WriteAllText(bad, "x"));
    }

    [Fact]
    public void CopyFile_RejectsReservedDeviceNameOnDestination()
    {
        var src = Path.Combine(_tempDir, "src.bin");
        File.WriteAllBytes(src, [0x01]);
        var bad = Path.Combine(_tempDir, "PRN");
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.CopyFile(src, bad, overwrite: true));
    }

    // ----- F6: Reparse-Point in Ancestry abweisen (best-effort, kein admin) -----
    // Negative Pruefung, dass legitime Pfade weiterhin funktionieren (Regression-Schutz).

    [Fact]
    public void WriteAllText_AcceptsLegitimateNestedPath()
    {
        var ok = Path.Combine(_tempDir, "sub", "deep", "file.txt");
        AtomicFileWriter.WriteAllText(ok, "hello", Encoding.UTF8);
        Assert.True(File.Exists(ok));
        Assert.Equal("hello", File.ReadAllText(ok));
    }

    [Fact]
    public void WriteAllBytes_AcceptsLegitimateNestedPath()
    {
        var ok = Path.Combine(_tempDir, "bytes", "data.bin");
        AtomicFileWriter.WriteAllBytes(ok, [0xDE, 0xAD]);
        Assert.True(File.Exists(ok));
        Assert.Equal([0xDE, 0xAD], File.ReadAllBytes(ok));
    }

    [Fact]
    public void AppendText_AcceptsLegitimateNestedPath()
    {
        var ok = Path.Combine(_tempDir, "logs", "app.log");
        AtomicFileWriter.AppendText(ok, "line1\n");
        AtomicFileWriter.AppendText(ok, "line2\n");
        Assert.Equal("line1\nline2\n", File.ReadAllText(ok));
    }

    [Fact]
    public void CopyFile_AcceptsLegitimateDestination()
    {
        var src = Path.Combine(_tempDir, "src.bin");
        File.WriteAllBytes(src, [0x42]);
        var dest = Path.Combine(_tempDir, "out", "dest.bin");
        AtomicFileWriter.CopyFile(src, dest, overwrite: true);
        Assert.True(File.Exists(dest));
        Assert.Equal([0x42], File.ReadAllBytes(dest));
    }

    // ----- Reparse-Ancestor-Test via Junction (skip when not creatable) -----

    [Fact]
    public void WriteAllText_RejectsTargetUnderReparsePointDirectory()
    {
        var realTarget = Path.Combine(_tempDir, "real");
        Directory.CreateDirectory(realTarget);

        var junction = Path.Combine(_tempDir, "junction");
        if (!TryCreateDirectoryJunction(junction, realTarget))
            return; // Skip silently when junction creation not supported (admin/policy).

        var pathUnderJunction = Path.Combine(junction, "file.txt");
        var ex = Record.Exception(() => AtomicFileWriter.WriteAllText(pathUnderJunction, "x"));
        Assert.NotNull(ex);
        Assert.True(ex is IOException or UnauthorizedAccessException,
            $"Expected IO/Unauthorized rejection for reparse-point parent, got {ex.GetType().Name}: {ex.Message}");
    }

    private static bool TryCreateDirectoryJunction(string junctionPath, string realPath)
    {
        try
        {
            // .NET 10: Directory.CreateSymbolicLink creates a junction-like reparse point on Windows
            // when target is an existing directory. Requires Developer Mode or admin on some systems.
            Directory.CreateSymbolicLink(junctionPath, realPath);
            return Directory.Exists(junctionPath)
                && (File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
