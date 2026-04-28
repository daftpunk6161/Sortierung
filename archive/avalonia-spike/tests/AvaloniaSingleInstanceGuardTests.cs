using Romulus.UI.Avalonia.Runtime;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaSingleInstanceGuardTests
{
    [Fact]
    public void Acquire_SecondAcquireFailsUntilFirstReleased()
    {
        var mutexName = $"Global\\Romulus_Avalonia_Test_{Guid.NewGuid():N}";

        using var first = SingleInstanceGuard.Acquire(mutexName);
        Assert.True(first.IsAcquired);

        using var second = SingleInstanceGuard.Acquire(mutexName);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public void Acquire_ReleasesMutexOnDispose()
    {
        var mutexName = $"Global\\Romulus_Avalonia_Test_{Guid.NewGuid():N}";

        using (var first = SingleInstanceGuard.Acquire(mutexName))
        {
            Assert.True(first.IsAcquired);
        }

        using var second = SingleInstanceGuard.Acquire(mutexName);
        Assert.True(second.IsAcquired);
    }
}
