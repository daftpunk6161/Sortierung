using RomCleanup.Contracts.Models;

namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Abstraction for tool-specific conversion invocation.
/// </summary>
public interface IToolInvoker
{
    /// <summary>Returns true when this invoker can execute the capability.</summary>
    bool CanHandle(ConversionCapability capability);

    /// <summary>Executes one conversion capability edge.</summary>
    ToolInvocationResult Invoke(
        string sourcePath,
        string targetPath,
        ConversionCapability capability,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies a generated target artifact.</summary>
    VerificationStatus Verify(string targetPath, ConversionCapability capability);
}
