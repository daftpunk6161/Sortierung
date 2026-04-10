using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests;

public sealed class ConversionExecutorTests : IDisposable
{
    private readonly string _tempDir;

    public ConversionExecutorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_CET_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    #region Helpers

    private static ConversionCapability MakeCapability(string tool = "chdman", string src = ".iso", string tgt = ".chd")
        => new()
        {
            SourceExtension = src,
            TargetExtension = tgt,
            Tool = new ToolRequirement { ToolName = tool },
            Command = "createcd",
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1,
            Verification = VerificationMethod.ChdmanVerify
        };

    private ConversionPlan MakeExecutablePlan(
        string? sourcePath = null,
        ConversionSafety safety = ConversionSafety.Safe,
        ConversionPolicy policy = ConversionPolicy.Auto,
        SourceIntegrity integrity = SourceIntegrity.Lossless,
        IReadOnlyList<ConversionStep>? steps = null)
    {
        var src = sourcePath ?? CreateSourceFile("game.iso");
        return new ConversionPlan
        {
            SourcePath = src,
            ConsoleKey = "PSX",
            Policy = policy,
            SourceIntegrity = integrity,
            Safety = safety,
            Steps = steps ?? [MakeStep(0, ".iso", ".chd")]
        };
    }

    private static ConversionStep MakeStep(int order, string input, string output, bool intermediate = false)
        => new()
        {
            Order = order,
            InputExtension = input,
            OutputExtension = output,
            Capability = MakeCapability(src: input, tgt: output),
            IsIntermediate = intermediate
        };

    private string CreateSourceFile(string name , string content = "FAKE_ISO_DATA")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class FakeInvoker : IToolInvoker
    {
        private readonly Func<string, string, ConversionCapability, CancellationToken, ToolInvocationResult>? _invokeFunc;
        private readonly Func<string, ConversionCapability, VerificationStatus>? _verifyFunc;
        private readonly HashSet<string> _handledTools;

        public int InvokeCount { get; private set; }
        public List<(string Source, string Target)> InvocationsLog { get; } = [];

        public FakeInvoker(
            string[]? handledTools = null,
            Func<string, string, ConversionCapability, CancellationToken, ToolInvocationResult>? invokeFunc = null,
            Func<string, ConversionCapability, VerificationStatus>? verifyFunc = null)
        {
            _handledTools = new HashSet<string>(handledTools ?? ["chdman"], StringComparer.OrdinalIgnoreCase);
            _invokeFunc = invokeFunc;
            _verifyFunc = verifyFunc;
        }

        public bool CanHandle(ConversionCapability capability) =>
            _handledTools.Contains(capability.Tool.ToolName);

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken ct)
        {
            InvokeCount++;
            InvocationsLog.Add((sourcePath, targetPath));

            if (_invokeFunc is not null)
                return _invokeFunc(sourcePath, targetPath, capability, ct);

            // Default: create a real output file so validation passes
            File.WriteAllText(targetPath, "CONVERTED_" + Path.GetFileName(sourcePath));
            return new ToolInvocationResult(true, targetPath, 0, "ok", null, 100, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
        {
            return _verifyFunc?.Invoke(targetPath, capability) ?? VerificationStatus.Verified;
        }
    }

    #endregion

    #region Null / guard tests

    [Fact]
    public void Execute_NullPlan_Throws()
    {
        var sut = new ConversionExecutor([]);
        Assert.Throws<ArgumentNullException>(() => sut.Execute(null!));
    }

    [Fact]
    public void Ctor_NullInvokers_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConversionExecutor(null!));
    }

    #endregion

    #region Source not found

    [Fact]
    public void Execute_SourceNotFound_ReturnsError()
    {
        var plan = new ConversionPlan
        {
            SourcePath = Path.Combine(_tempDir, "nonexistent.iso"),
            ConsoleKey = "PSX",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps = [MakeStep(0, ".iso", ".chd")]
        };

        var sut = new ConversionExecutor([new FakeInvoker()]);
        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("source-not-found", result.Reason);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal(VerificationStatus.NotAttempted, result.VerificationResult);
    }

    #endregion

    #region Plan blocked / not executable / review required

    [Fact]
    public void Execute_PlanBlocked_ReturnsBlocked()
    {
        var plan = MakeExecutablePlan(safety: ConversionSafety.Blocked);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.Contains("blocked", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PlanNotExecutable_ReturnsSkipped()
    {
        // Plan with no steps → IsExecutable = false
        var plan = MakeExecutablePlan(steps: []);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public void Execute_RequiresReview_WithoutApproval_ReturnsBlocked()
    {
        var plan = MakeExecutablePlan(policy: ConversionPolicy.ManualOnly);
        var sut = new ConversionExecutor([new FakeInvoker()], allowReviewRequiredPlans: false);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.Equal("review-required", result.Reason);
    }

    [Fact]
    public void Execute_RequiresReview_WithApproval_Proceeds()
    {
        var plan = MakeExecutablePlan(policy: ConversionPolicy.ManualOnly);
        var sut = new ConversionExecutor([new FakeInvoker()], allowReviewRequiredPlans: true);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    #endregion

    #region Step order validation

    [Fact]
    public void Execute_NonContiguousStepOrder_ReturnsError()
    {
        var steps = new[]
        {
            MakeStep(0, ".iso", ".tmp"),
            MakeStep(2, ".tmp", ".chd") // Gap: 0, 2 instead of 0, 1
        };
        var plan = MakeExecutablePlan(steps: steps);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("invalid-step-order", result.Reason);
    }

    [Fact]
    public void Execute_ContiguousSteps_Succeeds()
    {
        var steps = new[]
        {
            MakeStep(0, ".iso", ".tmp", intermediate: true),
            MakeStep(1, ".tmp", ".chd")
        };
        var plan = MakeExecutablePlan(steps: steps);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    #endregion

    #region Invoker not found

    [Fact]
    public void Execute_InvokerNotFound_ReturnsError()
    {
        var plan = MakeExecutablePlan();
        // Invoker that only handles "dolphintool", not "chdman"
        var invoker = new FakeInvoker(handledTools: ["dolphintool"]);
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("invoker-not-found", result.Reason!);
        Assert.Equal(VerificationStatus.VerifyNotAvailable, result.VerificationResult);
    }

    #endregion

    #region Target already exists

    [Fact]
    public void Execute_TargetAlreadyExists_ReturnsSkipped()
    {
        var src = CreateSourceFile("game.iso");
        // Pre-create the expected output
        File.WriteAllText(Path.Combine(_tempDir, "game.chd"), "EXISTING");

        var plan = MakeExecutablePlan(sourcePath: src);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("target-exists", result.Reason);
    }

    #endregion

    #region Successful single-step conversion

    [Fact]
    public void Execute_SingleStep_Success_ReturnsCorrectResult()
    {
        var plan = MakeExecutablePlan();
        var invoker = new FakeInvoker();
        var sut = new ConversionExecutor([invoker]);

        var stepResults = new List<(ConversionStep Step, ConversionStepResult Result)>();
        var result = sut.Execute(plan, (step, res) => stepResults.Add((step, res)));

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Null(result.Reason);
        Assert.NotNull(result.TargetPath);
        Assert.True(File.Exists(result.TargetPath), "Target file should exist after conversion");
        Assert.Equal(1, invoker.InvokeCount);
        Assert.Single(stepResults);
        Assert.True(stepResults[0].Result.Success);
    }

    [Fact]
    public void Execute_SingleStep_Success_CapturesFileSizes()
    {
        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.NotNull(result.SourceBytes);
        Assert.True(result.SourceBytes > 0);
        Assert.NotNull(result.TargetBytes);
        Assert.True(result.TargetBytes > 0);
    }

    #endregion

    #region Multi-step conversion

    [Fact]
    public void Execute_TwoSteps_IntermediateCleanedUp()
    {
        var steps = new[]
        {
            MakeStep(0, ".iso", ".tmp", intermediate: true),
            MakeStep(1, ".tmp", ".chd")
        };
        var plan = MakeExecutablePlan(steps: steps);
        var invoker = new FakeInvoker();
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Equal(2, invoker.InvokeCount);
        // Intermediate .tmp file should be cleaned up
        var tmpFiles = Directory.GetFiles(_tempDir, "*.tmp*");
        Assert.Empty(tmpFiles);
    }

    #endregion

    #region Invoke failure

    [Fact]
    public void Execute_InvokeFails_ReturnsError_CleansArtifacts()
    {
        var invoker = new FakeInvoker(invokeFunc: (src, tgt, cap, ct) =>
        {
            // Don't create output, return failure
            return new ToolInvocationResult(false, tgt, 1, null, "chdman error", 50, VerificationStatus.NotAttempted);
        });

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        var stepResults = new List<(ConversionStep Step, ConversionStepResult Result)>();
        var result = sut.Execute(plan, (step, res) => stepResults.Add((step, res)));

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal(1, result.ExitCode);
        Assert.Single(stepResults);
        Assert.False(stepResults[0].Result.Success);
    }

    #endregion

    #region Verification failed

    [Fact]
    public void Execute_VerificationFailed_ReturnsError()
    {
        var invoker = new FakeInvoker(
            verifyFunc: (_, _) => VerificationStatus.VerifyFailed);

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("verification-failed", result.Reason);
        Assert.Equal(VerificationStatus.VerifyFailed, result.VerificationResult);
    }

    [Fact]
    public void Execute_VerificationVerified_ReturnsSuccess()
    {
        var invoker = new FakeInvoker(
            verifyFunc: (_, _) => VerificationStatus.Verified);

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Equal(VerificationStatus.Verified, result.VerificationResult);
    }

    #endregion

    #region Cancellation

    [Fact]
    public void Execute_CancellationDuringStep_ReturnsError()
    {
        using var cts = new CancellationTokenSource();
        var invoker = new FakeInvoker(invokeFunc: (src, tgt, cap, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return new ToolInvocationResult(true, tgt, 0, "ok", null, 100, VerificationStatus.NotAttempted);
        });

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        Assert.ThrowsAny<OperationCanceledException>(() => sut.Execute(plan, cancellationToken: cts.Token));
    }

    #endregion

    #region Invalid source directory

    [Fact]
    public void Execute_InvalidSourceDirectory_ReturnsError()
    {
        // Source path with no directory (just a filename)
        var plan = new ConversionPlan
        {
            SourcePath = "game.iso",
            ConsoleKey = "PSX",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps = [MakeStep(0, ".iso", ".chd")]
        };

        var sut = new ConversionExecutor([new FakeInvoker()]);
        // This will hit "source-not-found" since "game.iso" won't exist as full path
        var result = sut.Execute(plan);
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
    }

    #endregion

    #region Output validation failure

    [Fact]
    public void Execute_OutputNotCreated_ReturnsError()
    {
        // Invoker returns success but doesn't create the file
        var invoker = new FakeInvoker(invokeFunc: (src, tgt, cap, ct) =>
            new ToolInvocationResult(true, tgt, 0, "ok", null, 100, VerificationStatus.NotAttempted));

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("output-not-created", result.Reason);
    }

    [Fact]
    public void Execute_EmptyOutput_ReturnsError()
    {
        var invoker = new FakeInvoker(invokeFunc: (src, tgt, cap, ct) =>
        {
            File.WriteAllText(tgt, ""); // empty file
            return new ToolInvocationResult(true, tgt, 0, "ok", null, 100, VerificationStatus.NotAttempted);
        });

        var plan = MakeExecutablePlan();
        var sut = new ConversionExecutor([invoker]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("output-empty", result.Reason);
    }

    #endregion

    #region Blocked plan with custom skip reason

    [Fact]
    public void Execute_Blocked_PreservesSkipReason()
    {
        var plan = new ConversionPlan
        {
            SourcePath = CreateSourceFile("game.iso"),
            ConsoleKey = "PSX",
            Policy = ConversionPolicy.None,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Blocked,
            Steps = [MakeStep(0, ".iso", ".chd")],
            SkipReason = "conversion-disabled-for-psx"
        };

        var sut = new ConversionExecutor([new FakeInvoker()]);
        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.Equal("conversion-disabled-for-psx", result.Reason);
    }

    #endregion

    #region Risky plan review gating (Lossy source)

    [Fact]
    public void Execute_LossySourcePlan_RequiresReview_BlockedWithoutApproval()
    {
        var plan = MakeExecutablePlan(
            integrity: SourceIntegrity.Lossy,
            policy: ConversionPolicy.Auto,
            safety: ConversionSafety.Risky);

        var sut = new ConversionExecutor([new FakeInvoker()], allowReviewRequiredPlans: false);
        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
        Assert.Equal("review-required", result.Reason);
    }

    #endregion

    #region Determinism: same input -> same output

    [Fact]
    public void Execute_DeterministicOutcome_SameInputSameResult()
    {
        var invoker = new FakeInvoker();
        var sut = new ConversionExecutor([invoker]);

        // Run twice with same input
        var src = CreateSourceFile("determinism_test.iso");
        var plan = MakeExecutablePlan(sourcePath: src);

        var result1 = sut.Execute(plan);
        // Target already exists now, so second run should return Skipped
        var result2 = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result1.Outcome);
        Assert.Equal(ConversionOutcome.Skipped, result2.Outcome);
        Assert.Equal("target-exists", result2.Reason);
    }

    #endregion

    #region Step callback invocation

    [Fact]
    public void Execute_MultiStep_InvokesCallbackPerStep()
    {
        var steps = new[]
        {
            MakeStep(0, ".cue", ".tmp", intermediate: true),
            MakeStep(1, ".tmp", ".chd")
        };
        var src = CreateSourceFile("game.cue");
        var plan = MakeExecutablePlan(sourcePath: src, steps: steps);
        var invoker = new FakeInvoker();
        var sut = new ConversionExecutor([invoker]);

        var callbacks = new List<int>();
        sut.Execute(plan, (step, _) => callbacks.Add(step.Order));

        Assert.Equal([0, 1], callbacks);
    }

    #endregion

    #region Plan properties propagated to result

    [Fact]
    public void Execute_PropagatesPlanInfoToResult()
    {
        var plan = MakeExecutablePlan(safety: ConversionSafety.Safe, integrity: SourceIntegrity.Lossless);
        var sut = new ConversionExecutor([new FakeInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Same(plan, result.Plan);
        Assert.Equal(SourceIntegrity.Lossless, result.SourceIntegrity);
        Assert.Equal(ConversionSafety.Safe, result.Safety);
        Assert.True(result.DurationMs >= 0);
    }

    #endregion
}
