using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Block D1 - centralized test harness for <see cref="EnrichmentPipelinePhase"/>.
/// Replaces previously duplicated <c>CreateContext</c> + <c>FixedPolicyResolver</c>
/// helpers across multiple test files (CrossConsoleDatPolicyTests, EnrichmentPipelinePhaseAuditPhase3And4Tests,
/// AuditP0P1FixTests, DatRenamePipelinePhaseIssue9RedTests, ...).
///
/// Goal: enable family x policy-switch testing without reflection or copy-pasted
/// resolvers. Keep the harness as a thin convenience wrapper around real production
/// types. No shadow business logic.
/// </summary>
internal static class EnrichmentTestHarness
{
    /// <summary>
    /// Build a real <see cref="PipelineContext"/> with a fresh
    /// <see cref="PhaseMetricsCollector"/>, <see cref="FileSystemAdapter"/>
    /// and <see cref="AuditCsvStore"/>. Tests that need bespoke ports must
    /// build the context inline; this helper covers the &gt;90 % case.
    /// </summary>
    public static PipelineContext BuildContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = metrics
        };
    }

    /// <summary>
    /// Build a <see cref="PipelineContext"/> with custom ports
    /// (e.g. <see cref="TrackingAuditStore"/>, <see cref="InMemoryFileSystem"/>).
    /// </summary>
    public static PipelineContext BuildContext(
        RunOptions options,
        IFileSystem fileSystem,
        IAuditStore auditStore)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = fileSystem,
            AuditStore = auditStore,
            Metrics = metrics
        };
    }

    /// <summary>
    /// Convenience builder for the most common <see cref="RunOptions"/> shape
    /// used by enrichment phase tests (DryRun, single root, explicit extensions).
    /// </summary>
    public static RunOptions DryRunOptions(
        string root,
        string[] extensions,
        string hashType = "SHA1") => new()
        {
            Roots = [root],
            Extensions = extensions,
            Mode = "DryRun",
            HashType = hashType
        };
}

/// <summary>
/// Block D1 - centralized <see cref="IFamilyDatStrategyResolver"/> test double
/// that returns the same <see cref="FamilyDatPolicy"/> for every family/extension.
///
/// Use this in place of locally duplicated <c>FixedPolicyResolver</c> classes.
/// </summary>
internal sealed class FixedFamilyDatPolicyResolver(FamilyDatPolicy policy) : IFamilyDatStrategyResolver
{
    public FamilyDatPolicy ResolvePolicy(PlatformFamily family, string extension, string? hashStrategy)
        => policy;
}
