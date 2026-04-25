using System.Text.Json;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Data-validation tests retained from the historical "tracker block 7-12" set.
/// All source-mirror assertions were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class TrackerBlock7To12RedTests
{
    [Fact]
    public void TH_06_ToolHashes_MustNotContainPendingMarkers_AndEcmCapabilityRemoved()
    {
        using var toolHashes = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "tool-hashes.json")));
        var tools = toolHashes.RootElement.GetProperty("Tools");

        foreach (var tool in tools.EnumerateObject())
        {
            var hash = tool.Value.GetString() ?? string.Empty;
            Assert.DoesNotContain("PENDING-VERIFY", hash, StringComparison.OrdinalIgnoreCase);
        }

        using var conversionRegistry = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "conversion-registry.json")));
        var hasUnecmCapability = conversionRegistry.RootElement
            .GetProperty("capabilities")
            .EnumerateArray()
            .Any(capability =>
            {
                if (!capability.TryGetProperty("tool", out var toolElement)
                    || !toolElement.TryGetProperty("toolName", out var toolNameElement))
                {
                    return false;
                }

                return string.Equals(toolNameElement.GetString(), "unecm", StringComparison.OrdinalIgnoreCase);
            });

        Assert.False(hasUnecmCapability, "ECM capability must be removed until an authenticated unecm tool hash is available.");
    }

    [Fact]
    public void I18N_PhaseLocalizationKeys_MustExistForAllSupportedLocales()
    {
        AssertPhaseLocalizationKeysExist("de");
        AssertPhaseLocalizationKeysExist("en");
        AssertPhaseLocalizationKeysExist("fr");
    }

    [Fact]
    public void I18N_FrenchTranslations_MustUseRomulusBranding()
    {
        using var frDoc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "i18n", "fr.json")));
        var fr = frDoc.RootElement;

        foreach (var key in new[] { "App.TrayRunning", "Scheduler.EmailSubject", "Scheduler.WebhookSummary" })
        {
            var value = fr.GetProperty(key).GetString() ?? string.Empty;
            Assert.Contains("Romulus", value, StringComparison.Ordinal);
            Assert.DoesNotContain("ROM Cleanup", value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertPhaseLocalizationKeysExist(string locale)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "i18n", locale + ".json")));
        var root = doc.RootElement;

        for (var phase = 1; phase <= 7; phase++)
        {
            Assert.True(root.TryGetProperty($"Run.PhaseDetail.{phase}", out _),
                $"{locale}.json must define Run.PhaseDetail.{phase}");
        }

        Assert.True(root.TryGetProperty("Run.PhaseStatus.Pending", out _), $"{locale}.json must define Run.PhaseStatus.Pending");
        Assert.True(root.TryGetProperty("Run.PhaseStatus.Active", out _), $"{locale}.json must define Run.PhaseStatus.Active");
        Assert.True(root.TryGetProperty("Run.PhaseStatus.Completed", out _), $"{locale}.json must define Run.PhaseStatus.Completed");
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved from data directory.");
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }
}
