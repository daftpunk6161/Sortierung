using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Tests.Conversion.TestDoubles;

/// <summary>
/// Block D3 / R3 - centralized <see cref="IToolInvoker"/> test doubles for
/// conversion-failure-mode coverage (Crash, HashMismatch, Cancellation,
/// OutputTooSmall, DiskFull, Success, multi-step success-then-fail).
///
/// Replaces ad-hoc copy-pasted invokers previously inlined in
/// <c>SourcePreservationInvariantTests</c>. Each double models exactly one
/// failure surface with clearly named semantics so test files do not need to
/// re-implement the contract.
///
/// All doubles are deterministic and side-effect-free except for the explicit
/// file IO they perform on the target path.
/// </summary>
internal static class FakeToolInvokers
{
    /// <summary>Tool process exits non-zero (crash). Verify is never reached.</summary>
    public sealed class Crash(int exitCode = 1) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
            => new(false, null, exitCode, null, "tool-crashed", 5, VerificationStatus.NotAttempted);

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.NotAttempted;
    }

    /// <summary>Tool reports success but produces a zero-byte output file.</summary>
    public sealed class EmptyOutput : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(targetPath, []);
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.NotAttempted;
    }

    /// <summary>
    /// Tool produces an output file that <see cref="Verify"/> rejects with
    /// <see cref="VerificationStatus.VerifyFailed"/>. Models post-conversion
    /// hash/structure mismatch.
    /// </summary>
    public sealed class HashMismatch : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(targetPath, "PRODUCED-BUT-INVALID");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.VerifyFailed;
    }

    /// <summary>
    /// Cancels the supplied <see cref="CancellationTokenSource"/> when
    /// invoked with the configured tool name and rethrows
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public sealed class CancelOnTool(CancellationTokenSource cts, string toolNameToCancelOn) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            if (capability.Tool.ToolName.Equals(toolNameToCancelOn, StringComparison.OrdinalIgnoreCase))
            {
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            File.WriteAllText(targetPath, "step-output");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }

    /// <summary>
    /// Tool reports failure with disk-full-style stderr message; no output
    /// file is created.
    /// </summary>
    public sealed class DiskFull : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
            => new(false, null, -1, null, "There is not enough space on the disk.", 5, VerificationStatus.NotAttempted);

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.NotAttempted;
    }

    /// <summary>
    /// Successful invoker: writes a small payload to the target and reports
    /// <see cref="VerificationStatus.Verified"/>.
    /// </summary>
    public sealed class Success(string payload = "ok") : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(targetPath, payload);
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.Verified);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }

    /// <summary>
    /// Multi-step helper: tools whose name does NOT match
    /// <paramref name="failOnToolName"/> succeed (and write a step output);
    /// the matching tool reports failure with exit code 1.
    /// </summary>
    public sealed class SuccessThenFail(string failOnToolName) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            if (capability.Tool.ToolName.Equals(failOnToolName, StringComparison.OrdinalIgnoreCase))
                return new(false, null, 1, null, "step-2-failure", 5, VerificationStatus.NotAttempted);

            File.WriteAllText(targetPath, "step-output");
            return new(true, targetPath, 0, "ok", null, 5, VerificationStatus.Verified);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }
}
