namespace RomCleanup.Api;

internal static class ProgressEstimator
{
    public static int EstimateFromMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0;

        if (message.StartsWith("[Preflight]", StringComparison.OrdinalIgnoreCase)) return 5;
        if (message.StartsWith("[Scan]", StringComparison.OrdinalIgnoreCase)) return 20;
        if (message.StartsWith("[Filter]", StringComparison.OrdinalIgnoreCase)) return 30;
        if (message.StartsWith("[Dedupe]", StringComparison.OrdinalIgnoreCase)) return 45;
        if (message.StartsWith("[Junk]", StringComparison.OrdinalIgnoreCase)) return 60;
        if (message.StartsWith("[Move]", StringComparison.OrdinalIgnoreCase)) return 75;
        if (message.StartsWith("[Sort]", StringComparison.OrdinalIgnoreCase)) return 85;
        if (message.StartsWith("[Convert]", StringComparison.OrdinalIgnoreCase)) return 92;
        if (message.StartsWith("[Report]", StringComparison.OrdinalIgnoreCase)) return 97;

        return 0;
    }
}
