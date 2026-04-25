using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Tools;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave3RemediationRegressionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Wave3_" + Guid.NewGuid().ToString("N"));

    public Wave3RemediationRegressionTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void ConversionOutputValidator_FinalZipRequiresRealMagicAndMinimumSize()
    {
        var fakeZip = Path.Combine(_tempDir, "fake.zip");
        File.WriteAllBytes(fakeZip, new byte[22]);

        Assert.False(ConversionOutputValidator.TryValidateCreatedOutput(fakeZip, out var reason));
        Assert.Equal("output-magic-header-mismatch", reason);
    }

    [Fact]
    public void ConversionGraph_WildcardCapability_DoesNotRearchiveArchiveSources()
    {
        var capability = new ConversionCapability
        {
            SourceExtension = "*",
            TargetExtension = ".zip",
            Tool = new ToolRequirement { ToolName = "7z" },
            Command = "a -tzip",
            ApplicableConsoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NES" },
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.SevenZipTest,
            Condition = ConversionCondition.None
        };

        var graph = new ConversionGraph([capability]);

        Assert.Null(graph.FindPath(".7z", ".zip", "NES", _ => true, SourceIntegrity.Unknown));
        Assert.NotNull(graph.FindPath(".nes", ".zip", "NES", _ => true, SourceIntegrity.Unknown));
    }

    [Fact]
    public void ToolRunner_RejectsUnsafeToolRootOverrides()
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(_tempDir))!;

        Assert.False(ToolRunnerAdapter.IsSafeConversionToolsRootOverride(driveRoot));
        Assert.False(ToolRunnerAdapter.IsSafeConversionToolsRootOverride(@"\\server\share\tools"));
        Assert.True(ToolRunnerAdapter.IsSafeConversionToolsRootOverride(_tempDir));
    }

    [Fact]
    public void ToolRunner_ToolHashesLoader_IgnoresNonStringValuesWithoutThrowing()
    {
        var fakeTool = Path.Combine(_tempDir, "fake.exe");
        File.WriteAllBytes(fakeTool, [0x4D, 0x5A, 0, 0]);

        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        File.WriteAllText(
            hashesPath,
            """
            {
              "schemaVersion": "tool-hashes-v1",
              "Tools": {
                "fake.exe": 12345
              }
            }
            """);

        var runner = new ToolRunnerAdapter(hashesPath);
        var result = runner.InvokeProcess(fakeTool, [], "fake");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HeaderRepair_ConcurrentNesRepair_IsSerializedAndCreatesSingleBackup()
    {
        var path = Path.Combine(_tempDir, "dirty.nes");
        File.WriteAllBytes(path, [0x4E, 0x45, 0x53, 0x1A, 2, 1, 0, 0, 0, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD, 1, 2, 3, 4]);

        var sut = new HeaderRepairService(new FileSystemAdapter());
        var results = ParallelEnumerable.Range(0, 2)
            .Select(_ => sut.RepairNesHeader(path))
            .ToArray();

        Assert.Single(results, static r => r);
        Assert.Single(Directory.GetFiles(_tempDir, "dirty.nes.*.bak"));
    }

    [Fact]
    public void DatXmlValidator_RejectsDatWithoutGameEntries()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatXmlValidator.ValidateLogiqxXmlContent("<datafile><header><name>Empty</name></header></datafile>"));

        Assert.Contains("contains no DAT game entries", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupDataSchemaValidator_RejectsNonStringToolHashValues()
    {
        var dataPath = Path.Combine(_tempDir, "tool-hashes.json");
        var schemaPath = RepoPath("data", "schemas", "tool-hashes.schema.json");
        File.WriteAllText(
            dataPath,
            """
            {
              "schemaVersion": "tool-hashes-v1",
              "Tools": { "7z.exe": 123 }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupDataSchemaValidator.ValidateFileAgainstSchema(dataPath, schemaPath, "tool-hashes.json"));

        Assert.Contains("$.Tools.7z.exe", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DataFiles_CompressionEstimatesAreCentralizedInConversionRegistry()
    {
        using var uiLookups = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "ui-lookups.json")));
        using var registry = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "conversion-registry.json")));

        Assert.False(uiLookups.RootElement.TryGetProperty("compressionRatios", out _));
        Assert.True(registry.RootElement.TryGetProperty("compressionEstimates", out var estimates));
        Assert.Equal(JsonValueKind.Object, estimates.ValueKind);
    }

    [Fact]
    public void DataFiles_ChdmanDvdProfile_DoesNotAutoConvertXboxIso()
    {
        using var registry = JsonDocument.Parse(File.ReadAllText(RepoPath("data", "conversion-registry.json")));
        var dvdIsoCapabilities = registry.RootElement.GetProperty("capabilities").EnumerateArray()
            .Where(static item =>
                string.Equals(item.GetProperty("sourceExtension").GetString(), ".iso", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("targetExtension").GetString(), ".chd", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.GetProperty("command").GetString(), "createdvd", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(dvdIsoCapabilities);
        foreach (var capability in dvdIsoCapabilities)
        {
            var consoles = capability.GetProperty("applicableConsoles").EnumerateArray()
                .Select(static value => value.GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("XBOX", consoles);
            Assert.DoesNotContain("X360", consoles);
        }
    }

    [Fact]
    public void ArchiveHashService_Crc32Mode_DoesNotTrustCorruptedZipCentralDirectoryCrc()
    {
        var zipPath = Path.Combine(_tempDir, "crc.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("game.bin");
            using var stream = entry.Open();
            stream.Write([1, 2, 3]);
        }

        CorruptCentralDirectoryCrc(zipPath);

        var hashes = new ArchiveHashService().GetArchiveHashes(zipPath, "CRC32");

        Assert.DoesNotContain("00000000", hashes);
    }

    private static void CorruptCentralDirectoryCrc(string zipPath)
    {
        var bytes = File.ReadAllBytes(zipPath);
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x02)
            {
                Array.Clear(bytes, i + 16, 4);
                File.WriteAllBytes(zipPath, bytes);
                return;
            }
        }

        throw new InvalidOperationException("ZIP central directory not found.");
    }

    private static string RepoPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
