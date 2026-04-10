using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for pure static methods in FeatureCommandService.Security.cs,
/// FeatureCommandService.Productization.cs, FeatureCommandService.Collection.cs,
/// and MainViewModel.Productization.cs to close uncovered branches.
/// </summary>
public sealed class FeatureCommandSecurityCoverageTests
{
    // ═══ TryNormalizeCustomJunkRules ═══════════════════════════════════

    [Fact]
    public void TryNormalize_ValidSingleRule_ReturnsTrue()
    {
        var json = """
        {
            "enabled": true,
            "rules": [
                {
                    "field": "name",
                    "operator": "contains",
                    "value": "(Beta)",
                    "logic": "AND",
                    "action": "SetCategoryJunk",
                    "priority": 100,
                    "enabled": true
                }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out var preview, out var error);

        Assert.True(result);
        Assert.Empty(error);
        Assert.Single(doc.Rules);
        Assert.Equal("name", doc.Rules[0].Field);
        Assert.Equal("contains", doc.Rules[0].Operator);
        Assert.Equal("(Beta)", doc.Rules[0].Value);
        Assert.Equal("AND", doc.Rules[0].Logic);
        Assert.Equal(100, doc.Rules[0].Priority);
        Assert.True(doc.Enabled);
        Assert.Contains("Custom Junk Rules Vorschau", preview);
    }

    [Fact]
    public void TryNormalize_MultipleRules_AllNormalized()
    {
        var json = """
        {
            "enabled": false,
            "rules": [
                { "field": "name", "operator": "contains", "value": "(Beta)", "action": "SetCategoryJunk" },
                { "field": "region", "operator": "equals", "value": "Japan", "action": "SetCategoryJunk" },
                { "field": "extension", "operator": "regex", "value": "^\\.(zip|7z)$", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal(3, doc.Rules.Count);
        Assert.False(doc.Enabled);
        Assert.Equal("region", doc.Rules[1].Field);
        Assert.Equal("extension", doc.Rules[2].Field);
    }

    [Fact]
    public void TryNormalize_InvalidJson_ReturnsFalse()
    {
        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            "{ not valid json !!!", out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Ungueltiges JSON", error);
    }

    [Fact]
    public void TryNormalize_NullDeserialization_ReturnsFalse()
    {
        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            "null", out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("konnte nicht gelesen werden", error);
    }

    [Fact]
    public void TryNormalize_MissingRulesField_ReturnsFalse()
    {
        // Rules property defaults to [] but explicit null triggers the check
        var json = """{ "enabled": true, "rules": null }""";

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("'rules' fehlt", error);
    }

    [Fact]
    public void TryNormalize_InvalidField_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "invalid_field", "operator": "contains", "value": "test", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Feld 'invalid_field' ist nicht erlaubt", error);
    }

    [Fact]
    public void TryNormalize_InvalidOperator_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "startswith", "value": "test", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Operator 'startswith' ist nicht erlaubt", error);
    }

    [Fact]
    public void TryNormalize_EmptyValue_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "contains", "value": "", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Value darf nicht leer sein", error);
    }

    [Fact]
    public void TryNormalize_InvalidRegex_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "regex", "value": "(((unclosed", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Regex ungueltig", error);
    }

    [Fact]
    public void TryNormalize_InvalidLogic_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "contains", "value": "test", "logic": "XOR", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Logic 'XOR' ist ungueltig", error);
    }

    [Fact]
    public void TryNormalize_WrongAction_ReturnsFalse()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "contains", "value": "test", "action": "Delete" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Action muss 'SetCategoryJunk' sein", error);
    }

    [Fact]
    public void TryNormalize_DefaultsApplied_WhenFieldsOmitted()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "contains", "value": "test", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal("AND", doc.Rules[0].Logic);
        Assert.Equal("SetCategoryJunk", doc.Rules[0].Action);
        Assert.Equal(1000, doc.Rules[0].Priority);
    }

    [Fact]
    public void TryNormalize_PriorityZero_GetsAutoAssigned()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "contains", "value": "a", "priority": 0, "action": "SetCategoryJunk" },
                { "field": "region", "operator": "equals", "value": "b", "priority": 0, "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal(1000, doc.Rules[0].Priority);
        Assert.Equal(1001, doc.Rules[1].Priority);
    }

    [Fact]
    public void TryNormalize_EqualsOperator_MappedToEqInRuleEngine()
    {
        var json = """
        {
            "rules": [
                { "field": "name", "operator": "equals", "value": "TestGame", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal("equals", doc.Rules[0].Operator);
    }

    [Fact]
    public void TryNormalize_CaseInsensitiveFields()
    {
        var json = """
        {
            "rules": [
                { "field": "NAME", "operator": "CONTAINS", "value": "test", "logic": "or", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal("name", doc.Rules[0].Field);
        Assert.Equal("contains", doc.Rules[0].Operator);
        Assert.Equal("OR", doc.Rules[0].Logic);
    }

    [Fact]
    public void TryNormalize_PathField_Accepted()
    {
        var json = """
        {
            "rules": [
                { "field": "path", "operator": "contains", "value": "/junk/", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.Equal("path", doc.Rules[0].Field);
    }

    [Fact]
    public void TryNormalize_MultipleErrors_AllReported()
    {
        var json = """
        {
            "rules": [
                { "field": "bad1", "operator": "bad2", "value": "", "action": "Delete" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        Assert.False(result);
        Assert.Contains("Feld 'bad1' ist nicht erlaubt", error);
        Assert.Contains("Operator 'bad2' ist nicht erlaubt", error);
        Assert.Contains("Value darf nicht leer sein", error);
        Assert.Contains("Action muss 'SetCategoryJunk' sein", error);
    }

    [Fact]
    public void TryNormalize_EmptyRules_ReturnsSuccess()
    {
        var json = """{ "enabled": true, "rules": [] }""";

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out var preview, out _);

        Assert.True(result);
        Assert.Empty(doc.Rules);
        Assert.Contains("Keine Regeln definiert", preview);
    }

    [Fact]
    public void TryNormalize_NullRuleInArray_TreatedAsEmpty()
    {
        var json = """
        {
            "rules": [
                null,
                { "field": "name", "operator": "contains", "value": "test", "action": "SetCategoryJunk" }
            ]
        }
        """;

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out _, out _, out var error);

        // null entry becomes empty object with blank fields -> field validation fails
        Assert.False(result);
        Assert.Contains("Regel 1", error);
    }

    // ═══ TryValidateCustomRegexPattern ════════════════════════════════

    [Fact]
    public void TryValidateRegex_ValidPattern_ReturnsTrue()
    {
        var result = FeatureCommandService.TryValidateCustomRegexPattern(
            @"^\(Beta\)$", out var error);

        Assert.True(result);
        Assert.Empty(error);
    }

    [Fact]
    public void TryValidateRegex_InvalidPattern_ReturnsFalse()
    {
        var result = FeatureCommandService.TryValidateCustomRegexPattern(
            @"[unclosed", out var error);

        Assert.False(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateRegex_EmptyPattern_ReturnsTrue()
    {
        var result = FeatureCommandService.TryValidateCustomRegexPattern(
            "", out _);

        Assert.True(result);
    }

    // ═══ BuildDefaultCustomJunkRulesJson ══════════════════════════════

    [Fact]
    public void BuildDefault_ReturnsValidJson()
    {
        var json = FeatureCommandService.BuildDefaultCustomJunkRulesJson();

        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Contains("\"enabled\"", json);
        Assert.Contains("\"rules\"", json);
        Assert.Contains("(Beta)", json);
        Assert.Contains("SetCategoryJunk", json);
    }

    [Fact]
    public void BuildDefault_CanBeNormalized()
    {
        var json = FeatureCommandService.BuildDefaultCustomJunkRulesJson();

        var result = FeatureCommandService.TryNormalizeCustomJunkRules(
            json, out var doc, out _, out _);

        Assert.True(result);
        Assert.True(doc.Enabled);
        Assert.Single(doc.Rules);
    }

    // ═══ BuildCustomJunkRulesPreview ══════════════════════════════════

    [Fact]
    public void Preview_ShowsEnabledStatus()
    {
        var doc = new FeatureCommandService.CustomJunkRulesDocument
        {
            Enabled = true,
            Rules =
            [
                new FeatureCommandService.CustomJunkRuleEntry
                {
                    Field = "name",
                    Operator = "contains",
                    Value = "(Beta)",
                    Logic = "AND",
                    Priority = 100,
                    Enabled = true
                }
            ]
        };

        var preview = FeatureCommandService.BuildCustomJunkRulesPreview(doc);

        Assert.Contains("Aktiviert: Ja", preview);
        Assert.Contains("Regeln: 1", preview);
        Assert.Contains("[1] aktiv", preview);
        Assert.Contains("name contains \"(Beta)\"", preview);
        Assert.Contains("Logic=AND", preview);
        Assert.Contains("Priority=100", preview);
    }

    [Fact]
    public void Preview_DisabledDocument_ShowsNein()
    {
        var doc = new FeatureCommandService.CustomJunkRulesDocument
        {
            Enabled = false,
            Rules = []
        };

        var preview = FeatureCommandService.BuildCustomJunkRulesPreview(doc);

        Assert.Contains("Aktiviert: Nein", preview);
        Assert.Contains("Keine Regeln definiert", preview);
    }

    [Fact]
    public void Preview_InactiveRule_ShowsInaktiv()
    {
        var doc = new FeatureCommandService.CustomJunkRulesDocument
        {
            Enabled = true,
            Rules =
            [
                new FeatureCommandService.CustomJunkRuleEntry
                {
                    Field = "region", Operator = "equals", Value = "Japan",
                    Enabled = false
                }
            ]
        };

        var preview = FeatureCommandService.BuildCustomJunkRulesPreview(doc);

        Assert.Contains("[1] inaktiv", preview);
    }

    // ═══ MapToRuleEngineField ═════════════════════════════════════════

    [Theory]
    [InlineData("name", "Name")]
    [InlineData("region", "Region")]
    [InlineData("extension", "Extension")]
    [InlineData("path", "Path")]
    [InlineData("unknown", "unknown")]
    public void MapField_MapsCorrectly(string input, string expected)
    {
        var result = FeatureCommandService.MapToRuleEngineField(input);
        Assert.Equal(expected, result);
    }

    // ═══ NormalizeProfileId ═══════════════════════════════════════════

    [Fact]
    public void NormalizeProfileId_AlphanumericName_Preserved()
    {
        var result = FeatureCommandService.NormalizeProfileId("MyProfile123");
        Assert.Equal("MyProfile123", result);
    }

    [Fact]
    public void NormalizeProfileId_SpacesConvertedToDashes()
    {
        var result = FeatureCommandService.NormalizeProfileId("My Cool Profile");
        Assert.Equal("My-Cool-Profile", result);
    }

    [Fact]
    public void NormalizeProfileId_SpecialCharsFiltered()
    {
        var result = FeatureCommandService.NormalizeProfileId("test@#$%^&!profile");
        Assert.Equal("testprofile", result);
    }

    [Fact]
    public void NormalizeProfileId_DotsAndUnderscoresPreserved()
    {
        var result = FeatureCommandService.NormalizeProfileId("my.profile_v2");
        Assert.Equal("my.profile_v2", result);
    }

    [Fact]
    public void NormalizeProfileId_EmptyOrWhitespace_ReturnsDefault()
    {
        Assert.Equal("custom-profile", FeatureCommandService.NormalizeProfileId(""));
        Assert.Equal("custom-profile", FeatureCommandService.NormalizeProfileId("   "));
        Assert.Equal("custom-profile", FeatureCommandService.NormalizeProfileId("---"));
    }

    [Fact]
    public void NormalizeProfileId_LongName_TruncatedTo64()
    {
        var longName = new string('a', 100);
        var result = FeatureCommandService.NormalizeProfileId(longName);
        Assert.Equal(64, result.Length);
    }

    [Fact]
    public void NormalizeProfileId_LeadingTrailingPunctuation_Trimmed()
    {
        var result = FeatureCommandService.NormalizeProfileId("--my-profile--");
        Assert.Equal("my-profile", result);
    }

    // ═══ BuildRunSnapshotChoicePrompt ═════════════════════════════════

    [Fact]
    public void BuildSnapshotPrompt_ShowsSnapshots()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot
            {
                RunId = "run-001",
                CompletedUtc = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc),
                Mode = "DryRun",
                Status = "ok"
            },
            new CollectionRunSnapshot
            {
                RunId = "run-002",
                CompletedUtc = new DateTime(2025, 6, 14, 10, 0, 0, DateTimeKind.Utc),
                Mode = "Execute",
                Status = "completed_with_errors"
            }
        };

        var prompt = FeatureCommandService.BuildRunSnapshotChoicePrompt(snapshots);

        Assert.Contains("run-001", prompt);
        Assert.Contains("run-002", prompt);
        Assert.Contains("2025-06-15 14:30", prompt);
        Assert.Contains("DryRun", prompt);
        Assert.Contains("Execute", prompt);
        Assert.Contains("Neueste Snapshots", prompt);
    }

    [Fact]
    public void BuildSnapshotPrompt_LimitsFive()
    {
        var snapshots = Enumerable.Range(1, 8)
            .Select(i => new CollectionRunSnapshot
            {
                RunId = $"run-{i:D3}",
                CompletedUtc = DateTime.UtcNow,
                Mode = "DryRun",
                Status = "ok"
            }).ToArray();

        var prompt = FeatureCommandService.BuildRunSnapshotChoicePrompt(snapshots);

        Assert.Contains("run-005", prompt);
        Assert.DoesNotContain("run-006", prompt);
    }

    // ═══ ResolveComparisonPair ════════════════════════════════════════

    [Fact]
    public void ResolveComparison_NullInput_FallsBackToFirstTwo()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot { RunId = "first" },
            new CollectionRunSnapshot { RunId = "second" }
        };

        var pair = FeatureCommandService.ResolveComparisonPair(null, snapshots);

        Assert.Equal(2, pair.Count);
        Assert.Equal("first", pair[0]);
        Assert.Equal("second", pair[1]);
    }

    [Fact]
    public void ResolveComparison_WhitespaceInput_FallsBackToFirstTwo()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot { RunId = "a" },
            new CollectionRunSnapshot { RunId = "b" }
        };

        var pair = FeatureCommandService.ResolveComparisonPair("   ", snapshots);

        Assert.Equal("a", pair[0]);
        Assert.Equal("b", pair[1]);
    }

    [Fact]
    public void ResolveComparison_TwoIds_ParsedCorrectly()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot { RunId = "fallback1" },
            new CollectionRunSnapshot { RunId = "fallback2" }
        };

        var pair = FeatureCommandService.ResolveComparisonPair("run-A run-B", snapshots);

        Assert.Equal("run-A", pair[0]);
        Assert.Equal("run-B", pair[1]);
    }

    [Fact]
    public void ResolveComparison_SemicolonDelimited()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot { RunId = "f1" },
            new CollectionRunSnapshot { RunId = "f2" }
        };

        var pair = FeatureCommandService.ResolveComparisonPair("x;y", snapshots);

        Assert.Equal("x", pair[0]);
        Assert.Equal("y", pair[1]);
    }

    [Fact]
    public void ResolveComparison_SingleId_FallsBackToFirstTwo()
    {
        var snapshots = new[]
        {
            new CollectionRunSnapshot { RunId = "a" },
            new CollectionRunSnapshot { RunId = "b" }
        };

        var pair = FeatureCommandService.ResolveComparisonPair("single-id", snapshots);

        Assert.Equal("a", pair[0]);
        Assert.Equal("b", pair[1]);
    }

    // ═══ FormatFrontendExportSummary ══════════════════════════════════

    [Fact]
    public void FormatExportSummary_ShowsAllFields()
    {
        var result = new FrontendExportResult(
            "emulationstation",
            "C:\\Roms",
            42,
            [
                new FrontendExportArtifact("C:\\Out\\gamelist.xml", "GameList", 42),
                new FrontendExportArtifact("C:\\Out\\media", "Media", 10)
            ]);

        var text = FeatureCommandService.FormatFrontendExportSummary(result);

        Assert.Contains("emulationstation", text);
        Assert.Contains("C:\\Roms", text);
        Assert.Contains("42", text);
        Assert.Contains("GameList", text);
        Assert.Contains("Media", text);
    }

    [Fact]
    public void FormatExportSummary_EmptyArtifacts()
    {
        var result = new FrontendExportResult("mister", "D:\\Games", 0, []);

        var text = FeatureCommandService.FormatFrontendExportSummary(result);

        Assert.Contains("mister", text);
        Assert.Contains("Spiele: 0", text);
    }

    // ═══ FormatCollectionMergePlan ════════════════════════════════════

    [Fact]
    public void FormatMergePlan_ShowsSummaryAndEntries()
    {
        var plan = new CollectionMergePlan
        {
            Request = new CollectionMergeRequest
            {
                TargetRoot = "C:\\Target",
                AllowMoves = true
            },
            Summary = new CollectionMergePlanSummary(10, 5, 2, 1, 1, 1, 0, 7),
            Entries =
            [
                new CollectionMergePlanEntry
                {
                    Decision = CollectionMergeDecision.CopyToTarget,
                    DiffKey = "game-a",
                    TargetPath = "C:\\Target\\game-a.zip",
                    ReasonCode = "missing-in-target"
                }
            ]
        };

        var text = FeatureCommandService.FormatCollectionMergePlan(plan);

        Assert.Contains("C:\\Target", text);
        Assert.Contains("AllowMoves: True", text);
        Assert.Contains("Total: 10", text);
        Assert.Contains("Copy: 5", text);
        Assert.Contains("Move: 2", text);
        Assert.Contains("[CopyToTarget] game-a", text);
    }

    [Fact]
    public void FormatMergePlan_MoreThan25_ShowsTruncation()
    {
        var entries = Enumerable.Range(1, 30)
            .Select(i => new CollectionMergePlanEntry
            {
                Decision = CollectionMergeDecision.CopyToTarget,
                DiffKey = $"game-{i}",
                TargetPath = $"C:\\T\\game-{i}.zip",
                ReasonCode = "missing"
            }).ToList();

        var plan = new CollectionMergePlan
        {
            Request = new CollectionMergeRequest { TargetRoot = "C:\\T" },
            Summary = new CollectionMergePlanSummary(30, 30, 0, 0, 0, 0, 0, 30),
            Entries = entries
        };

        var text = FeatureCommandService.FormatCollectionMergePlan(plan);

        Assert.Contains("... und 5 weitere Eintraege", text);
    }

    // ═══ FormatCollectionMergeApply ═══════════════════════════════════

    [Fact]
    public void FormatMergeApply_ShowsSummaryAndEntries()
    {
        var result = new CollectionMergeApplyResult
        {
            Summary = new CollectionMergeApplySummary(5, 4, 3, 1, 0, 0, 0, 0, 1),
            AuditPath = "C:\\Audit\\log.json",
            Entries =
            [
                new CollectionMergeApplyEntryResult
                {
                    Outcome = CollectionMergeApplyOutcome.Applied,
                    DiffKey = "game-x",
                    TargetPath = "C:\\T\\game-x.zip",
                    ReasonCode = "copied"
                }
            ]
        };

        var text = FeatureCommandService.FormatCollectionMergeApply(result);

        Assert.Contains("Applied: 4", text);
        Assert.Contains("Copied: 3", text);
        Assert.Contains("Moved: 1", text);
        Assert.Contains("Failed: 1", text);
        Assert.Contains("Audit: C:\\Audit\\log.json", text);
        Assert.Contains("[Applied] game-x", text);
    }

    [Fact]
    public void FormatMergeApply_NoAuditPath_OmitsAuditLine()
    {
        var result = new CollectionMergeApplyResult
        {
            Summary = new CollectionMergeApplySummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
            AuditPath = "",
            Entries = []
        };

        var text = FeatureCommandService.FormatCollectionMergeApply(result);

        Assert.DoesNotContain("Audit:", text);
    }

    [Fact]
    public void FormatMergeApply_MoreThan25_ShowsTruncation()
    {
        var entries = Enumerable.Range(1, 30)
            .Select(i => new CollectionMergeApplyEntryResult
            {
                Outcome = CollectionMergeApplyOutcome.Applied,
                DiffKey = $"g-{i}",
                TargetPath = $"C:\\T\\g-{i}.zip",
                ReasonCode = "c"
            }).ToList();

        var result = new CollectionMergeApplyResult
        {
            Summary = new CollectionMergeApplySummary(30, 30, 30, 0, 0, 0, 0, 0, 0),
            Entries = entries
        };

        var text = FeatureCommandService.FormatCollectionMergeApply(result);

        Assert.Contains("... und 5 weitere Eintraege", text);
    }

    // ═══ ResolveRecommendedWizardWorkflow ═════════════════════════════

    [Fact]
    public void WizardWorkflow_DiscOnlyNoCart_ReturnsFormatOptimization()
    {
        var result = MainViewModel.ResolveRecommendedWizardWorkflow(
            hasDiscLikeFormats: true, hasCartridgeFormats: false, estimatedSavingsBytes: 0);

        Assert.Equal(WorkflowScenarioIds.FormatOptimization, result);
    }

    [Fact]
    public void WizardWorkflow_DiscAndCart_ReturnsNewCollectionSetup()
    {
        var result = MainViewModel.ResolveRecommendedWizardWorkflow(
            hasDiscLikeFormats: true, hasCartridgeFormats: true, estimatedSavingsBytes: 0);

        Assert.Equal(WorkflowScenarioIds.NewCollectionSetup, result);
    }

    [Fact]
    public void WizardWorkflow_CartOnlyWithSavings_ReturnsQuickClean()
    {
        var result = MainViewModel.ResolveRecommendedWizardWorkflow(
            hasDiscLikeFormats: false, hasCartridgeFormats: true, estimatedSavingsBytes: 1024);

        Assert.Equal(WorkflowScenarioIds.QuickClean, result);
    }

    [Fact]
    public void WizardWorkflow_CartOnlyNoSavings_ReturnsFullAudit()
    {
        var result = MainViewModel.ResolveRecommendedWizardWorkflow(
            hasDiscLikeFormats: false, hasCartridgeFormats: true, estimatedSavingsBytes: 0);

        Assert.Equal(WorkflowScenarioIds.FullAudit, result);
    }

    [Fact]
    public void WizardWorkflow_NeitherFormat_ReturnsFullAudit()
    {
        var result = MainViewModel.ResolveRecommendedWizardWorkflow(
            hasDiscLikeFormats: false, hasCartridgeFormats: false, estimatedSavingsBytes: 5000);

        Assert.Equal(WorkflowScenarioIds.FullAudit, result);
    }

    // ═══ NormalizeSelection ═══════════════════════════════════════════

    [Fact]
    public void NormalizeSelection_Null_ReturnsNull()
    {
        Assert.Null(MainViewModel.NormalizeSelection(null));
    }

    [Fact]
    public void NormalizeSelection_Empty_ReturnsNull()
    {
        Assert.Null(MainViewModel.NormalizeSelection(""));
        Assert.Null(MainViewModel.NormalizeSelection("  "));
    }

    [Fact]
    public void NormalizeSelection_WithWhitespace_Trims()
    {
        Assert.Equal("abc", MainViewModel.NormalizeSelection("  abc  "));
    }

    [Fact]
    public void NormalizeSelection_NormalValue_PreservedTrimmed()
    {
        Assert.Equal("my-value", MainViewModel.NormalizeSelection("my-value"));
    }
}
