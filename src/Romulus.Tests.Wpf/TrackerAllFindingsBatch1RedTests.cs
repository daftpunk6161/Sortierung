using System.Text.Json;
using Romulus.Infrastructure.Configuration;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavior- and data-validation tests retained from the historical "tracker batch 1" set.
/// Source-mirror and reflection-existence assertions were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class TrackerAllFindingsBatch1RedTests
{
    [Fact]
    public void Data04_NkitLossyRules_MustNotClaimLossless()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "conversion-registry.json")));
        var capabilities = doc.RootElement.GetProperty("capabilities").EnumerateArray();

        foreach (var capability in capabilities)
        {
            if (!capability.TryGetProperty("sourceExtension", out var sourceElement))
                continue;

            var sourceExtension = sourceElement.GetString() ?? string.Empty;
            if (!sourceExtension.StartsWith(".nkit", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!capability.TryGetProperty("resultIntegrity", out var integrityElement))
                continue;

            var resultIntegrity = integrityElement.GetString() ?? string.Empty;
            if (!string.Equals(resultIntegrity, "Lossy", StringComparison.OrdinalIgnoreCase))
                continue;

            Assert.True(capability.TryGetProperty("lossless", out var losslessElement));
            Assert.False(losslessElement.GetBoolean());
        }
    }

    [Fact]
    public void Data05_FormatScores_MustContainRomExtension()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "format-scores.json")));
        var formatScores = doc.RootElement.GetProperty("formatScores");

        Assert.True(formatScores.TryGetProperty(".rom", out var scoreElement));
        Assert.True(scoreElement.ValueKind == JsonValueKind.Number);
        Assert.True(scoreElement.GetInt32() > 300);
    }

    [Fact]
    public void Data09_SettingsStructure_AllowsUnknownTopLevelKeys()
    {
        var json = """
        {
          "general": { "logLevel": "Info" },
          "futureExtensibility": { "customFlag": true }
        }
        """;

        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.DoesNotContain(errors, e => e.Contains("Unknown top-level key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Data12_I18nFallback_UsesEnglishBaseForUnknownLocale()
    {
        var service = new LocalizationService();
        service.SetLocale("zz");

        Assert.Equal("Romulus is running in the background", service["App.TrayRunning"]);
    }

    private static string FindRepoFile(params string[] segments)
        => Romulus.Tests.TestFixtures.RepoPaths.RepoFile(segments);
}
