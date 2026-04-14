using System.Text;

namespace Romulus.Infrastructure.Audit;

/// <summary>
/// Shared CSV line parser used by AuditCsvStore and AuditSigningService.
/// Handles RFC 4180 quoting (double-quotes escaped as "").
/// </summary>
public static class AuditCsvParser
{
    public static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());

        return fields.ToArray();
    }

    /// <summary>
    /// Sanitize a CSV field to prevent CSV injection (OWASP).
    /// Dangerous formula-like prefixes are always emitted as RFC-4180 quoted fields.
    /// </summary>
    public static string SanitizeCsvField(string value)
        => SanitizeCsvField(value, ',');

    /// <summary>
    /// Apply spreadsheet-safe CSV sanitization with a consistent formula-injection policy.
    /// Dangerous leading spreadsheet operators are prefixed before RFC-4180 sanitization.
    /// </summary>
    public static string SanitizeSpreadsheetCsvField(string value)
        => SanitizeSpreadsheetCsvField(value, ',');

    /// <summary>
    /// Apply spreadsheet-safe CSV sanitization with a consistent formula-injection policy.
    /// Dangerous leading spreadsheet operators are prefixed before RFC-4180 sanitization.
    /// </summary>
    public static string SanitizeSpreadsheetCsvField(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
            return SanitizeCsvField(value, delimiter);

        var valueForCsv = HasDangerousSpreadsheetPrefix(value)
            ? "'" + value
            : value;

        return SanitizeCsvField(valueForCsv, delimiter);
    }

    /// <summary>
    /// Legacy WPF-compatible CSV sanitization policy kept for backwards-compatible UI exports.
    /// Consolidated here to avoid duplicate CSV logic across entry points.
    /// </summary>
    public static string SanitizeLegacyUiCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value[0] is '=' or '+' or '@' or '\t' or '\r')
            value = "'" + value;
        else if (value[0] == '-' && !IsPlainNegativeNumber(value))
            value = "'" + value;

        if (value.Contains('"') || value.Contains(';') || value.Contains(','))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    /// <summary>
    /// Dat-audit CSV export compatibility sanitization.
    /// Keeps historical behavior (always protects leading '-' and emits "" for empty fields).
    /// </summary>
    public static string SanitizeDatAuditCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var sanitized = value;
        if (sanitized.Length > 0 && sanitized[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            sanitized = "'" + sanitized;

        if (sanitized.Contains('"') || sanitized.Contains(',') || sanitized.Contains('\n'))
            return "\"" + sanitized.Replace("\"", "\"\"") + "\"";

        return sanitized;
    }

    /// <summary>
    /// Sanitize a delimited text field to prevent CSV injection (OWASP).
    /// Dangerous formula-like prefixes are always emitted as RFC-4180 quoted fields.
    /// </summary>
    public static string SanitizeCsvField(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var hadDangerousControlPrefix = value[0] is '\r' or '\n' or '\t';

        // Normalize control characters to keep each CSV field on one logical line.
        value = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", " ");

        var hasDangerousFormulaPrefix = hadDangerousControlPrefix
            || value[0] is '=' or '+' or '@'
            || (value[0] == '-' && !IsPlainNegativeNumber(value));

        if (hasDangerousFormulaPrefix)
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        if (value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private static bool IsPlainNegativeNumber(string value)
    {
        if (value.Length < 2 || value[0] != '-') return false;
        for (int i = 1; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]) && value[i] != '.')
                return false;
        }
        return true;
    }

    // R5-021 FIX: Include tab (\t) and carriage return (\r) — both are dangerous
    // spreadsheet prefixes that can trigger formula execution in Excel/Calc.
    private static bool HasDangerousSpreadsheetPrefix(string value)
        => !string.IsNullOrEmpty(value)
            && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
}
