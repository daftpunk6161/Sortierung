using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Contracts.Ports;
using Xunit;

namespace RomCleanup.Tests;

public sealed class ConversionPipelineTests
{
    // =========================================================================
    //  DiskSpaceCheck Tests
    // =========================================================================

    [Fact]
    public void CheckDiskSpace_NonExistentFile_ReturnsFalse()
    {
        var result = ConversionPipeline.CheckDiskSpace(
            @"C:\nonexistent\file.iso", @"C:\temp");
        Assert.False(result.Ok);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDiskSpace_ExistingFile_ReturnsOk()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[1024]);
            var result = ConversionPipeline.CheckDiskSpace(tempFile, Path.GetTempPath());
            Assert.True(result.Ok);
            Assert.True(result.AvailableBytes > 0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // =========================================================================
    //  Pipeline Building Tests
    // =========================================================================

    [Fact]
    public void BuildCsoToChdPipeline_HasTwoSteps()
    {
        var pipeline = ConversionPipeline.BuildCsoToChdPipeline(
            @"D:\roms\game.cso", @"D:\output");

        Assert.Equal(2, pipeline.Steps.Count);
        Assert.Equal("ciso", pipeline.Steps[0].Tool);
        Assert.Equal("decompress", pipeline.Steps[0].Action);
        Assert.True(pipeline.Steps[0].IsTemp); // intermediate ISO is temp
        Assert.Equal("chdman", pipeline.Steps[1].Tool);
        Assert.Equal("createcd", pipeline.Steps[1].Action);
        Assert.False(pipeline.Steps[1].IsTemp); // final CHD is NOT temp
    }

    [Fact]
    public void BuildCsoToChdPipeline_CorrectPaths()
    {
        var pipeline = ConversionPipeline.BuildCsoToChdPipeline(
            @"D:\roms\game.cso", @"D:\output");

        Assert.EndsWith(".iso", pipeline.Steps[0].Output);
        Assert.EndsWith(".chd", pipeline.Steps[1].Output);
        Assert.Equal(pipeline.Steps[0].Output, pipeline.Steps[1].Input);
    }

    // =========================================================================
    //  Pipeline Execution Tests (DryRun)
    // =========================================================================

    [Fact]
    public void Execute_DryRun_AllStepsSkipped()
    {
        var tools = new FakeToolRunner();
        var fs = new FakeFs();
        var cvPipeline = new ConversionPipeline(tools, fs);

        var def = ConversionPipeline.BuildCsoToChdPipeline(@"D:\game.cso", @"D:\out");
        var results = cvPipeline.Execute(def, mode: "DryRun");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Skipped));
        Assert.All(results, r => Assert.Equal("dryrun", r.Status));
    }

    // Fakes
    private sealed class FakeToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => $@"C:\tools\{toolName}.exe";
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "success", true);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "success", true);
    }

    private sealed class FakeFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => [];
        public bool MoveItemSafely(string src, string dest) => true;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
    }
}
