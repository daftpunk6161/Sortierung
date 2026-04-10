using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Local persistence port for user-defined run profiles.
/// Built-in profiles are loaded separately from the data directory.
/// </summary>
public interface IRunProfileStore
{
    ValueTask<IReadOnlyList<RunProfileDocument>> ListAsync(CancellationToken ct = default);

    ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default);

    ValueTask UpsertAsync(RunProfileDocument profile, CancellationToken ct = default);

    ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default);
}
