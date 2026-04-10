using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ReachConversionTests : IDisposable
{
    private readonly string _tempDir;

    public ReachConversionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ReachConversion_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void PlanForConsole_UsesCompoundNkitExtension_AndProducesMultiStepPlan()
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var registry = new ConversionRegistryLoader(
            Path.Combine(dataDir, "conversion-registry.json"),
            Path.Combine(dataDir, "consoles.json"));
        var planner = new ConversionPlanner(
            registry,
            toolName => toolName switch
            {
                "nkit" => @"C:\tools\NKitProcessingApp.exe",
                "dolphintool" => @"C:\tools\DolphinTool.exe",
                _ => null
            },
            _ => 1024);

        var sourcePath = Path.Combine(_tempDir, "game.nkit.iso");
        File.WriteAllText(sourcePath, "nkit");

        var converter = new FormatConverterAdapter(new NullToolRunner(), null, registry, planner, executor: null);
        var plan = converter.PlanForConsole(sourcePath, "GC");

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Steps.Count);
        Assert.Equal(".nkit.iso", plan.Steps[0].InputExtension);
        Assert.Equal(".iso", plan.Steps[0].OutputExtension);
        Assert.Equal(".rvz", plan.Steps[1].OutputExtension);
        Assert.True(plan.RequiresReview);
    }

    [Fact]
    public void ConvertForConsole_ReviewRequiredPlan_IsBlocked_WhenApprovalMissing()
    {
        var sourcePath = Path.Combine(_tempDir, "game.nkit.iso");
        File.WriteAllText(sourcePath, "nkit");

        var plan = new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = "GC",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossy,
            Safety = ConversionSafety.Acceptable,
            Steps =
            [
                new ConversionStep
                {
                    Order = 0,
                    InputExtension = ".nkit.iso",
                    OutputExtension = ".iso",
                    Capability = new ConversionCapability
                    {
                        SourceExtension = ".nkit.iso",
                        TargetExtension = ".iso",
                        Tool = new ToolRequirement { ToolName = "nkit", ExpectedHash = "abc" },
                        Command = "expand",
                        ResultIntegrity = SourceIntegrity.Lossy,
                        Lossless = false,
                        Cost = 1,
                        Verification = VerificationMethod.FileExistenceCheck
                    },
                    IsIntermediate = false
                }
            ]
        };

        var converter = new FormatConverterAdapter(
            new NullToolRunner(),
            null,
            registry: null,
            planner: new FixedPlanner(plan),
            executor: new ThrowingExecutor());

        var result = converter.ConvertForConsole(sourcePath, "GC");

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.Equal("review-required", result.Reason);
    }

    private sealed class NullToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => null;
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null) => new(1, "not-used", false);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => new(1, "not-used", false);
    }

    private sealed class FixedPlanner(ConversionPlan plan) : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension) => plan;
        public IReadOnlyList<ConversionPlan> PlanBatch(IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(_ => plan).ToArray();
    }

    private sealed class ThrowingExecutor : IConversionExecutor
    {
        public ConversionResult Execute(ConversionPlan plan, Action<ConversionStep, ConversionStepResult>? onStepComplete = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Executor should not be called for blocked review-required plans.");
    }
}
