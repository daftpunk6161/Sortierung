using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-036: Conversion estimation, format priority, GPU hashing.</summary>
public interface IConversionEstimator
{
    ConversionEstimateResult GetConversionEstimate(IReadOnlyList<RomCandidate> candidates);
    string? GetTargetFormat(string ext);
    (int passed, int failed, int missing) VerifyConversions(IReadOnlyList<string> targetPaths, long minSize = 1);
    string FormatFormatPriority();
    string FormatEmulatorCompat();
    string BuildConvertQueueReport(ConversionEstimateResult est);
    string BuildNKitConvertReport(string filePath);
    (string Report, bool IsEnabled) BuildGpuHashingStatus();
    bool ToggleGpuHashing();
    string BuildConversionEstimateReport(IReadOnlyList<RomCandidate> candidates);
    string BuildParallelHashingReport(int cores, int newThreads);
}
