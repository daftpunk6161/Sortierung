using System.Collections.Generic;
using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Wave 4 — T-W4-TELEMETRY-OPT-IN. Read/write port for the local
/// opt-in telemetry sink. DSGVO-konformes Opt-in: <see cref="IsEnabled"/>
/// MUST default to <c>false</c>; the user must explicitly call
/// <see cref="Enable"/>. Adapters must persist the toggle so it
/// survives application restarts.
///
/// <para>
/// <strong>Privacy contract:</strong> any field passed to
/// <see cref="Record(string, IReadOnlyDictionary{string, object?})"/> that is
/// not on <c>TelemetryEventAllowList.AllowedFields</c> MUST be silently
/// stripped. Paths, hostnames and usernames are explicitly forbidden.
/// </para>
/// </summary>
public interface ITelemetryService
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();

    /// <summary>
    /// Records an aggregate event after allow-list filtering. Returns
    /// <c>true</c> when telemetry is enabled and the event is captured.
    /// Returns <c>false</c> as a no-op when telemetry is disabled.
    /// </summary>
    bool Record(string eventName, IReadOnlyDictionary<string, object?> fields);

    /// <summary>In-memory ring of the most recent captured events.</summary>
    IReadOnlyList<TelemetryEvent> RecentEvents { get; }
}
