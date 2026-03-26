using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using Xunit;

namespace RomCleanup.Tests.Conversion;

/// <summary>
/// Regression tests for FormatConverterAdapter facade: planner/executor delegation,
/// legacy fallback, blocked system handling, and registry precedence.
/// </summary>
public sealed class ConversionFacadeRegressionTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    private string CreateTempFile(string extension, byte[]? content = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"facade_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        _tempFiles.Add(path);
        // Also track potential output file
        _tempFiles.Add(Path.ChangeExtension(path, ".chd"));
        _tempFiles.Add(Path.ChangeExtension(path, ".rvz"));
        _tempFiles.Add(Path.ChangeExtension(path, ".zip"));
        return path;
    }

    // -----------------------------------------------------------------------
    // ConvertForConsole delegation tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ConvertForConsole_PlannerAndExecutorAvailable_DelegatesToExecutor()
    {
        var source = CreateTempFile(".cue");
        var expectedTarget = Path.ChangeExtension(source, ".chd");
        var executor = new RecordingExecutor(ConversionOutcome.Success, expectedTarget);
        var planner = new ExecutablePlanner(".chd");

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            planner: planner,
            executor: executor);

        var result = adapter.ConvertForConsole(source, "PS1");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Equal(expectedTarget, result.TargetPath);
        Assert.True(executor.WasCalled, "Executor.Execute should have been called");
        Assert.Equal("PS1", planner.LastConsoleKey);
    }

    [Fact]
    public void ConvertForConsole_NoPlannerOrExecutor_FallsBackToLegacy()
    {
        var source = CreateTempFile(".cue");
        var tools = new StubToolRunner();

        // Constructor without planner/executor → legacy path
        var adapter = new FormatConverterAdapter(tools);

        var result = adapter.ConvertForConsole(source, "PS1");

        // Legacy path: tries Convert() with chdman which creates output
        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.NotNull(result.TargetPath);
    }

    [Fact]
    public void ConvertForConsole_BlockedSystem_ReturnsBlockedOutcome()
    {
        var source = CreateTempFile(".zip");
        var planner = new BlockedPlanner();
        var executor = new RecordingExecutor(ConversionOutcome.Success, null);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            planner: planner,
            executor: executor);

        var result = adapter.ConvertForConsole(source, "ARCADE");

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.False(executor.WasCalled, "Executor should not be called for blocked system");
    }

    [Fact]
    public void ConvertForConsole_NonExecutablePlan_NonArchive_ReturnsSkipped()
    {
        var source = CreateTempFile(".iso");
        var planner = new SkippedPlanner("unsupported-source", ConversionSafety.Safe);
        var executor = new RecordingExecutor(ConversionOutcome.Success, null);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            planner: planner,
            executor: executor);

        var result = adapter.ConvertForConsole(source, "UNKNOWN_CONSOLE");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("unsupported-source", result.Reason);
        Assert.NotNull(result.Plan);
        Assert.False(executor.WasCalled);
    }

    [Fact]
    public void ConvertForConsole_SourceNotFound_ReturnsError()
    {
        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            planner: new ExecutablePlanner(".chd"),
            executor: new RecordingExecutor(ConversionOutcome.Success, null));

        var result = adapter.ConvertForConsole(@"C:\nonexistent\file.cue", "PS1");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("source-not-found", result.Reason);
    }

    [Fact]
    public void ConvertForConsole_NullPlanner_NonNullExecutor_FallsBackToLegacy()
    {
        var source = CreateTempFile(".cue");
        var executor = new RecordingExecutor(ConversionOutcome.Success, null);

        // planner = null → legacy path even though executor is present
        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            executor: executor);

        var result = adapter.ConvertForConsole(source, "PS1");

        // Should take legacy path (GetTargetFormat→Convert) since planner is null
        // The executor gets called from Convert→TryExecuteSingleStepPlan but via different code path
        Assert.True(result.Outcome is ConversionOutcome.Success or ConversionOutcome.Skipped);
    }

    // -----------------------------------------------------------------------
    // GetTargetFormat registry precedence tests
    // -----------------------------------------------------------------------

    [Fact]
    public void GetTargetFormat_RegistryOverridesDefaultBestFormats()
    {
        var registry = new StubRegistry(
            preferredTarget: ".rvz",
            policy: ConversionPolicy.Auto,
            capabilities: [MakeCapability(".iso", ".rvz", "customtool", "convert")]);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: registry,
            planner: null,
            executor: null);

        // PS1 default is .chd/chdman, but registry says .rvz/customtool
        var target = adapter.GetTargetFormat("PS1", ".iso");

        Assert.NotNull(target);
        Assert.Equal(".rvz", target!.Extension);
        Assert.Equal("customtool", target.ToolName);
    }

    [Fact]
    public void GetTargetFormat_RegistryPolicyNone_BlocksRegistryTarget()
    {
        var registry = new StubRegistry(
            preferredTarget: ".chd",
            policy: ConversionPolicy.None,
            capabilities: [MakeCapability(".iso", ".chd", "chdman", "createcd")]);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: registry,
            planner: null,
            executor: null);

        // ARCADE is also in BlockedAutoSystems, so use a console that has a default
        // but registry blocks it with Policy.None
        var target = adapter.GetTargetFormat("PS1", ".iso");

        // Registry returns null (Policy.None), falls back to DefaultBestFormats
        Assert.NotNull(target);
        Assert.Equal(".chd", target!.Extension);
        Assert.Equal("chdman", target.ToolName);
    }

    [Fact]
    public void GetTargetFormat_RegistryManualOnly_FallsBackToDefault()
    {
        var registry = new StubRegistry(
            preferredTarget: ".chd",
            policy: ConversionPolicy.ManualOnly,
            capabilities: [MakeCapability(".cue", ".chd", "chdman", "createcd")]);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: registry,
            planner: null,
            executor: null);

        var target = adapter.GetTargetFormat("PS1", ".cue");

        // ManualOnly in registry → TryGetRegistryTarget returns null → DefaultBestFormats used
        Assert.NotNull(target);
        Assert.Equal(".chd", target!.Extension);
        Assert.Equal("chdman", target.ToolName);
    }

    // -----------------------------------------------------------------------
    // Convert delegation tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Convert_WithExecutor_DelegatesToSingleStepPlan()
    {
        var source = CreateTempFile(".cue");
        var expectedTarget = Path.ChangeExtension(source, ".chd");
        var executor = new RecordingExecutor(ConversionOutcome.Success, expectedTarget);

        var adapter = new FormatConverterAdapter(
            new StubToolRunner(),
            bestFormats: null,
            registry: null,
            planner: null,
            executor: executor);

        var target = new ConversionTarget(".chd", "chdman", "createcd");
        var result = adapter.Convert(source, target);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.True(executor.WasCalled, "Executor should be called via TryExecuteSingleStepPlan");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ConversionCapability MakeCapability(
        string sourceExt, string targetExt, string tool, string command)
    {
        return new ConversionCapability
        {
            SourceExtension = sourceExt,
            TargetExtension = targetExt,
            Tool = new ToolRequirement { ToolName = tool },
            Command = command,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1,
            Verification = VerificationMethod.None,
            Condition = ConversionCondition.None
        };
    }

    // -----------------------------------------------------------------------
    // Stub/mock implementations
    // -----------------------------------------------------------------------

    private sealed class StubToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => $@"C:\mock\{toolName}.exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            // Create output file for converters that check output existence
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
            return new ToolResult(0, "OK", true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => new(0, "OK", true);
    }

    private sealed class ExecutablePlanner(string targetExtension) : IConversionPlanner
    {
        public string? LastConsoleKey { get; private set; }

        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
        {
            LastConsoleKey = consoleKey;
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = consoleKey,
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 0,
                        InputExtension = sourceExtension,
                        OutputExtension = targetExtension,
                        Capability = new ConversionCapability
                        {
                            SourceExtension = sourceExtension,
                            TargetExtension = targetExtension,
                            Tool = new ToolRequirement { ToolName = "chdman" },
                            Command = "createcd",
                            ResultIntegrity = SourceIntegrity.Lossless,
                            Lossless = true,
                            Cost = 0,
                            Verification = VerificationMethod.ChdmanVerify
                        },
                        IsIntermediate = false
                    }
                ]
            };
        }

        public IReadOnlyList<ConversionPlan> PlanBatch(
            IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(c => Plan(c.Path, c.ConsoleKey, c.Extension)).ToArray();
    }

    private sealed class BlockedPlanner : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = consoleKey,
                Policy = ConversionPolicy.None,
                SourceIntegrity = SourceIntegrity.Unknown,
                Safety = ConversionSafety.Blocked,
                Steps = [],
                SkipReason = "blocked-by-policy"
            };
        }

        public IReadOnlyList<ConversionPlan> PlanBatch(
            IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(c => Plan(c.Path, c.ConsoleKey, c.Extension)).ToArray();
    }

    private sealed class SkippedPlanner(string reason, ConversionSafety safety) : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = consoleKey,
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Unknown,
                Safety = safety,
                Steps = [],
                SkipReason = reason
            };
        }

        public IReadOnlyList<ConversionPlan> PlanBatch(
            IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(c => Plan(c.Path, c.ConsoleKey, c.Extension)).ToArray();
    }

    private sealed class RecordingExecutor(ConversionOutcome outcome, string? targetPath) : IConversionExecutor
    {
        public bool WasCalled { get; private set; }

        public ConversionResult Execute(
            ConversionPlan plan,
            Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return new ConversionResult(plan.SourcePath, targetPath, outcome)
            {
                Plan = plan,
                SourceIntegrity = plan.SourceIntegrity,
                Safety = plan.Safety,
                VerificationResult = VerificationStatus.Verified,
                DurationMs = 42
            };
        }
    }

    private sealed class StubRegistry(
        string? preferredTarget,
        ConversionPolicy policy,
        IReadOnlyList<ConversionCapability> capabilities) : IConversionRegistry
    {
        public IReadOnlyList<ConversionCapability> GetCapabilities() => capabilities;
        public ConversionPolicy GetPolicy(string consoleKey) => policy;
        public string? GetPreferredTarget(string consoleKey) => preferredTarget;
        public IReadOnlyList<string> GetAlternativeTargets(string consoleKey) => [];
    }
}
