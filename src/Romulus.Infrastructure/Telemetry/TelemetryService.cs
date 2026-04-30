using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Telemetry;

/// <summary>
/// Wave 4 — T-W4-TELEMETRY-OPT-IN. Local opt-in telemetry sink.
/// <para>
/// <strong>Default off:</strong> <c>DefaultEnabled = false</c>; the
/// constructor never auto-enables. Persisted in a small JSON file
/// (default location: <c>%APPDATA%/Romulus/telemetry.json</c>).
/// </para>
/// <para>
/// <strong>No network in this iteration:</strong> events are kept in an
/// in-memory ring of the last <see cref="MaxRecentEvents"/> entries so
/// the GUI/CLI can show the user what would be sent. The ADR
/// (<c>docs/adrs/ADR-0030-telemetry-opt-in.md</c>) documents the future
/// endpoint and retention policy.
/// </para>
/// <para>
/// <strong>Allow-list:</strong> any field outside
/// <see cref="TelemetryEventAllowList.AllowedFields"/> is silently dropped
/// before the event is captured. This blocks the
/// "Pfade oder Hostnames leaken" failure mode at the recording boundary.
/// </para>
/// </summary>
public sealed class TelemetryService : ITelemetryService
{
    public const bool DefaultEnabled = false;
    public const int MaxRecentEvents = 200;

    private readonly string _statePath;
    private readonly object _lock = new();
    private readonly LinkedList<TelemetryEvent> _recent = new();
    private bool _enabled;

    public TelemetryService(string? statePath = null)
    {
        _statePath = statePath ?? DefaultStatePath();
        _enabled = TryLoadEnabled() ?? DefaultEnabled;
    }

    public bool IsEnabled
    {
        get { lock (_lock) return _enabled; }
    }

    public void Enable()
    {
        lock (_lock)
        {
            _enabled = true;
            PersistLocked();
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            _enabled = false;
            PersistLocked();
        }
    }

    public IReadOnlyList<TelemetryEvent> RecentEvents
    {
        get
        {
            lock (_lock)
            {
                return _recent.ToList();
            }
        }
    }

    public bool Record(string eventName, IReadOnlyDictionary<string, object?> fields)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("eventName must not be empty.", nameof(eventName));
        ArgumentNullException.ThrowIfNull(fields);

        lock (_lock)
        {
            if (!_enabled)
                return false;

            var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in fields)
            {
                if (TelemetryEventAllowList.AllowedFields.Contains(kv.Key))
                    filtered[kv.Key] = kv.Value;
            }

            var ev = new TelemetryEvent(eventName, DateTimeOffset.UtcNow, filtered);
            _recent.AddLast(ev);
            while (_recent.Count > MaxRecentEvents)
                _recent.RemoveFirst();
            return true;
        }
    }

    private static string DefaultStatePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Romulus", "telemetry.json");
    }

    private bool? TryLoadEnabled()
    {
        try
        {
            if (!File.Exists(_statePath))
                return null;
            using var stream = File.OpenRead(_statePath);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("enabled", out var prop)
                && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }
            return null;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void PersistLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new { enabled = _enabled });
            File.WriteAllText(_statePath, json);
        }
        catch (IOException) { /* best-effort persistence */ }
        catch (UnauthorizedAccessException) { /* best-effort persistence */ }
    }
}
