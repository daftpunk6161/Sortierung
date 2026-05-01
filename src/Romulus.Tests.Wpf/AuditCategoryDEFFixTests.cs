using System.Reflection;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Infrastructure.Metrics;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD tests for full-repo-audit categories D, E, and F findings.
/// D-01: Region preference bidirectional sync (MainViewModel ↔ SetupViewModel)
/// D-02: API error codes centralized in ApiErrorCodes
/// D-03: GetCategoryOverride wired into enrichment pipeline
/// E-01: fr.json mojibake repair
/// E-02: App.Title typo fix
/// E-03: PhaseMetricsCollector hardcoded strings → RunConstants
/// E-04: Empty UnauthorizedAccessException catch blocks documented
/// F-05: Strengthened no-crash tests (covered in original test files)
/// F-06: Tautological assertions fixed (covered in original test files)
/// </summary>
public sealed class AuditCategoryDEFFixTests
{
    // ═══════════════════════════════════════════════════════════════════
    // D-01: Region preference bidirectional sync
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void D01_MainVM_PreferEU_SyncsToSetup_RegionItems()
    {
        var vm = new MainViewModel();
        // Default: EU is active
        var euItem = vm.Setup.RegionItems.First(r => r.Code == "EU");
        Assert.True(euItem.IsActive);

        // Toggle off in MainViewModel
        vm.PreferEU = false;

        // SetupViewModel.RegionItems must reflect the change
        Assert.False(euItem.IsActive);
    }

    [Fact]
    public void D01_MainVM_PreferDE_SyncsToSetup_RegionItems()
    {
        var vm = new MainViewModel();
        var deItem = vm.Setup.RegionItems.First(r => r.Code == "DE");
        Assert.False(deItem.IsActive); // DE defaults to inactive

        // Enable in MainViewModel
        vm.PreferDE = true;

        Assert.True(deItem.IsActive);
    }

    [Fact]
    public void D01_Setup_RegionItem_IsActive_SyncsToMainVM_PreferBool()
    {
        var vm = new MainViewModel();

        // Toggle a Setup region off
        var usItem = vm.Setup.RegionItems.First(r => r.Code == "US");
        Assert.True(vm.PreferUS);

        usItem.IsActive = false;

        Assert.False(vm.PreferUS);
    }

    [Fact]
    public void D01_Setup_RegionItem_Enable_SyncsToMainVM()
    {
        var vm = new MainViewModel();

        var krItem = vm.Setup.RegionItems.First(r => r.Code == "KR");
        Assert.False(vm.PreferKR);

        krItem.IsActive = true;

        Assert.True(vm.PreferKR);
    }

    [Fact]
    public void D01_Bidirectional_NoInfiniteLoop()
    {
        // Setting from either side must not cause stack overflow via reentrancy
        var vm = new MainViewModel();

        vm.PreferSCAN = true;
        var scanItem = vm.Setup.RegionItems.First(r => r.Code == "SCAN");
        Assert.True(scanItem.IsActive);

        scanItem.IsActive = false;
        Assert.False(vm.PreferSCAN);
    }

    [Fact]
    public void D01_AllSixteenRegions_SyncMainToSetup()
    {
        var vm = new MainViewModel();
        var codes = new[]
        {
            "EU", "US", "JP", "WORLD", "DE", "FR", "IT", "ES",
            "AU", "ASIA", "KR", "CN", "BR", "NL", "SE", "SCAN"
        };

        foreach (var code in codes)
        {
            // Set all to false via MainViewModel
            SetPreferBool(vm, code, false);
        }

        foreach (var code in codes)
        {
            var item = vm.Setup.RegionItems.First(r => r.Code == code);
            Assert.False(item.IsActive, $"Region {code} should be inactive after MainVM set false");
        }

        foreach (var code in codes)
        {
            // Set all to true via MainViewModel
            SetPreferBool(vm, code, true);
        }

        foreach (var code in codes)
        {
            var item = vm.Setup.RegionItems.First(r => r.Code == code);
            Assert.True(item.IsActive, $"Region {code} should be active after MainVM set true");
        }
    }

    [Fact]
    public void D01_AllSixteenRegions_SyncSetupToMain()
    {
        var vm = new MainViewModel();
        var codes = new[]
        {
            "EU", "US", "JP", "WORLD", "DE", "FR", "IT", "ES",
            "AU", "ASIA", "KR", "CN", "BR", "NL", "SE", "SCAN"
        };

        foreach (var code in codes)
        {
            var item = vm.Setup.RegionItems.First(r => r.Code == code);
            item.IsActive = true;
        }

        foreach (var code in codes)
        {
            Assert.True(GetPreferBool(vm, code), $"MainVM Prefer{code} should be true after Setup.RegionItem set true");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // D-02: Centralized API error codes
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void D02_ApiErrorCodes_HasExpectedCategories()
    {
        // Verify all error code categories exist via reflection
        var fields = typeof(ApiErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Contains(fields, f => f.StartsWith("AUTH-"));
        Assert.Contains(fields, f => f.StartsWith("RUN-"));
        Assert.Contains(fields, f => f.StartsWith("DAT-"));
        Assert.Contains(fields, f => f.StartsWith("WATCH-"));
        Assert.Contains(fields, f => f.StartsWith("IO-"));
        Assert.Contains(fields, f => f.StartsWith("PROFILE-"));
        Assert.Contains(fields, f => f.StartsWith("WORKFLOW-"));
        Assert.Contains(fields, f => f.StartsWith("COLLECTION-"));
    }

    [Fact]
    public void D02_ApiErrorCodes_NoDuplicateValues()
    {
        var fields = typeof(ApiErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var duplicates = fields.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void D02_ProgramCs_NoHardcodedErrorStrings()
    {
        // Verify Program.cs uses ApiErrorCodes constants, not hardcoded strings
        var programPath = Path.Combine(FindSrcDir(), "Romulus.Api", "Program.cs");
        var content = File.ReadAllText(programPath);

        // These previously hardcoded strings should now be ApiErrorCodes references
        var knownOldCodes = new[] { "\"RUN-NOT-FOUND\"", "\"AUTH-UNAUTHORIZED\"", "\"RUN-IN-PROGRESS\"" };

        foreach (var old in knownOldCodes)
        {
            Assert.DoesNotContain(old, content);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // D-03: GetCategoryOverride wired into pipeline
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void D03_EnrichmentPipelinePhase_ReferencesGetCategoryOverride()
    {
        var pipelinePath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "EnrichmentPipelinePhase.cs");
        var content = File.ReadAllText(pipelinePath);

        Assert.Contains("GetCategoryOverride", content);
    }

    [Fact]

    public void D03_ScanPipelinePhase_DoesNotUseSyncOverAsyncGetResult()
    {
        var scanPath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "ScanPipelinePhase.cs");
        var content = File.ReadAllText(scanPath);

        Assert.DoesNotContain("GetAwaiter().GetResult()", content);
    }

    [Fact]
    public void D03_RunOrchestratorPreviewHelpers_DoesNotUseSyncOverAsyncGetResult()
    {
        var helperPath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Orchestration", "RunOrchestrator.PreviewAndPipelineHelpers.cs");
        var content = File.ReadAllText(helperPath);

        Assert.DoesNotContain("GetAwaiter().GetResult()", content);
    }

    // ═══════════════════════════════════════════════════════════════════
    // E-01: fr.json encoding fix
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void E01_FrJson_NoMojibakeCharacters()
    {
        var frPath = Path.Combine(FindDataDir(), "i18n", "fr.json");
        var content = File.ReadAllText(frPath);

        Assert.DoesNotContain("├á", content);  // was à
    }

    [Fact]
    public void E01_FrJson_ContainsProperFrenchCharacters()
    {
        var frPath = Path.Combine(FindDataDir(), "i18n", "fr.json");
        var content = File.ReadAllText(frPath);

        // Must contain proper French diacritics
        Assert.Contains("é", content);
        Assert.Contains("è", content);
        Assert.Contains("ê", content);
        Assert.Contains("ç", content);
        Assert.Contains("à", content);
    }

    [Fact]
    public void E01_FrJson_IsValidJson()
    {
        var frPath = Path.Combine(FindDataDir(), "i18n", "fr.json");
        var content = File.ReadAllText(frPath);

        // Must parse as valid JSON
        var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.EnumerateObject().Any());
    }

    // ═══════════════════════════════════════════════════════════════════
    // E-02: App.Title fix
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void E02_AllLocales_AppTitle_IsRomulus()
    {
        foreach (var locale in new[] { "en", "de", "fr" })
        {
            var path = Path.Combine(FindDataDir(), "i18n", $"{locale}.json");
            var content = File.ReadAllText(path);
            var doc = JsonDocument.Parse(content);

            Assert.True(doc.RootElement.TryGetProperty("App.Title", out var title),
                $"{locale}.json must have App.Title");
            Assert.Equal("Romulus", title.GetString());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // E-03: RunConstants phase-status centralization
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void E03_RunConstants_HasPhaseStatusConstants()
    {
        Assert.Equal("Running", RunConstants.PhaseStatusRunning);
        Assert.Equal("Completed", RunConstants.PhaseStatusCompleted);
    }

    [Fact]
    public void E03_PhaseMetricsCollector_UsesRunConstants()
    {
        var collectorPath = Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Metrics", "PhaseMetricsCollector.cs");
        var content = File.ReadAllText(collectorPath);

        // Must reference RunConstants, not hardcoded "Running"/"Completed"
        Assert.Contains("RunConstants.PhaseStatusRunning", content);
        Assert.Contains("RunConstants.PhaseStatusCompleted", content);

        // Should not have standalone hardcoded status strings (excluding comments/using)
        var lines = content.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("using"))
            .ToList();
        var hardcoded = lines.Where(l => l.Contains("\"Running\"") || l.Contains("\"Completed\"")).ToList();
        Assert.Empty(hardcoded);
    }

    // ═══════════════════════════════════════════════════════════════════
    // E-04: Empty catch blocks documented
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void E04_NoCatchBlocksWithoutCommentOrAction()
    {
        // Verify files previously identified with empty catches now have SUPPRESSED comments
        var files = new[]
        {
            Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "DiscHeaderDetector.cs"),
            Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "CartridgeHeaderDetector.cs"),
            Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ConsoleDetector.cs"),
            Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Deduplication", "FolderDeduplicator.cs"),
            Path.Combine(FindSrcDir(), "Romulus.Infrastructure", "Conversion", "ConversionExecutor.cs"),
        };

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("catch") && trimmed.Contains("UnauthorizedAccessException"))
                {
                    // Single-line catch block: catch (...) { /* SUPPRESSED: ... */ }
                    if (trimmed.Contains("SUPPRESSED") || trimmed.Contains("//"))
                        continue;

                    // Multi-line: look at the catch block body (next few lines) for a SUPPRESSED comment or actual code
                    var bodyHasContent = false;
                    for (int j = i + 1; j < Math.Min(i + 5, lines.Length); j++)
                    {
                        var bodyLine = lines[j].Trim();
                        if (bodyLine == "{" || bodyLine == "") continue;
                        if (bodyLine == "}") break;
                        bodyHasContent = true;
                        break;
                    }
                    Assert.True(bodyHasContent,
                        $"Empty catch (UnauthorizedAccessException) without SUPPRESSED comment in {Path.GetFileName(file)} near line {i + 1}");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string FindSrcDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src");
    }

    private static string FindDataDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data")))
            dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "data");
    }

    private static void SetPreferBool(MainViewModel vm, string code, bool value)
    {
        switch (code)
        {
            case "EU": vm.PreferEU = value; break;
            case "US": vm.PreferUS = value; break;
            case "JP": vm.PreferJP = value; break;
            case "WORLD": vm.PreferWORLD = value; break;
            case "DE": vm.PreferDE = value; break;
            case "FR": vm.PreferFR = value; break;
            case "IT": vm.PreferIT = value; break;
            case "ES": vm.PreferES = value; break;
            case "AU": vm.PreferAU = value; break;
            case "ASIA": vm.PreferASIA = value; break;
            case "KR": vm.PreferKR = value; break;
            case "CN": vm.PreferCN = value; break;
            case "BR": vm.PreferBR = value; break;
            case "NL": vm.PreferNL = value; break;
            case "SE": vm.PreferSE = value; break;
            case "SCAN": vm.PreferSCAN = value; break;
        }
    }

    private static bool GetPreferBool(MainViewModel vm, string code) => code switch
    {
        "EU" => vm.PreferEU, "US" => vm.PreferUS, "JP" => vm.PreferJP, "WORLD" => vm.PreferWORLD,
        "DE" => vm.PreferDE, "FR" => vm.PreferFR, "IT" => vm.PreferIT, "ES" => vm.PreferES,
        "AU" => vm.PreferAU, "ASIA" => vm.PreferASIA, "KR" => vm.PreferKR, "CN" => vm.PreferCN,
        "BR" => vm.PreferBR, "NL" => vm.PreferNL, "SE" => vm.PreferSE, "SCAN" => vm.PreferSCAN,
        _ => false,
    };
}
