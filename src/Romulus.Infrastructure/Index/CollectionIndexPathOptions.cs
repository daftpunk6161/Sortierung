namespace Romulus.Infrastructure.Index;

/// <summary>
/// Infrastructure-local override for the persisted collection database path.
/// Callers may leave <see cref="DatabasePath"/> empty to use the default path policy.
/// </summary>
public sealed class CollectionIndexPathOptions
{
    public string? DatabasePath { get; init; }
}
