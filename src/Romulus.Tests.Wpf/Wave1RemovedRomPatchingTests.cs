using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W1-FEATURE-CULL Step D — ROM-Patching removal pin tests.
/// Reflection-based assertions that the patching surface (IPS/BPS/UPS/xdelta)
/// has been completely removed from Romulus.Contracts, Romulus.Infrastructure
/// and Romulus.UI.Wpf, including i18n keys.
/// Reverting any of these would re-introduce a documented Wave-1 cull violation.
/// </summary>
public sealed class Wave1RemovedRomPatchingTests
{
    private static Type? FindType(string fullName)
        => AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(fullName, throwOnError: false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t is not null);

    [Theory]
    [InlineData("Romulus.Contracts.Models.PatchApplyResult")]
    public void RomPatchingTypes_AreRemoved(string fullName)
        => Assert.Null(FindType(fullName));

    [Theory]
    [InlineData("Romulus.Infrastructure.Analysis.IntegrityService", "DetectPatchFormat")]
    [InlineData("Romulus.Infrastructure.Analysis.IntegrityService", "ApplyPatch")]
    [InlineData("Romulus.Infrastructure.Analysis.IntegrityService", "ResolvePatchFormat")]
    [InlineData("Romulus.UI.Wpf.Services.FeatureService", "DetectPatchFormat")]
    [InlineData("Romulus.UI.Wpf.Services.FeatureService", "ApplyPatch")]
    [InlineData("Romulus.UI.Wpf.Services.HeaderSecurityService", "DetectPatchFormat")]
    public void RomPatchingMethods_AreRemoved(string typeName, string methodName)
    {
        var type = FindType(typeName);
        if (type is null)
        {
            // Type itself absent counts as removed surface.
            return;
        }

        var method = type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.Null(method);
    }

    [Fact]
    public void IHeaderService_DoesNotExposeDetectPatchFormat()
    {
        var iface = FindType("Romulus.UI.Wpf.Services.IHeaderService");
        Assert.NotNull(iface);
        var method = iface!.GetMethod("DetectPatchFormat");
        Assert.Null(method);
    }

    [Fact]
    public void FeatureCommandKeys_DoNotExposePatchPipeline()
    {
        var type = FindType("Romulus.UI.Wpf.Models.FeatureCommandKeys");
        Assert.NotNull(type);
        var field = type!.GetField("PatchPipeline", BindingFlags.Public | BindingFlags.Static);
        Assert.Null(field);
    }

    [Theory]
    [InlineData("data/i18n/de.json")]
    [InlineData("data/i18n/en.json")]
    [InlineData("data/i18n/fr.json")]
    public void I18n_DoesNotContainPatchPipelineKeys(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            Assert.False(
                prop.Name.StartsWith("Cmd.PatchPipeline", StringComparison.Ordinal)
                || prop.Name.StartsWith("Tool.PatchPipeline", StringComparison.Ordinal),
                $"i18n key '{prop.Name}' in {relativePath} must be removed (T-W1 ROM-Patching cull).");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
