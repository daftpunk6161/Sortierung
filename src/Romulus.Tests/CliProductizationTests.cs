using System.IO;
using Romulus.CLI;
using Romulus.Contracts;
using Xunit;

namespace Romulus.Tests;

public sealed class CliProductizationTests : IDisposable
{
    private readonly string _tempDir;

    public CliProductizationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_CliProductization_" + Guid.NewGuid().ToString("N"));
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
            // best effort cleanup
        }
    }

    [Fact]
    public void WriteUsage_ListsProductizationSubcommands_And_RunConfigurationFlags()
    {
        using var stdout = new StringWriter();

        CliOutputWriter.WriteUsage(stdout);

        var text = stdout.ToString();
        Assert.Contains("romulus profiles list", text, StringComparison.Ordinal);
        Assert.Contains("romulus workflows", text, StringComparison.Ordinal);
        Assert.Contains("romulus compare --run <run-id> --compare-to <run-id>", text, StringComparison.Ordinal);
        Assert.Contains("romulus trends [--limit <n>] [-o <file>]", text, StringComparison.Ordinal);
        Assert.Contains("romulus dat fixdat --roots <path>", text, StringComparison.Ordinal);
        Assert.Contains("--workflow <id>", text, StringComparison.Ordinal);
        Assert.Contains("--profile <id>", text, StringComparison.Ordinal);
        Assert.Contains("--profile-file <file>", text, StringComparison.Ordinal);
        Assert.Contains("m3u|launchbox|emulationstation|playnite|mister|analoguepocket|onionos", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteUsage_UsesRunConstantsDefaultPreferRegions()
    {
        using var stdout = new StringWriter();

        CliOutputWriter.WriteUsage(stdout);

        var text = stdout.ToString();
        Assert.Contains(
            $"default: {string.Join(",", RunConstants.DefaultPreferRegions)}",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CliArgsParser_ProfilesShow_ParsesProfileId()
    {
        var result = CliArgsParser.Parse(["profiles", "show", "--id", "quick-scan"]);

        Assert.Equal(CliCommand.ProfilesShow, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal("quick-scan", result.Options!.ProfileId);
    }

    [Fact]
    public void CliArgsParser_ProfilesImport_ParsesInputPath()
    {
        var profilePath = Path.Combine(_tempDir, "profile.json");
        File.WriteAllText(profilePath, "{}");

        var result = CliArgsParser.Parse(["profiles", "import", "--input", profilePath]);

        Assert.Equal(CliCommand.ProfilesImport, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(profilePath, result.Options!.InputPath);
    }

    [Fact]
    public void CliArgsParser_Workflows_ParsesWorkflowId()
    {
        var result = CliArgsParser.Parse(["workflows", "--id", "full-audit"]);

        Assert.Equal(CliCommand.Workflows, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal("full-audit", result.Options!.WorkflowScenarioId);
    }

    [Fact]
    public void CliArgsParser_Compare_ParsesRunPair_AndOutput()
    {
        var outputPath = Path.Combine(_tempDir, "compare.json");

        var result = CliArgsParser.Parse([
            "compare",
            "--run", "run-new",
            "--compare-to", "run-old",
            "--output", outputPath
        ]);

        Assert.Equal(CliCommand.Compare, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal("run-new", result.Options!.RunId);
        Assert.Equal("run-old", result.Options.CompareToRunId);
        Assert.Equal(outputPath, result.Options.OutputPath);
    }

    [Fact]
    public void CliArgsParser_Trends_ParsesLimit_AndOutput()
    {
        var outputPath = Path.Combine(_tempDir, "trends.json");

        var result = CliArgsParser.Parse([
            "trends",
            "--limit", "90",
            "--output", outputPath
        ]);

        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(90, result.Options!.HistoryLimit);
        Assert.Equal(outputPath, result.Options.OutputPath);
    }

    [Theory]
    [InlineData("m3u")]
    [InlineData("mister")]
    [InlineData("analoguepocket")]
    [InlineData("onionos")]
    public void CliArgsParser_Export_AcceptsAdditionalFrontendFormats(string format)
    {
        var result = CliArgsParser.Parse([
            "export",
            "--roots", _tempDir,
            "--format", format
        ]);

        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(format, result.Options!.ExportFormat, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CliArgsParser_DatFixDat_ParsesRootsOutputAndName()
    {
        var outputPath = Path.Combine(_tempDir, "fixdat.dat");

        var result = CliArgsParser.Parse([
            "dat", "fixdat",
            "--roots", _tempDir,
            "--output", outputPath,
            "--name", "Romulus-FixDAT"
        ]);

        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(_tempDir, Assert.Single(result.Options!.Roots));
        Assert.Equal(outputPath, result.Options.OutputPath);
        Assert.Equal("Romulus-FixDAT", result.Options.DatName);
    }

    [Fact]
    public void CliArgsParser_DatFixDat_RejectsUncOutputPath()
    {
        var result = CliArgsParser.Parse([
            "dat", "fixdat",
            "--roots", _tempDir,
            "--output", "\\\\server\\share\\fixdat.dat"
        ]);

        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, error =>
            error.Contains("must not be a UNC path", StringComparison.OrdinalIgnoreCase));
    }
}
