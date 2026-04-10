using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Watch;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ SORT TEMPLATES ═════════════════════════════════════════════════
    // Port of SortTemplates.ps1

    public static Dictionary<string, string> GetSortTemplates()
    {
        var ext = UiLookupData.Instance.SortTemplates;
        if (ext.Count > 0) return new(ext);
        return new()
        {
            ["RetroArch"] = "{console}/{filename}",
            ["EmulationStation"] = "roms/{console_lower}/{filename}",
            ["LaunchBox"] = "Games/{console}/{filename}",
            ["Batocera"] = "share/roms/{console_lower}/{filename}",
            ["Flat"] = "{filename}"
        };
    }


    // ═══ CRON TESTER ═════════════════════════════════════════════════════
    // Port of CronTester (formerly SchedulerAdvanced.ps1)

    public static bool TestCronMatch(string cronExpression, DateTime dt)
        => CronScheduleEvaluator.TestCronMatch(cronExpression, dt);


    internal static bool CronFieldMatch(string field, int value)
        => CronScheduleEvaluator.CronFieldMatch(field, value);


    // ═══ CSV FIELD EXTRACTION ══════════════════════════════════════════

    /// <summary>
    /// Extract the first field from a CSV line, auto-detecting delimiter (semicolon or comma).
    /// Handles RFC 4180 quoted fields.
    /// </summary>
    internal static string ExtractFirstCsvField(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        if (line[0] == '"')
        {
            // Quoted field — find closing quote
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    { i++; continue; } // escaped quote
                    return line[1..i].Replace("\"\"", "\"");
                }
            }
            return line[1..].Replace("\"\"", "\"");
        }
        // Unquoted — split on first semicolon or comma
        var idxSemi = line.IndexOf(';');
        var idxComma = line.IndexOf(',');
        int idx;
        if (idxSemi < 0) idx = idxComma;
        else if (idxComma < 0) idx = idxSemi;
        else idx = Math.Min(idxSemi, idxComma);
        return idx >= 0 ? line[..idx] : line;
    }

}
