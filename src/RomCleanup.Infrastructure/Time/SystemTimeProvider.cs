using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Time;

/// <summary>
/// Production implementation of <see cref="ITimeProvider"/> using system clock.
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
