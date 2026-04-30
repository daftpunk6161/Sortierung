using System;
using System.Collections.Generic;

namespace Romulus.Contracts.Models;

/// <summary>
/// Wave 4 — T-W4-TELEMETRY-OPT-IN. Aggregate-only telemetry event.
/// Field set is filtered through <c>TelemetryEventAllowList</c>; PII
/// (paths, hostnames, usernames) is stripped before persistence.
/// </summary>
public sealed record TelemetryEvent(
    string Name,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, object?> Fields);
