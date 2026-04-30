using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Telemetry;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 4 — T-W4-TELEMETRY-OPT-IN pin tests.
/// Acceptance gates from plan.yaml:
///   * Default OFF (DSGVO-konformes Opt-in).
///   * Kein Pfad/Hostname im Payload (Allow-List).
///   * Settings-Toggle vorhanden (TelemetryService.IsEnabled persistiert).
///   * ADR fuer Endpoint und Retention vorhanden.
///   * CI verbietet Default-Flip ohne ADR-Update.
/// </summary>
public sealed class Wave4TelemetryOptInTests
{
    private static string TempStatePath() =>
        Path.Combine(Path.GetTempPath(), "rom-w4-telemetry-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Default_IsDisabled_OptIn()
    {
        var path = TempStatePath();
        try
        {
            var svc = new TelemetryService(path);
            Assert.False(svc.IsEnabled);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Disabled_RecordIsNoOp_NoEventsCaptured()
    {
        var path = TempStatePath();
        try
        {
            var svc = new TelemetryService(path);
            var accepted = svc.Record("run.completed", new Dictionary<string, object?> { ["count"] = 7 });
            Assert.False(accepted);
            Assert.Empty(svc.RecentEvents);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Enabled_RecordsEventWithAggregateFieldsOnly()
    {
        var path = TempStatePath();
        try
        {
            var svc = new TelemetryService(path);
            svc.Enable();
            Assert.True(svc.IsEnabled);

            var accepted = svc.Record("run.completed", new Dictionary<string, object?>
            {
                ["count"] = 42,
                ["durationMs"] = 1500,
                ["consoleKey"] = "NES",
            });
            Assert.True(accepted);
            Assert.Single(svc.RecentEvents);
            var ev = svc.RecentEvents[0];
            Assert.Equal("run.completed", ev.Name);
            Assert.Equal(42, ev.Fields["count"]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Record_ForbiddenFields_AreStrippedSilently()
    {
        var path = TempStatePath();
        try
        {
            var svc = new TelemetryService(path);
            svc.Enable();

            var accepted = svc.Record("run.completed", new Dictionary<string, object?>
            {
                ["count"] = 1,
                ["path"] = @"C:\Users\alice\roms\smb.nes",
                ["hostname"] = "ALICE-PC",
                ["username"] = "alice",
                ["sourcePath"] = @"C:\foo",
            });

            Assert.True(accepted);
            var ev = svc.RecentEvents[0];
            Assert.True(ev.Fields.ContainsKey("count"));
            Assert.False(ev.Fields.ContainsKey("path"), "path must be stripped (PII)");
            Assert.False(ev.Fields.ContainsKey("hostname"), "hostname must be stripped (PII)");
            Assert.False(ev.Fields.ContainsKey("username"), "username must be stripped (PII)");
            Assert.False(ev.Fields.ContainsKey("sourcePath"), "any *Path field must be stripped");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void State_IsPersisted_AcrossInstances()
    {
        var path = TempStatePath();
        try
        {
            var first = new TelemetryService(path);
            first.Enable();
            Assert.True(File.Exists(path));

            var second = new TelemetryService(path);
            Assert.True(second.IsEnabled);

            second.Disable();
            var third = new TelemetryService(path);
            Assert.False(third.IsEnabled);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AllowList_DoesNotPermitPathOrIdentityKeys()
    {
        // Reflection pin: defends against future "convenient" extensions.
        var allowed = TelemetryEventAllowList.AllowedFields;
        Assert.NotEmpty(allowed);
        foreach (var key in allowed)
        {
            var lower = key.ToLowerInvariant();
            Assert.DoesNotContain("path", lower);
            Assert.DoesNotContain("host", lower);
            Assert.DoesNotContain("user", lower);
            Assert.DoesNotContain("ip", lower);
        }
    }

    [Fact]
    public void Adr_TelemetryOptIn_ExistsInRepo()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var adrDir = Path.Combine(dir!.FullName, "docs", "adrs");
        var matches = Directory.EnumerateFiles(adrDir, "ADR-*telemetry*.md", SearchOption.TopDirectoryOnly).ToList();
        Assert.NotEmpty(matches);
    }

    [Fact]
    public void Source_DefaultEnabledLiteral_IsFalse()
    {
        // Source-inspection pin: protects against the failure_mode
        // "Telemetrie-Default rutscht auf ON". A change here forces the
        // contributor to also touch the ADR + this test.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(
            dir!.FullName,
            "src", "Romulus.Infrastructure", "Telemetry", "TelemetryService.cs"));
        Assert.Contains("DefaultEnabled=false", src.Replace(" ", string.Empty), StringComparison.Ordinal);
    }
}
