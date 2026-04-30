using System.Collections.Generic;

namespace Romulus.Infrastructure.Telemetry;

/// <summary>
/// Wave 4 — T-W4-TELEMETRY-OPT-IN. Closed allow-list of telemetry field
/// names. Adding a field requires updating this list AND the ADR. The
/// list intentionally excludes anything that could carry PII:
/// no <c>path</c>, no <c>host</c>, no <c>user</c>, no <c>ip</c>.
/// </summary>
public static class TelemetryEventAllowList
{
    public static IReadOnlySet<string> AllowedFields { get; } = new HashSet<string>(System.StringComparer.Ordinal)
    {
        "count",
        "durationMs",
        "consoleKey",   // canonical short key (e.g. "NES"); never a path
        "winnerCount",
        "loserCount",
        "groupCount",
        "phaseName",
        "outcome",      // "Move" | "Skip" | "Convert" | "Error"
        "exitCode",
        "version",
        "feature",
    };
}
