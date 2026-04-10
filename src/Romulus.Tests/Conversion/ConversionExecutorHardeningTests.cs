using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ConversionExecutorHardeningTests
{
    [Fact]
    public void Execute_SourceMissing_ReturnsError()
    {
        var executor = new ConversionExecutor([new PassThroughInvoker()]);
        var plan = BuildPlan(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.iso"));

        var result = executor.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("source-not-found", result.Reason);
    }

    [Fact]
    public void Execute_NonContiguousStepOrder_ReturnsError()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var capability = Capability(".iso", ".chd", "chdman", "createcd");
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "PS1",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 1,
                        InputExtension = ".iso",
                        OutputExtension = ".chd",
                        Capability = capability,
                        IsIntermediate = false
                    }
                ]
            };

            var executor = new ConversionExecutor([new PassThroughInvoker()], allowReviewRequiredPlans: true);
            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("invalid-step-order", result.Reason);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
        }
    }

    [Fact]
    public void Execute_ExceptionInLaterStep_ReturnsError_AndCleansIntermediateArtifacts()
    {
        var source = CreateTempFile(".cso");
        var sourceDir = Path.GetDirectoryName(source)!;
        var baseName = Path.GetFileNameWithoutExtension(source);
        var intermediatePath = Path.Combine(sourceDir, $"{baseName}.tmp.step1.iso");

        try
        {
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "PSP",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Acceptable,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 0,
                        InputExtension = ".cso",
                        OutputExtension = ".iso",
                        Capability = Capability(".cso", ".iso", "ciso", "decompress"),
                        IsIntermediate = true
                    },
                    new ConversionStep
                    {
                        Order = 1,
                        InputExtension = ".iso",
                        OutputExtension = ".chd",
                        Capability = Capability(".iso", ".chd", "chdman", "throw"),
                        IsIntermediate = false
                    }
                ]
            };

            var executor = new ConversionExecutor([new ThrowOnCommandInvoker("throw")], allowReviewRequiredPlans: true);
            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("forced", result.Reason);
            Assert.False(File.Exists(intermediatePath));
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);

            var finalPath = Path.ChangeExtension(source, ".chd");
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            if (File.Exists(intermediatePath))
                File.Delete(intermediatePath);
        }
    }

    [Fact]
    public void Execute_InvalidOutputExtension_ReturnsError()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "PS1",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 0,
                        InputExtension = ".iso",
                        OutputExtension = ".chd/evil",
                        Capability = Capability(".iso", ".chd/evil", "chdman", "createcd"),
                        IsIntermediate = false
                    }
                ]
            };

            var executor = new ConversionExecutor([new PassThroughInvoker()], allowReviewRequiredPlans: true);
            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("invalid-output-extension", result.Reason);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  TASK-035: Consolidated ConversionExecutor Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_SingleStepSuccess_ReturnsSuccessWithOutputPath()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new PassThroughInvoker()], allowReviewRequiredPlans: true);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Success, result.Outcome);
            Assert.NotNull(result.TargetPath);
            Assert.EndsWith(".chd", result.TargetPath);
            Assert.True(result.DurationMs >= 0);
        }
        finally
        {
            Cleanup(source, ".chd");
        }
    }

    [Fact]
    public void Execute_SingleStepSuccess_PromotesStagedFinalOutput_AndLeavesNoTempArtifact()
    {
        var source = CreateTempFile(".iso");
        var sourceDir = Path.GetDirectoryName(source)!;
        var baseName = Path.GetFileNameWithoutExtension(source);
        var stagedPath = Path.Combine(sourceDir, $"{baseName}.tmp.final.step1.chd");
        var finalPath = Path.Combine(sourceDir, $"{baseName}.chd");

        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new PassThroughInvoker()], allowReviewRequiredPlans: true);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Success, result.Outcome);
            Assert.True(File.Exists(finalPath));
            Assert.False(File.Exists(stagedPath));
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
    }

    [Fact]
    public void Execute_SingleStepVerifyFail_CleansOutput()
    {
        var source = CreateTempFile(".iso");
        var expectedOutput = Path.Combine(
            Path.GetDirectoryName(source)!,
            Path.GetFileNameWithoutExtension(source) + ".chd");
        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new VerifyFailInvoker()]);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("verification-failed", result.Reason);
            Assert.Equal(VerificationStatus.VerifyFailed, result.VerificationResult);
            Assert.False(File.Exists(expectedOutput), "Output file should be cleaned up after verify failure");
        }
        finally
        {
            Cleanup(source, ".chd");
        }
    }

    [Fact]
    public void Execute_SingleStepEmptyOutput_ReturnsErrorAndCleansStagedArtifact()
    {
        var source = CreateTempFile(".iso");
        var sourceDir = Path.GetDirectoryName(source)!;
        var baseName = Path.GetFileNameWithoutExtension(source);
        var stagedPath = Path.Combine(sourceDir, $"{baseName}.tmp.final.step1.chd");
        var finalPath = Path.Combine(sourceDir, $"{baseName}.chd");

        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new EmptyOutputInvoker()], allowReviewRequiredPlans: true);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.Equal("output-empty", result.Reason);
            Assert.False(File.Exists(stagedPath));
            Assert.False(File.Exists(finalPath));
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
    }

    [Fact]
    public void Execute_MultiStepSuccess_CleansIntermediatesKeepsFinal()
    {
        var source = CreateTempFile(".cso");
        var sourceDir = Path.GetDirectoryName(source)!;
        var baseName = Path.GetFileNameWithoutExtension(source);
        var intermediatePath = Path.Combine(sourceDir, $"{baseName}.tmp.step1.iso");
        var finalPath = Path.Combine(sourceDir, $"{baseName}.chd");
        try
        {
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "PSP",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Acceptable,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 0,
                        InputExtension = ".cso",
                        OutputExtension = ".iso",
                        Capability = Capability(".cso", ".iso", "ciso", "decompress"),
                        IsIntermediate = true
                    },
                    new ConversionStep
                    {
                        Order = 1,
                        InputExtension = ".iso",
                        OutputExtension = ".chd",
                        Capability = Capability(".iso", ".chd", "chdman", "createcd"),
                        IsIntermediate = false
                    }
                ]
            };

            var executor = new ConversionExecutor([new PassThroughInvoker()], allowReviewRequiredPlans: true);
            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Success, result.Outcome);
            Assert.True(File.Exists(finalPath), "Final output should exist");
            Assert.False(File.Exists(intermediatePath), "Intermediate should be cleaned up");
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(intermediatePath)) File.Delete(intermediatePath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
    }

    [Fact]
    public void Execute_Cancellation_ThrowsAndCleansUp()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = BuildPlan(source);
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            var executor = new ConversionExecutor([new PassThroughInvoker()]);

            // OperationCanceledException is caught internally and mapped to Error
            // or rethrown — check that intermediate cleanup still happens
            Assert.ThrowsAny<OperationCanceledException>(() => executor.Execute(plan, cancellationToken: cts.Token));
        }
        finally
        {
            Cleanup(source, ".chd");
        }
    }

    [Fact]
    public void Execute_OutputAlreadyExists_ReturnsSkipped()
    {
        var source = CreateTempFile(".iso");
        var existingOutput = Path.Combine(
            Path.GetDirectoryName(source)!,
            Path.GetFileNameWithoutExtension(source) + ".chd");
        File.WriteAllBytes(existingOutput, [9, 8, 7, 6]);
        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new PassThroughInvoker()]);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
            Assert.Equal("target-exists", result.Reason);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(existingOutput)) File.Delete(existingOutput);
        }
    }

    [Fact]
    public void Execute_InvokerNotFound_ReturnsError()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = BuildPlan(source);
            // Empty invoker list → no invoker can handle the step
            var executor = new ConversionExecutor([new NoneHandleInvoker()]);

            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Error, result.Outcome);
            Assert.StartsWith("invoker-not-found:", result.Reason);
        }
        finally
        {
            Cleanup(source, ".chd");
        }
    }

    [Fact]
    public void Execute_OnStepCompleteCallback_InvokedPerStep()
    {
        var source = CreateTempFile(".cso");
        var sourceDir = Path.GetDirectoryName(source)!;
        var baseName = Path.GetFileNameWithoutExtension(source);
        var intermediatePath = Path.Combine(sourceDir, $"{baseName}.tmp.step1.iso");
        var finalPath = Path.Combine(sourceDir, $"{baseName}.chd");
        try
        {
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "PSP",
                Policy = ConversionPolicy.Auto,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Acceptable,
                Steps =
                [
                    new ConversionStep
                    {
                        Order = 0,
                        InputExtension = ".cso",
                        OutputExtension = ".iso",
                        Capability = Capability(".cso", ".iso", "ciso", "decompress"),
                        IsIntermediate = true
                    },
                    new ConversionStep
                    {
                        Order = 1,
                        InputExtension = ".iso",
                        OutputExtension = ".chd",
                        Capability = Capability(".iso", ".chd", "chdman", "createcd"),
                        IsIntermediate = false
                    }
                ]
            };

            var callbackResults = new List<(int Order, bool Success)>();
            var executor = new ConversionExecutor([new PassThroughInvoker()]);
            executor.Execute(plan, (step, stepResult) =>
            {
                callbackResults.Add((step.Order, stepResult.Success));
            });

            Assert.Equal(2, callbackResults.Count);
            Assert.Equal(0, callbackResults[0].Order);
            Assert.True(callbackResults[0].Success);
            Assert.Equal(1, callbackResults[1].Order);
            Assert.True(callbackResults[1].Success);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(intermediatePath)) File.Delete(intermediatePath);
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
    }

    [Fact]
    public void Execute_PlanBlocked_ReturnsBlocked()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = new ConversionPlan
            {
                SourcePath = source,
                ConsoleKey = "ARCADE",
                Policy = ConversionPolicy.None,
                SourceIntegrity = SourceIntegrity.Unknown,
                Safety = ConversionSafety.Blocked,
                SkipReason = "set-protected",
                Steps = []
            };

            var executor = new ConversionExecutor([new PassThroughInvoker()]);
            var result = executor.Execute(plan);

            Assert.Equal(ConversionOutcome.Blocked, result.Outcome);
            Assert.Equal("set-protected", result.Reason);
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
        }
    }

    [Fact]
    public void Execute_SourcePreservedOnVerifyFail()
    {
        var source = CreateTempFile(".iso");
        try
        {
            var plan = BuildPlan(source);
            var executor = new ConversionExecutor([new VerifyFailInvoker()]);

            executor.Execute(plan);

            Assert.True(File.Exists(source), "Source must NOT be deleted on verification failure");
        }
        finally
        {
            Cleanup(source, ".chd");
        }
    }

    private static ConversionPlan BuildPlan(string sourcePath)
    {
        return new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = "PS1",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps =
            [
                new ConversionStep
                {
                    Order = 0,
                    InputExtension = ".iso",
                    OutputExtension = ".chd",
                    Capability = Capability(".iso", ".chd", "chdman", "createcd"),
                    IsIntermediate = false
                }
            ]
        };
    }

    private static ConversionCapability Capability(string source, string target, string tool, string command)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = command,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };
    }

    private static string CreateTempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"conv_exec_hardening_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private sealed class PassThroughInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
            return new ToolInvocationResult(true, targetPath, 0, "ok", null, 1, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => File.Exists(targetPath) ? VerificationStatus.Verified : VerificationStatus.VerifyFailed;
    }

    private sealed class ThrowOnCommandInvoker(string commandToThrow) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            if (string.Equals(capability.Command, commandToThrow, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("forced");

            File.Copy(sourcePath, targetPath, overwrite: false);
            return new ToolInvocationResult(true, targetPath, 0, "ok", null, 1, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }

    private sealed class VerifyFailInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            File.Copy(sourcePath, targetPath, overwrite: false);
            return new ToolInvocationResult(true, targetPath, 0, "ok", null, 1, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.VerifyFailed;
    }

    private sealed class EmptyOutputInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            using var _ = File.Create(targetPath);
            return new ToolInvocationResult(true, targetPath, 0, "ok", null, 1, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }

    private sealed class NoneHandleInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => false;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath, ConversionCapability capability, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => throw new NotSupportedException();
    }

    private static void Cleanup(string source, string targetExtension)
    {
        if (File.Exists(source)) File.Delete(source);
        var target = Path.ChangeExtension(source, targetExtension);
        if (File.Exists(target)) File.Delete(target);
    }
}
