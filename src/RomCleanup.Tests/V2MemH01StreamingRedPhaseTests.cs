using System.Reflection;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// RED phase for V2-MEM-H01: enforce streaming architecture seams before implementation.
/// These tests are intentionally expected to fail until streaming migration is implemented.
/// </summary>
public sealed class V2MemH01StreamingRedPhaseTests
{
    [Fact]
    public void Should_ExposeStreamingScanPhaseType_When_V2MemH01Implemented()
    {
        var type = Type.GetType("RomCleanup.Infrastructure.Orchestration.StreamingScanPipelinePhase, RomCleanup.Infrastructure");
        Assert.NotNull(type);
    }

    [Fact]
    public void Should_ExposeAsyncScannerPort_When_V2MemH01Implemented()
    {
        var type = Type.GetType("RomCleanup.Contracts.Ports.IAsyncFileScanner, RomCleanup.Contracts");
        Assert.NotNull(type);
    }

    [Fact]
    public void Should_DefineAsyncEnumerateMethod_OnAsyncScannerPort()
    {
        var type = Type.GetType("RomCleanup.Contracts.Ports.IAsyncFileScanner, RomCleanup.Contracts");
        Assert.NotNull(type);

        var method = type!.GetMethod("EnumerateFilesAsync", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Contains("IAsyncEnumerable", method!.ReturnType.FullName ?? method.ReturnType.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_UseAsyncEnumerableInRunOrchestrator_When_V2MemH01Implemented()
    {
        var file = Path.Combine(GetRepoRoot(), "src", "RomCleanup.Infrastructure", "Orchestration", "RunOrchestrator.cs");
        Assert.True(File.Exists(file));

        var content = File.ReadAllText(file);
        Assert.Contains("IAsyncEnumerable", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_RemoveFullCandidateMaterializationInRunOrchestrator_When_V2MemH01Implemented()
    {
        var file = Path.Combine(GetRepoRoot(), "src", "RomCleanup.Infrastructure", "Orchestration", "RunOrchestrator.cs");
        Assert.True(File.Exists(file));

        var content = File.ReadAllText(file);
        Assert.DoesNotContain("var candidates = enrichmentPhase.Execute", content, StringComparison.Ordinal);
    }

    private static string GetRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "RomCleanup.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        // Fallback: walk up from compile-time source path
        if (callerPath is not null)
        {
            dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "RomCleanup.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
