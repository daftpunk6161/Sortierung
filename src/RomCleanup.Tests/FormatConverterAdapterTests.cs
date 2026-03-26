using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using System.IO.Compression;
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

    [Theory]
    [InlineData("ARCADE", ".zip")]
    [InlineData("NEOGEO", ".zip")]
    [InlineData("SWITCH", ".nsp")]
    [InlineData("PS3", ".iso")]
    [InlineData("3DS", ".3ds")]
    [InlineData("VITA", ".vpk")]
    [InlineData("DOS", ".exe")]
    public void GetTargetFormat_BlockedAutoSystems_ReturnsNull(string console, string ext)
    {
        Assert.Null(_converter.GetTargetFormat(console, ext));
    }

    [Theory]
    [InlineData("XBOX", ".iso")]
    [InlineData("X360", ".iso")]
    [InlineData("WIIU", ".wux")]
    [InlineData("PC98", ".hdi")]
    [InlineData("X68K", ".xdf")]
    public void GetTargetFormat_ManualOnlySystems_AutoSelectionReturnsNull(string console, string ext)
    {
        Assert.Null(_converter.GetTargetFormat(console, ext));
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

    [Fact]
    public void ConvertForConsole_NoConversionPath_ZipSource_FallsBackToLegacyConversion()
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"conv_zip_fallback_{Guid.NewGuid():N}.zip");
        var expectedTarget = Path.ChangeExtension(zipPath, ".chd");

        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var cueEntry = archive.CreateEntry("game.cue");
                using var cueWriter = new StreamWriter(cueEntry.Open());
                cueWriter.Write("FILE \"game.bin\" BINARY");
            }

            var converter = new FormatConverterAdapter(
                _tools,
                null,
                registry: null,
                planner: new NonExecutablePlanner("no-conversion-path", ConversionSafety.Safe),
                executor: new ThrowingExecutor());

            var result = converter.ConvertForConsole(zipPath, "PS1");

            Assert.Equal(ConversionOutcome.Success, result.Outcome);
            Assert.Equal(expectedTarget, result.TargetPath);
            Assert.True(File.Exists(expectedTarget));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            if (File.Exists(expectedTarget)) File.Delete(expectedTarget);
        }
    }

    [Fact]
    public void ConvertForConsole_NoConversionPath_NonArchive_DoesNotFallback()
    {
        var isoPath = Path.Combine(Path.GetTempPath(), $"conv_no_fallback_{Guid.NewGuid():N}.iso");
        try
        {
            File.WriteAllBytes(isoPath, [1, 2, 3]);

            var converter = new FormatConverterAdapter(
                _tools,
                null,
                registry: null,
                planner: new NonExecutablePlanner("no-conversion-path", ConversionSafety.Safe),
                executor: new ThrowingExecutor());

            var result = converter.ConvertForConsole(isoPath, "PS1");

            Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
            Assert.Equal("no-conversion-path", result.Reason);
        }
        finally
        {
            if (File.Exists(isoPath)) File.Delete(isoPath);
        }
    }

    [Fact]
    public void PlanForConsole_WithPlanner_ReturnsPlan()
    {
        var isoPath = Path.Combine(Path.GetTempPath(), $"conv_plan_{Guid.NewGuid():N}.iso");
        try
        {
            File.WriteAllBytes(isoPath, [1, 2, 3]);

            var expectedPlan = new ConversionPlan
            {
                SourcePath = isoPath,
                ConsoleKey = "XBOX",
                Policy = ConversionPolicy.ManualOnly,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Risky,
                Steps = Array.Empty<ConversionStep>(),
                SkipReason = "manual-only"
            };

            var converter = new FormatConverterAdapter(
                _tools,
                null,
                registry: null,
                planner: new FixedPlanner(expectedPlan),
                executor: new ThrowingExecutor());

            var plan = converter.PlanForConsole(isoPath, "XBOX");

            Assert.NotNull(plan);
            Assert.Equal("XBOX", plan!.ConsoleKey);
            Assert.Equal(ConversionPolicy.ManualOnly, plan.Policy);
            Assert.Equal(ConversionSafety.Risky, plan.Safety);
        }
        finally
        {
            if (File.Exists(isoPath)) File.Delete(isoPath);
        }
    }

    [Fact]
    public void PlanForConsole_WithoutPlanner_ReturnsNull()
    {
        var isoPath = Path.Combine(Path.GetTempPath(), $"conv_plan_none_{Guid.NewGuid():N}.iso");
        try
        {
            File.WriteAllBytes(isoPath, [1, 2, 3]);
            var plan = _converter.PlanForConsole(isoPath, "PS2");
            Assert.Null(plan);
        }
        finally
        {
            if (File.Exists(isoPath)) File.Delete(isoPath);
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
        var cartConsoles = new[] { "NES", "SNES", "N64", "GB", "GBC", "GBA", "NDS", "MD", "SMS", "GG", "PCE" };
        foreach (var c in cartConsoles)
        {
            var target = _converter.GetTargetFormat(c, ".rom");
            Assert.NotNull(target);
            Assert.Equal(".zip", target!.Extension);
        }
    }

    [Fact]
    public void Convert_Ps2IsoUnder700Mb_UsesCreateCdInsteadOfCreateDvd()
    {
        var target = new ConversionTarget(".chd", "chdman", "createdvd");
        var isoPath = Path.Combine(Path.GetTempPath(), $"ps2_cd_{Guid.NewGuid():N}.iso");
        var expectedTarget = Path.ChangeExtension(isoPath, ".chd");

        try
        {
            using (var fs = new FileStream(isoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                fs.SetLength(699L * 1024 * 1024);

            var result = _converter.Convert(isoPath, target);

            Assert.Equal(ConversionOutcome.Success, result.Outcome);
            Assert.Contains("createcd", _tools.LastArgs, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("createdvd", _tools.LastArgs, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(isoPath)) File.Delete(isoPath);
            if (File.Exists(expectedTarget)) File.Delete(expectedTarget);
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
        public string[] LastArgs { get; private set; } = Array.Empty<string>();

        public string? FindTool(string toolName)
        {
            return ToolPaths.TryGetValue(toolName, out var p) ? p : null;
        }

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            // Simulate success
            LastArgs = arguments;
            LastInvocation = new ToolResult(0, "OK", true);

            // Create output file for converters that require output existence checks.
            var outputIndex = Array.IndexOf(arguments, "-o");
            if (outputIndex >= 0 && outputIndex < arguments.Length - 1)
            {
                var outputPath = arguments[outputIndex + 1];
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(outputPath))
                    File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            }

            return LastInvocation;
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
        {
            return new ToolResult(0, "OK", true);
        }
    }

    private sealed class NonExecutablePlanner(string skipReason, ConversionSafety safety) : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = consoleKey,
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = safety,
                Steps = Array.Empty<ConversionStep>(),
                SkipReason = skipReason
            };
        }

        public IReadOnlyList<ConversionPlan> PlanBatch(IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(c => Plan(c.Path, c.ConsoleKey, c.Extension)).ToArray();
    }

    private sealed class ThrowingExecutor : IConversionExecutor
    {
        public ConversionResult Execute(
            ConversionPlan plan,
            Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Executor should not be called in fallback tests.");
        }
    }

    private sealed class FixedPlanner(ConversionPlan plan) : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
            => plan;

        public IReadOnlyList<ConversionPlan> PlanBatch(IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(_ => plan).ToArray();
    }
}
