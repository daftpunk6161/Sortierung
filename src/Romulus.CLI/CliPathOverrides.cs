namespace Romulus.CLI;

/// <summary>
/// Test-isolation overrides for persistent CLI paths.
///
/// <para>
/// Set via <see cref="Program.SetTestPathOverrides"/> in test code so that
/// <see cref="Program.CreateCliServiceProvider"/> avoids touching the real
/// user's <c>%APPDATA%\Romulus\</c> footprint:
/// <list type="bullet">
///   <item><description><see cref="CollectionDbPath"/> overrides <c>%APPDATA%\Romulus\collection.db</c> (LiteDB exclusive lock).</description></item>
///   <item><description><see cref="AuditSigningKeyPath"/> overrides <c>%APPDATA%\Romulus\security\audit-signing.key</c>.</description></item>
///   <item><description><see cref="ProvenanceRootPath"/> overrides <c>%APPDATA%\Romulus\provenance</c>.</description></item>
///   <item><description><see cref="DatCatalogStatePath"/> overrides <c>%APPDATA%\Romulus\dat-catalog-state.json</c>.</description></item>
/// </list>
/// All properties are optional; <c>null</c> falls back to the real default
/// resolver. The record itself is scoped via <see cref="System.Threading.AsyncLocal{T}"/>
/// so parallel test fixtures cannot leak overrides between each other.
/// </para>
///
/// <para>
/// Production code (the real <c>Main</c> entry point) never sets this
/// override, so behaviour for end users is unchanged.
/// </para>
/// </summary>
public sealed record CliPathOverrides
{
    public string? CollectionDbPath { get; init; }
    public string? AuditSigningKeyPath { get; init; }
    public string? ProvenanceRootPath { get; init; }
    public string? DatCatalogStatePath { get; init; }
}
