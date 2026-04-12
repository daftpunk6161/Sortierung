using System.Text.Json;
using System.Text.Json.Nodes;
using Romulus.Infrastructure.Configuration;
using Xunit;

namespace Romulus.Tests;

public sealed class StartupDataSchemaValidatorTests : IDisposable
{
    private static readonly string[] RequiredDataFiles =
    [
        "consoles.json",
        "console-maps.json",
        "rules.json",
        "defaults.json",
        "format-scores.json",
        "tool-hashes.json",
        "ui-lookups.json",
        "conversion-registry.json",
        "dat-catalog.json"
    ];

    private static readonly string[] RequiredSchemaFiles =
    [
        "consoles.schema.json",
        "console-maps.schema.json",
        "rules.schema.json",
        "defaults.schema.json",
        "format-scores.schema.json",
        "tool-hashes.schema.json",
        "ui-lookups.schema.json",
        "conversion-registry.schema.json",
        "dat-catalog.schema.json"
    ];

    private readonly string _tempRoot;
    private readonly string _tempDataDir;

    public StartupDataSchemaValidatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_StartupSchema_" + Guid.NewGuid().ToString("N"));
        _tempDataDir = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_tempDataDir);
        Directory.CreateDirectory(Path.Combine(_tempDataDir, "schemas"));

        CopyRequiredDataAndSchemas();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    [Fact]
    public void ValidateRequiredFiles_ValidData_DoesNotThrow()
    {
        StartupDataSchemaValidator.ValidateRequiredFiles(_tempDataDir);
    }

    [Fact]
    public void ValidateRequiredFiles_InvalidDefaults_ReportsFileAndFieldPath()
    {
        var defaultsPath = Path.Combine(_tempDataDir, "defaults.json");
        var defaultsNode = JsonNode.Parse(File.ReadAllText(defaultsPath))!.AsObject();
        defaultsNode["versionScoreMaxSegments"] = 0;
        File.WriteAllText(defaultsPath, defaultsNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupDataSchemaValidator.ValidateRequiredFiles(_tempDataDir));

        Assert.Contains("defaults.json", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$.versionScoreMaxSegments", ex.Message, StringComparison.Ordinal);
    }

    private void CopyRequiredDataAndSchemas()
    {
        var sourceDataDir = ResolveSourceDataDirectory();
        foreach (var dataFile in RequiredDataFiles)
        {
            File.Copy(
                Path.Combine(sourceDataDir, dataFile),
                Path.Combine(_tempDataDir, dataFile),
                overwrite: true);
        }

        var sourceSchemaDir = Path.Combine(sourceDataDir, "schemas");
        var targetSchemaDir = Path.Combine(_tempDataDir, "schemas");
        foreach (var schemaFile in RequiredSchemaFiles)
        {
            File.Copy(
                Path.Combine(sourceSchemaDir, schemaFile),
                Path.Combine(targetSchemaDir, schemaFile),
                overwrite: true);
        }
    }

    private static string ResolveSourceDataDirectory()
    {
        var outputData = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(outputData))
            return outputData;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "data");
            if (Directory.Exists(candidate))
                return candidate;

            var srcCandidate = Path.Combine(current.FullName, "src", "Romulus.Tests", "bin", "Debug", "net10.0-windows", "data");
            if (Directory.Exists(srcCandidate))
                return srcCandidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not resolve test data directory for startup schema validation tests.");
    }
}
