using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using Xunit;

namespace RomCleanup.Tests;

public class FormatConverterAdapterTests
{
    private readonly MockToolRunner _tools = new();
    private readonly FormatConverterAdapter _converter;

    public FormatConverterAdapterTests()
    {
        _converter = new FormatConverterAdapter(_tools);
    }

    // --- GetTargetFormat tests ---

    [Theory]
    [InlineData("PS1", ".cue", ".chd", "chdman", "createcd")]
    [InlineData("PS2", ".iso", ".chd", "chdman", "createdvd")]
    [InlineData("GC", ".iso", ".rvz", "dolphintool", "convert")]
    [InlineData("WII", ".wbfs", ".rvz", "dolphintool", "convert")]
    [InlineData("NES", ".nes", ".zip", "7z", "zip")]
    [InlineData("SNES", ".sfc", ".zip", "7z", "zip")]
    public void GetTargetFormat_MapsCorrectly(string console, string ext,
        string expectedExt, string expectedTool, string expectedCmd)
    {
        var target = _converter.GetTargetFormat(console, ext);
        Assert.NotNull(target);
        Assert.Equal(expectedExt, target!.Extension);
        Assert.Equal(expectedTool, target.ToolName);
        Assert.Equal(expectedCmd, target.Command);
    }

    [Fact]
    public void GetTargetFormat_PBP_ReturnsPsxtract()
    {
        var target = _converter.GetTargetFormat("PS1", ".pbp");
        Assert.NotNull(target);
        Assert.Equal(".chd", target!.Extension);
        Assert.Equal("psxtract", target.ToolName);
    }

    [Fact]
    public void GetTargetFormat_UnknownConsole_ReturnsNull()
    {
        Assert.Null(_converter.GetTargetFormat("UNKNOWN", ".bin"));
    }

    [Fact]
    public void GetTargetFormat_CaseInsensitive()
    {
        var t1 = _converter.GetTargetFormat("ps1", ".cue");
        var t2 = _converter.GetTargetFormat("PS1", ".cue");
        Assert.NotNull(t1);
        Assert.NotNull(t2);
        Assert.Equal(t1!.Extension, t2!.Extension);
    }

    // --- Convert tests ---

    [Fact]
    public void Convert_AlreadyTargetFormat_ReturnsSkipped()
    {
        var target = new ConversionTarget(".chd", "chdman", "createcd");
        var tmpFile = Path.GetTempFileName();
        var chdFile = Path.ChangeExtension(tmpFile, ".chd");
        try
        {
            File.Move(tmpFile, chdFile);
            var result = _converter.Convert(chdFile, target);
            Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
            Assert.Equal("already-target-format", result.Reason);
        }
        finally
        {
            if (File.Exists(chdFile)) File.Delete(chdFile);
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Convert_SourceNotFound_ReturnsError()
    {
        var target = new ConversionTarget(".chd", "chdman", "createcd");
        var result = _converter.Convert(@"C:\nonexistent\file.cue", target);
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("source-not-found", result.Reason);
    }

    [Fact]
    public void Convert_ToolNotFound_ReturnsSkipped()
    {
        _tools.ToolPaths.Clear(); // no tools available
        var target = new ConversionTarget(".chd", "chdman", "createcd");
        var tmpFile = Path.GetTempFileName();
        try
        {
            var result = _converter.Convert(tmpFile, target);
            Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
            Assert.Contains("tool-not-found", result.Reason);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // --- Format table completeness ---

    [Fact]
    public void GetTargetFormat_AllCdConsoles_ReturnChd()
    {
        var cdConsoles = new[] { "PS1", "SAT", "DC", "SCD", "PCECD", "NEOCD", "3DO", "JAGCD", "PSP" };
        foreach (var c in cdConsoles)
        {
            var target = _converter.GetTargetFormat(c, ".cue");
            Assert.NotNull(target);
            Assert.Equal(".chd", target!.Extension);
        }
    }

    [Fact]
    public void GetTargetFormat_AllCartridgeConsoles_ReturnZip()
    {
        var cartConsoles = new[] { "NES", "SNES", "N64", "GB", "GBC", "GBA", "NDS", "MD", "SMS", "GG", "PCE", "NEOGEO", "ARCADE" };
        foreach (var c in cartConsoles)
        {
            var target = _converter.GetTargetFormat(c, ".rom");
            Assert.NotNull(target);
            Assert.Equal(".zip", target!.Extension);
        }
    }

    /// <summary>
    /// Simple mock IToolRunner for unit testing conversion logic.
    /// </summary>
    private sealed class MockToolRunner : IToolRunner
    {
        public Dictionary<string, string> ToolPaths { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["chdman"] = @"C:\mock\chdman.exe",
            ["dolphintool"] = @"C:\mock\dolphintool.exe",
            ["7z"] = @"C:\mock\7z.exe",
            ["psxtract"] = @"C:\mock\psxtract.exe"
        };

        public ToolResult LastInvocation { get; private set; } = new(0, "", true);

        public string? FindTool(string toolName)
        {
            return ToolPaths.TryGetValue(toolName, out var p) ? p : null;
        }

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            // Simulate success
            LastInvocation = new ToolResult(0, "OK", true);
            return LastInvocation;
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
        {
            return new ToolResult(0, "OK", true);
        }
    }
}
