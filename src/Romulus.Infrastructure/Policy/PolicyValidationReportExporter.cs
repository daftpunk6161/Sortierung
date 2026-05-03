using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;

namespace Romulus.Infrastructure.Policy;

public static class PolicyValidationReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(PolicyValidationReport report)
        => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToCsv(PolicyValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("PolicyId,PolicyName,PolicyFingerprint,RuleId,Severity,ConsoleKey,GameKey,Region,Extension,Expected,Actual,Path,Message");
        foreach (var violation in report.Violations)
        {
            sb.AppendJoin(',', new[]
            {
                Csv(report.PolicyId),
                Csv(report.PolicyName),
                Csv(report.PolicyFingerprint),
                Csv(violation.RuleId),
                Csv(violation.Severity),
                Csv(violation.ConsoleKey),
                Csv(violation.GameKey),
                Csv(violation.Region),
                Csv(violation.Extension),
                Csv(violation.Expected),
                Csv(violation.Actual),
                Csv(violation.Path),
                Csv(violation.Message)
            });
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
        => AuditCsvParser.SanitizeSpreadsheetCsvField(value ?? "");
}
