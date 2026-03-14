using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-036: Delegates to static FeatureService.Conversion methods.</summary>
public sealed class ConversionEstimator : IConversionEstimator
{
    public ConversionEstimateResult GetConversionEstimate(IReadOnlyList<RomCandidate> candidates)
        => FeatureService.GetConversionEstimate(candidates);

    public string? GetTargetFormat(string ext)
        => FeatureService.GetTargetFormat(ext);

    public (int passed, int failed, int missing) VerifyConversions(IReadOnlyList<string> targetPaths, long minSize = 1)
        => FeatureService.VerifyConversions(targetPaths, minSize);

    public string FormatFormatPriority()
        => FeatureService.FormatFormatPriority();

    public string FormatEmulatorCompat()
        => FeatureService.FormatEmulatorCompat();

    public string BuildConvertQueueReport(ConversionEstimateResult est)
        => FeatureService.BuildConvertQueueReport(est);

    public string BuildNKitConvertReport(string filePath)
        => FeatureService.BuildNKitConvertReport(filePath);

    public (string Report, bool IsEnabled) BuildGpuHashingStatus()
        => FeatureService.BuildGpuHashingStatus();

    public bool ToggleGpuHashing()
        => FeatureService.ToggleGpuHashing();

    public string BuildConversionEstimateReport(IReadOnlyList<RomCandidate> candidates)
        => FeatureService.BuildConversionEstimateReport(candidates);

    public string BuildParallelHashingReport(int cores, int newThreads)
        => FeatureService.BuildParallelHashingReport(cores, newThreads);
}
