namespace RomCleanup.Core.Conversion;

using RomCleanup.Contracts.Models;

/// <summary>
/// Directed weighted graph over conversion format edges.
/// </summary>
public sealed class ConversionGraph(IReadOnlyList<ConversionCapability> capabilities)
{
    private readonly IReadOnlyList<ConversionCapability> _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));

    /// <summary>
    /// Finds the cheapest conversion path from source extension to target extension.
    /// </summary>
    public IReadOnlyList<ConversionCapability>? FindPath(
        string sourceExtension,
        string targetExtension,
        string consoleKey,
        Func<ConversionCondition, bool> conditionEvaluator,
        SourceIntegrity sourceIntegrity = SourceIntegrity.Unknown)
    {
        if (string.IsNullOrWhiteSpace(sourceExtension) || string.IsNullOrWhiteSpace(targetExtension))
            return null;

        if (string.Equals(sourceExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
            return [];

        var source = sourceExtension.Trim().ToLowerInvariant();
        var target = targetExtension.Trim().ToLowerInvariant();
        var queue = new PriorityQueue<(string Ext, int Depth), int>();
        var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [source] = 0
        };
        var previous = new Dictionary<string, (string PrevExt, ConversionCapability Edge)>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue((source, 0), 0);

        while (queue.TryDequeue(out var current, out var currentCost))
        {
            if (current.Depth > 5)
                continue;

            if (!distances.TryGetValue(current.Ext, out var knownCost) || currentCost > knownCost)
                continue;

            if (string.Equals(current.Ext, target, StringComparison.OrdinalIgnoreCase))
                break;

            foreach (var edge in GetOutgoingEdges(current.Ext, consoleKey, conditionEvaluator, sourceIntegrity))
            {
                var nextExt = edge.TargetExtension.ToLowerInvariant();
                var nextCost = currentCost + Math.Max(edge.Cost, 0);

                if (distances.TryGetValue(nextExt, out var best) && nextCost >= best)
                    continue;

                distances[nextExt] = nextCost;
                previous[nextExt] = (current.Ext, edge);
                queue.Enqueue((nextExt, current.Depth + 1), nextCost);
            }
        }

        if (!previous.ContainsKey(target))
            return null;

        var path = new List<ConversionCapability>();
        var cursor = target;
        while (!string.Equals(cursor, source, StringComparison.OrdinalIgnoreCase))
        {
            if (!previous.TryGetValue(cursor, out var prev))
                return null;

            path.Add(prev.Edge);
            cursor = prev.PrevExt;
        }

        path.Reverse();
        return path;
    }

    private IEnumerable<ConversionCapability> GetOutgoingEdges(
        string fromExtension,
        string consoleKey,
        Func<ConversionCondition, bool> conditionEvaluator,
        SourceIntegrity sourceIntegrity)
    {
        foreach (var capability in _capabilities)
        {
            if (!string.Equals(capability.SourceExtension, fromExtension, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(capability.SourceExtension, "*", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (capability.ApplicableConsoles is { Count: > 0 }
                && !capability.ApplicableConsoles.Contains(consoleKey))
            {
                continue;
            }

            if (capability.RequiredSourceIntegrity is not null && capability.RequiredSourceIntegrity != sourceIntegrity)
                continue;

            if (!conditionEvaluator(capability.Condition))
                continue;

            // TASK-056: Block Lossy→Lossy conversion paths — a lossy source must not be
            // fed through another lossy conversion step (e.g. CSO→WBFS, NKit→GCZ).
            if (sourceIntegrity == SourceIntegrity.Lossy && !capability.Lossless)
                continue;

            yield return capability;
        }
    }
}
