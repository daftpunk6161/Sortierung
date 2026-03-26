using System.Text;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// Shared CSV line parser used by AuditCsvStore and AuditSigningService.
/// Handles RFC 4180 quoting (double-quotes escaped as "").
/// </summary>
internal static class AuditCsvParser
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
    /// Prefixes dangerous leading characters with single quote, but allows plain negative numbers.
    /// </summary>
    public static string SanitizeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var hadDangerousControlPrefix = value[0] is '\r' or '\n' or '\t';

        // Normalize control characters to keep each CSV field on one logical line.
        value = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", " ");

        if (hadDangerousControlPrefix || value[0] is '=' or '+' or '@')
            value = "'" + value;
        else if (value[0] == '-' && !IsPlainNegativeNumber(value))
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
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
}
