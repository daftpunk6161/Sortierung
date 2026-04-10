namespace Romulus.Infrastructure.Orchestration;

public static class ReportPathResolver
{
    public static string? Resolve(string? actualReportPath, string? plannedReportPath)
    {
        if (!string.IsNullOrWhiteSpace(actualReportPath) && File.Exists(actualReportPath))
            return Path.GetFullPath(actualReportPath);

        if (!string.IsNullOrWhiteSpace(plannedReportPath) && File.Exists(plannedReportPath))
            return Path.GetFullPath(plannedReportPath);

        return null;
    }
}
