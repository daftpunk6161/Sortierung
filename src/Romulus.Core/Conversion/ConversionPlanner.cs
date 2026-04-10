namespace Romulus.Core.Conversion;

using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

/// <summary>
/// Computes conversion plans from configured capabilities and policies.
/// </summary>
public sealed class ConversionPlanner(
    IConversionRegistry registry,
    Func<string, string?> toolFinder,
    Func<string, long> fileSizeProvider,
    Func<string, bool>? encryptedPbpDetector = null) : IConversionPlanner
{
    private readonly IConversionRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly Func<string, string?> _toolFinder = toolFinder ?? throw new ArgumentNullException(nameof(toolFinder));
    private readonly Func<string, long> _fileSizeProvider = fileSizeProvider ?? throw new ArgumentNullException(nameof(fileSizeProvider));
    private readonly Func<string, bool>? _encryptedPbpDetector = encryptedPbpDetector;
    private readonly ConversionPolicyEvaluator _policyEvaluator = new();

    /// <inheritdoc />
    public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
    {
        var normalizedConsole = (consoleKey ?? string.Empty).Trim();
        var normalizedExt = (sourceExtension ?? string.Empty).Trim().ToLowerInvariant();
        var sourceIntegrity = SourceIntegrityClassifier.Classify(normalizedExt, Path.GetFileName(sourcePath));

        if (string.IsNullOrWhiteSpace(normalizedConsole) || string.Equals(normalizedConsole, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return BlockedPlan(sourcePath, normalizedConsole, sourceIntegrity, "unknown-console");
        }

        var configuredPolicy = _registry.GetPolicy(normalizedConsole);
        var policy = _policyEvaluator.GetEffectivePolicy(normalizedConsole, configuredPolicy);

        if (policy == ConversionPolicy.None)
            return BlockedPlan(sourcePath, normalizedConsole, sourceIntegrity, $"policy-none:{normalizedConsole}");

        var preferredTarget = _registry.GetPreferredTarget(normalizedConsole);
        if (string.IsNullOrWhiteSpace(preferredTarget))
            return BlockedPlan(sourcePath, normalizedConsole, sourceIntegrity, $"no-target-defined:{normalizedConsole}");

        if (string.Equals(normalizedExt, preferredTarget, StringComparison.OrdinalIgnoreCase))
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = normalizedConsole,
                Policy = policy,
                SourceIntegrity = sourceIntegrity,
                Safety = ConversionSafety.Safe,
                Steps = Array.Empty<ConversionStep>(),
                SkipReason = "already-target-format"
            };
        }

        var conditionEvaluator = new ConversionConditionEvaluator(_fileSizeProvider, _encryptedPbpDetector);
        var graph = new ConversionGraph(_registry.GetCapabilities());
        var capabilities = graph.FindPath(
            normalizedExt,
            preferredTarget,
            normalizedConsole,
            c => conditionEvaluator.Evaluate(c, sourcePath),
            sourceIntegrity);

        if (capabilities is null)
        {
            foreach (var alt in _registry.GetAlternativeTargets(normalizedConsole))
            {
                capabilities = graph.FindPath(
                    normalizedExt,
                    alt,
                    normalizedConsole,
                    c => conditionEvaluator.Evaluate(c, sourcePath),
                    sourceIntegrity);

                if (capabilities is not null)
                    break;
            }
        }

        if (capabilities is null)
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = normalizedConsole,
                Policy = policy,
                SourceIntegrity = sourceIntegrity,
                Safety = ConversionSafety.Blocked,
                Steps = Array.Empty<ConversionStep>(),
                SkipReason = "no-conversion-path"
            };
        }

        var missingTool = capabilities
            .Select(c => c.Tool.ToolName)
            .FirstOrDefault(t => string.IsNullOrWhiteSpace(_toolFinder(t)));

        var allToolsAvailable = string.IsNullOrWhiteSpace(missingTool);
        var safety = _policyEvaluator.EvaluateSafety(policy, sourceIntegrity, capabilities, allToolsAvailable);

        if (!allToolsAvailable)
        {
            return new ConversionPlan
            {
                SourcePath = sourcePath,
                ConsoleKey = normalizedConsole,
                Policy = policy,
                SourceIntegrity = sourceIntegrity,
                Safety = safety,
                Steps = Array.Empty<ConversionStep>(),
                SkipReason = $"tool-not-found:{missingTool}"
            };
        }

        var steps = capabilities
            .Select((cap, index) => new ConversionStep
            {
                Order = index,
                InputExtension = cap.SourceExtension,
                OutputExtension = cap.TargetExtension,
                Capability = cap,
                IsIntermediate = index < capabilities.Count - 1
            })
            .ToArray();

        return new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = normalizedConsole,
            Policy = policy,
            SourceIntegrity = sourceIntegrity,
            Safety = safety,
            Steps = steps,
            SkipReason = null
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ConversionPlan> PlanBatch(
        IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
    {
        if (candidates is null || candidates.Count == 0)
            return Array.Empty<ConversionPlan>();

        var plans = new List<ConversionPlan>(candidates.Count);
        foreach (var candidate in candidates)
            plans.Add(Plan(candidate.Path, candidate.ConsoleKey, candidate.Extension));

        return plans;
    }

    private static ConversionPlan BlockedPlan(
        string sourcePath,
        string consoleKey,
        SourceIntegrity sourceIntegrity,
        string reason)
    {
        return new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = consoleKey,
            Policy = ConversionPolicy.None,
            SourceIntegrity = sourceIntegrity,
            Safety = ConversionSafety.Blocked,
            Steps = Array.Empty<ConversionStep>(),
            SkipReason = reason
        };
    }
}
