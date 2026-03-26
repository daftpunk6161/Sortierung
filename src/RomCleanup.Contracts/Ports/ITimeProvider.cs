namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for time abstraction.
/// Allows testable time-dependent logic in orchestration and API.
/// </summary>
public interface ITimeProvider
{
    DateTimeOffset UtcNow { get; }
}
