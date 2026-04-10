using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Orchestration;

public interface IRunOptionsSource
{
    IReadOnlyList<string> Roots { get; }
    string Mode { get; }
    string[] PreferRegions { get; }
    IReadOnlyList<string> Extensions { get; }
    bool RemoveJunk { get; }
    bool OnlyGames { get; }
    bool KeepUnknownWhenOnlyGames { get; }
    bool AggressiveJunk { get; }
    bool SortConsole { get; }
    bool EnableDat { get; }
    bool EnableDatAudit { get; }
    bool EnableDatRename { get; }
    string? DatRoot { get; }
    string HashType { get; }
    string? ConvertFormat { get; }
    bool ConvertOnly { get; }
    bool ApproveReviews { get; }
    bool ApproveConversionReview { get; }
    string? TrashRoot { get; }
    string ConflictPolicy { get; }
}

public interface IRunOptionsFactory
{
    RunOptions Create(IRunOptionsSource source, string? auditPath, string? reportPath);
}

public sealed class RunOptionsFactory : IRunOptionsFactory
{
    public RunOptions Create(IRunOptionsSource source, string? auditPath, string? reportPath)
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = new RunOptions
        {
            Roots = source.Roots,
            Mode = source.Mode,
            PreferRegions = source.PreferRegions,
            Extensions = source.Extensions,
            RemoveJunk = source.RemoveJunk,
            OnlyGames = source.OnlyGames,
            KeepUnknownWhenOnlyGames = source.KeepUnknownWhenOnlyGames,
            AggressiveJunk = source.AggressiveJunk,
            SortConsole = source.SortConsole,
            EnableDat = source.EnableDat,
            EnableDatAudit = source.EnableDatAudit,
            EnableDatRename = source.EnableDatRename,
            DatRoot = source.DatRoot,
            HashType = source.HashType,
            ConvertFormat = source.ConvertFormat,
            ConvertOnly = source.ConvertOnly,
            ApproveReviews = source.ApproveReviews,
            ApproveConversionReview = source.ApproveConversionReview,
            TrashRoot = source.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath,
            ConflictPolicy = source.ConflictPolicy
        };

        var normalized = RunOptionsBuilder.Normalize(options);
        var validationErrors = RunOptionsBuilder.Validate(normalized);
        if (validationErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", validationErrors));

        return normalized;
    }
}
