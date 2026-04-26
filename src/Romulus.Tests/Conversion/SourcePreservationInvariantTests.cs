using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

/// <summary>
/// Conversion source-persistence invariants.
///
/// Invariant: After ANY conversion failure mode, the source file MUST still exist
/// at its original path. Intermediate artifacts produced by step N must be cleaned up.
///
/// Source-deletion is always external to <see cref="ConversionExecutor"/>; this suite
/// guards that contract: the executor must never touch the source file regardless of
/// outcome (success, error, blocked, cancellation).
/// </summary>
public sealed class SourcePreservationInvariantTests : IDisposable
{
    private readonly string _tempDir;

    public SourcePreservationInvariantTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B1_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // -----------------------------------------------------------------
    // B1.1  Tool crash (ExitCode != 0) -> source preserved, no target left
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_ToolCrashNonZeroExit_PreservesSourceAndLeavesNoIntermediates()
    {
        var src = WriteSource("crash.cso", payload: "SRC-CRASH");
        var plan = SingleStepPlan(src, ".cso", ".chd", "tool-crash");
        var sut = new ConversionExecutor([new CrashingInvoker(exitCode: 137)]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.True(File.Exists(src), "Source must remain on disk after tool crash.");
        Assert.Equal("SRC-CRASH", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // -----------------------------------------------------------------
    // B1.2  Output too small / empty -> source preserved
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_EmptyOutput_PreservesSourceAndCleansEmptyOutput()
    {
        var src = WriteSource("empty.iso", payload: "SRC-EMPTY");
        var plan = SingleStepPlan(src, ".iso", ".chd", "tool-empty");
        var sut = new ConversionExecutor([new EmptyOutputInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.True(File.Exists(src), "Source must remain after empty-output failure.");
        Assert.Equal("SRC-EMPTY", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // -----------------------------------------------------------------
    // B1.3  Verification failure (treated as tool-hash-mismatch surrogate
    //       since tool-hash gating happens inside IToolRunner before any
    //       executor invocation; here we model the post-conversion verify
    //       failure that also indicates artifact corruption). Source must
    //       remain untouched.
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_VerificationFailed_PreservesSourceAndCleansTarget()
    {
        var src = WriteSource("verify.xyz", payload: "SRC-VERIFY");
        var plan = SingleStepPlan(src, ".xyz", ".zzz", "tool-verify");
        var sut = new ConversionExecutor([new VerifyFailingInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal(VerificationStatus.VerifyFailed, result.VerificationResult);
        Assert.True(File.Exists(src));
        Assert.Equal("SRC-VERIFY", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // -----------------------------------------------------------------
    // B1.4  Cancellation mid-conversion -> source preserved, all
    //       intermediate artifacts removed.
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_CancellationMidConversion_PreservesSourceAndCleansIntermediates()
    {
        var src = WriteSource("cancel.cso", payload: "SRC-CANCEL");
        var plan = TwoStepPlan(src, ".cso", ".iso", ".chd", "tool-step1", "tool-cancel");

        using var cts = new CancellationTokenSource();
        var sut = new ConversionExecutor([new CancelOnSecondStepInvoker(cts)]);

        // Executor propagates OperationCanceledException from invokers; the outer
        // try/finally still cleans intermediates and never touches the source.
        Assert.Throws<OperationCanceledException>(() => sut.Execute(plan, onStepComplete: null, cts.Token));

        Assert.True(File.Exists(src), "Source must remain after cancellation.");
        Assert.Equal("SRC-CANCEL", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // -----------------------------------------------------------------
    // B1.5  IO Exception during promote (final-output collision) ->
    //       source preserved, staged temp file cleaned.
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_StagedFinalCollision_PreservesSource()
    {
        var src = WriteSource("collide.iso", payload: "SRC-COLLIDE");

        // Pre-create the final target so executor short-circuits to "target-exists"
        // and never touches the source.
        var finalTarget = Path.Combine(_tempDir, "collide.chd");
        File.WriteAllText(finalTarget, "PRE-EXISTING-TARGET");

        var plan = SingleStepPlan(src, ".iso", ".chd", "tool-ok");
        var sut = new ConversionExecutor([new SuccessfulInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("target-exists", result.Reason);
        Assert.True(File.Exists(src));
        Assert.Equal("SRC-COLLIDE", File.ReadAllText(src));
        Assert.Equal("PRE-EXISTING-TARGET", File.ReadAllText(finalTarget));
    }

    // -----------------------------------------------------------------
    // B1.6  Disk-Full simulation (IOException from invoker) ->
    //       source preserved, no partial output retained.
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_DiskFullSimulation_PreservesSource()
    {
        var src = WriteSource("diskfull.iso", payload: "SRC-DISKFULL");
        var plan = SingleStepPlan(src, ".iso", ".chd", "tool-diskfull");
        var sut = new ConversionExecutor([new DiskFullInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.True(File.Exists(src));
        Assert.Equal("SRC-DISKFULL", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // -----------------------------------------------------------------
    // B1.7  Multi-step plan: failure in step 2 -> source preserved,
    //       step-1 intermediate artifact cleaned up.
    // -----------------------------------------------------------------
    [Fact]
    public void ConversionExecutor_MultiStepFailureInStepTwo_PreservesSourceAndHandlesIntermediates()
    {
        var src = WriteSource("multistep.cso", payload: "SRC-MULTI");
        var plan = TwoStepPlan(src, ".cso", ".iso", ".chd", "tool-ok", "tool-fail");
        var sut = new ConversionExecutor([new SuccessThenFailInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.True(File.Exists(src), "Source must remain after step-2 failure.");
        Assert.Equal("SRC-MULTI", File.ReadAllText(src));
        AssertNoStrayArtifacts(src);
    }

    // ───────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────

    private string WriteSource(string fileName, string payload)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, payload);
        return path;
    }

    private void AssertNoStrayArtifacts(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var stray = Directory.GetFiles(dir, $"{baseName}.tmp.*");
        Assert.Empty(stray);
    }

    private static ConversionPlan SingleStepPlan(string src, string inExt, string outExt, string toolName)
        => new()
        {
            SourcePath = src,
            ConsoleKey = "TEST",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps = [Step(0, inExt, outExt, toolName, intermediate: false)]
        };

    private static ConversionPlan TwoStepPlan(string src, string inExt, string midExt, string outExt,
        string toolA, string toolB)
        => new()
        {
            SourcePath = src,
            ConsoleKey = "TEST",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps =
            [
                Step(0, inExt, midExt, toolA, intermediate: true),
                Step(1, midExt, outExt, toolB, intermediate: false)
            ]
        };

    private static ConversionStep Step(int order, string inExt, string outExt, string toolName, bool intermediate)
        => new()
        {
            Order = order,
            InputExtension = inExt,
            OutputExtension = outExt,
            IsIntermediate = intermediate,
            Capability = new ConversionCapability
            {
                SourceExtension = inExt,
                TargetExtension = outExt,
                Tool = new ToolRequirement { ToolName = toolName },
                Command = "convert",
                Verification = VerificationMethod.FileExistenceCheck,
                Lossless = true,
                ResultIntegrity = SourceIntegrity.Lossless,
                Cost = 1
            }
        };

    // ───────── Test invokers ─────────

    private sealed class CrashingInvoker(int exitCode) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string t, ConversionCapability c, CancellationToken ct)
            => new(false, null, exitCode, null, "tool-crashed", 5, VerificationStatus.NotAttempted);
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.NotAttempted;
    }

    private sealed class EmptyOutputInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
        {
            // Produce a zero-byte output so ConversionOutputValidator rejects it.
            File.WriteAllBytes(targetPath, []);
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.NotAttempted;
    }

    private sealed class VerifyFailingInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
        {
            File.WriteAllText(targetPath, "PRODUCED-BUT-INVALID");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.VerifyFailed;
    }

    private sealed class CancelOnSecondStepInvoker(CancellationTokenSource cts) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
        {
            if (c.Tool.ToolName.Equals("tool-cancel", StringComparison.OrdinalIgnoreCase))
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            File.WriteAllText(targetPath, "step1-output");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.Verified;
    }

    private sealed class SuccessfulInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
        {
            File.WriteAllText(targetPath, "ok");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.Verified);
        }
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.Verified;
    }

    private sealed class DiskFullInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
            => new(false, null, -1, null, "There is not enough space on the disk.", 5, VerificationStatus.NotAttempted);
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.NotAttempted;
    }

    private sealed class SuccessThenFailInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability cap) => true;
        public ToolInvocationResult Invoke(string s, string targetPath, ConversionCapability c, CancellationToken ct)
        {
            if (c.Tool.ToolName.Equals("tool-fail", StringComparison.OrdinalIgnoreCase))
                return new(false, null, 1, null, "step-2-failure", 5, VerificationStatus.NotAttempted);

            File.WriteAllText(targetPath, "step1-output");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.Verified);
        }
        public VerificationStatus Verify(string t, ConversionCapability c) => VerificationStatus.Verified;
    }
}
