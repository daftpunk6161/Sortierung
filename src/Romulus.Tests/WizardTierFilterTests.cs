using System.Collections.Generic;
using System.Linq;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Pins the Wave-1 acceptance criterion of T-W1-CONSOLE-COVERAGE that the
/// Onboarding-Wizard / Default-Filter shows only tier="core" consoles by
/// default and falls back to all detections when no core console was found.
///
/// The filter logic lives in <see cref="ConsoleDetector.FilterTopByTier"/>
/// (Single Source of Truth in Romulus.Core, consumed by the WPF wizard).
/// </summary>
public sealed class WizardTierFilterTests
{
    private static ConsoleDetector LoadDetector()
    {
        // Walk up to repo root and load the real consoles.json so the test
        // pins behaviour against production data.
        var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (dir is not null
               && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "src", "Romulus.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var consolesPath = System.IO.Path.Combine(dir!.FullName, "data", "consoles.json");
        Assert.True(System.IO.File.Exists(consolesPath));
        return ConsoleDetector.LoadFromJson(System.IO.File.ReadAllText(consolesPath));
    }

    [Fact]
    public void FilterTopByTier_KeepsOnlyCore_WhenAtLeastOneCorePresent()
    {
        var detector = LoadDetector();
        var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = 5,        // core
            ["SNES"] = 3,       // core
            ["JAG"] = 7,        // best-effort (highest count)
            ["A26"] = 4,        // best-effort
        };

        var (filtered, fellBack) = ConsoleDetector.FilterTopByTier(counts, detector, "core", take: 5);

        Assert.False(fellBack);
        // All returned keys must be core; best-effort entries (JAG, A26) must be dropped
        // even though JAG had the highest absolute count.
        Assert.All(filtered, kv =>
        {
            var info = detector.GetConsole(kv.Key);
            Assert.NotNull(info);
            Assert.Equal("core", info!.Tier);
        });
        Assert.Contains(filtered, kv => kv.Key.Equals("NES", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filtered, kv => kv.Key.Equals("SNES", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, kv => kv.Key.Equals("JAG", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, kv => kv.Key.Equals("A26", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FilterTopByTier_FallsBackToAll_WhenNoCorePresent()
    {
        var detector = LoadDetector();
        var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["JAG"] = 7,        // best-effort
            ["A26"] = 4,        // best-effort
            ["unknown"] = 2,    // not in catalog
        };

        var (filtered, fellBack) = ConsoleDetector.FilterTopByTier(counts, detector, "core", take: 5);

        Assert.True(fellBack);
        // Fallback returns all detections so the Wizard never shows an empty list
        // when the user's library contains only best-effort consoles.
        Assert.NotEmpty(filtered);
        Assert.Equal(counts.Count, filtered.Count);
    }

    [Fact]
    public void FilterTopByTier_OrdersByCountDescending_ThenByKeyOrdinal()
    {
        var detector = LoadDetector();
        var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["SNES"] = 2,
            ["NES"] = 5,
            ["GB"] = 5,
        };

        var (filtered, _) = ConsoleDetector.FilterTopByTier(counts, detector, "core", take: 5);

        var ordered = filtered.ToList();
        Assert.Equal("GB", ordered[0].Key);   // tied at 5, GB before NES alphabetically
        Assert.Equal("NES", ordered[1].Key);
        Assert.Equal("SNES", ordered[2].Key);
    }

    [Fact]
    public void FilterTopByTier_HonoursTakeLimit()
    {
        var detector = LoadDetector();
        // 4 core entries, but take=2 should clip to the top 2.
        var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = 1,
            ["SNES"] = 4,
            ["GB"] = 3,
            ["GBA"] = 2,
        };

        var (filtered, _) = ConsoleDetector.FilterTopByTier(counts, detector, "core", take: 2);

        Assert.Equal(2, filtered.Count);
        Assert.Equal("SNES", filtered[0].Key);
        Assert.Equal("GB", filtered[1].Key);
    }
}
