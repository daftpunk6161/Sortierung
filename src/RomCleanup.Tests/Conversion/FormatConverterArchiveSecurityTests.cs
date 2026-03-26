using System.IO.Compression;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using Xunit;

namespace RomCleanup.Tests.Conversion;

public sealed class FormatConverterArchiveSecurityTests
{
    [Fact]
    public void Convert_ZipWithTraversalEntry_IsBlocked()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fmt_conv_sec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "payload.zip");

        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("..\\evil.cue");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("FILE \"evil.bin\" BINARY\n");
                writer.Write("  TRACK 01 MODE1/2352\n");
                writer.Write("    INDEX 01 00:00:00\n");
            }

            var converter = new FormatConverterAdapter(new MockToolRunner());
            var target = new ConversionTarget(".chd", "chdman", "createcd");

            var result = converter.Convert(zipPath, target);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("archive-zip-slip-detected", result.Reason);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    private sealed class MockToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => $"C:\\mock\\{toolName}.exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "ok", true);

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "ok", true);
    }
}
