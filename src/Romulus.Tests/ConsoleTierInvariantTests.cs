using System.IO;
using System.Linq;
using System.Text.Json;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Pins the console-tier contract introduced by T-W1-CONSOLE-COVERAGE
/// (Strategic Reduction 2026, Wave 1).
///
/// Invariants:
///  - Every console in data/consoles.json has a tier field.
///  - Tier is exactly one of "core" or "best-effort".
///  - Exactly 30 consoles are tier=core (Top-30 mainstream coverage).
///  - The "core" set is stable (drift requires conscious update of this test).
///  - ConsoleDetector.LoadFromJson surfaces the tier on every loaded ConsoleInfo.
/// </summary>
public class ConsoleTierInvariantTests
{
    private static readonly string[] ExpectedCoreKeys =
    {
        "3DS", "ARCADE", "DC", "GB", "GBA", "GBC", "GC", "GG", "MAME", "MD",
        "N64", "NDS", "NEOGEO", "NES", "PCE", "PCECD", "PS1", "PS2", "PS3",
        "PSP", "SAT", "SCD", "SMS", "SNES", "SWITCH", "VITA", "WII", "WIIU",
        "X360", "XBOX",
    };

    private static string LocateConsolesJson()
    {
        // Tests run from a bin/Debug/net... directory; walk up to repo root.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "consoles.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("data/consoles.json not found by walking up from test working directory.");
    }

    [Fact]
    public void EveryConsole_HasTierField_AndItIsCoreOrBestEffort()
    {
        var path = LocateConsolesJson();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var consoles = doc.RootElement.GetProperty("consoles");

        foreach (var c in consoles.EnumerateArray())
        {
            var key = c.GetProperty("key").GetString();
            Assert.False(string.IsNullOrWhiteSpace(key), "console.key must not be empty");
            Assert.True(c.TryGetProperty("tier", out var tier), $"console '{key}' has no tier field");
            var tierValue = tier.GetString();
            Assert.True(
                tierValue is "core" or "best-effort",
                $"console '{key}' has invalid tier '{tierValue}' (expected 'core' or 'best-effort')");
        }
    }

    [Fact]
    public void CoreTier_ContainsExactly30_AndMatchesExpectedTop30()
    {
        var path = LocateConsolesJson();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var consoles = doc.RootElement.GetProperty("consoles");

        var coreKeys = consoles.EnumerateArray()
            .Where(c => c.TryGetProperty("tier", out var t) && t.GetString() == "core")
            .Select(c => c.GetProperty("key").GetString()!)
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToArray();

        var expected = ExpectedCoreKeys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray();

        Assert.Equal(30, coreKeys.Length);
        Assert.Equal(expected, coreKeys);
    }

    [Fact]
    public void ConsoleDetector_LoadFromJson_SurfacesTierOnConsoleInfo()
    {
        var path = LocateConsolesJson();
        var detector = ConsoleDetector.LoadFromJson(File.ReadAllText(path));

        var keys = detector.AllConsoleKeys;
        Assert.NotEmpty(keys);

        // Every loaded console must surface a tier value (no null, no empty).
        foreach (var key in keys)
        {
            var c = detector.GetConsole(key);
            Assert.NotNull(c);
            Assert.False(string.IsNullOrWhiteSpace(c!.Tier), $"console '{key}' loaded with empty tier");
            Assert.True(c.Tier is "core" or "best-effort",
                $"console '{key}' loaded with invalid tier '{c.Tier}'");
        }

        // At least one well-known core console must come back as core.
        var nes = detector.GetConsole("NES");
        Assert.NotNull(nes);
        Assert.Equal("core", nes!.Tier);

        // At least one well-known best-effort console must come back as best-effort.
        var astrocade = detector.GetConsole("ASTROCADE");
        Assert.NotNull(astrocade);
        Assert.Equal("best-effort", astrocade!.Tier);
    }

    [Fact]
    public void TierDefault_IsBestEffort_WhenJsonOmitsField()
    {
        // ConsoleInfo's JSON loader must default to best-effort for legacy entries
        // that don't yet declare a tier (forward-compat for schema migration).
        const string minimal = """
            {
              "consoles": [
                { "key": "FAKE", "displayName": "Fake Console" }
              ]
            }
            """;

        var detector = ConsoleDetector.LoadFromJson(minimal);
        var fake = detector.GetConsole("FAKE");
        Assert.NotNull(fake);
        Assert.Equal("best-effort", fake!.Tier);
    }

    /// <summary>
    /// Regression pin: T-W1-CONSOLE-COVERAGE added the 'tier' field to data/consoles.json
    /// but consoles.schema.json was not updated. StartupDataSchemaValidator (which runs on
    /// every API/CLI/WPF startup) then rejects the file with $.consoles[0].tier is not allowed.
    /// This test fails fast if the data file ever drifts away from its schema again.
    /// </summary>
    [Fact]
    public void ConsolesJson_ConformsToSchema_IncludingTierField()
    {
        var consolesPath = LocateConsolesJson();
        var schemaPath = Path.Combine(
            Path.GetDirectoryName(consolesPath)!,
            "schemas",
            "consoles.schema.json");
        Assert.True(File.Exists(schemaPath), $"schema not found at {schemaPath}");

        // Use the same validator the application bootstraps with so the test's
        // verdict matches production startup behavior.
        var dataDir = Path.GetDirectoryName(consolesPath)!;
        var ex = Record.Exception(() =>
            Romulus.Infrastructure.Configuration.StartupDataSchemaValidator
                .ValidateRequiredFiles(dataDir));
        Assert.Null(ex);
    }
}
