using Romulus.Contracts;

namespace Romulus.Api;

internal static class ProgressEstimator
{
    public static int EstimateFromMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0;

        foreach (var (prefix, percent) in PipelinePhaseWeights.Phases)
        {
            if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return percent;
        }

        return 0;
    }
}
