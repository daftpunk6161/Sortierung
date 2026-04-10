using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Workflow;
using Xunit;

namespace Romulus.Tests;

public sealed class RunConfigurationResolverRegressionTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly RunConfigurationResolver _resolver;
  private readonly RunProfileService _profileService;

    public RunConfigurationResolverRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_RunConfigResolver_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var dataDir = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, RunProfilePaths.BuiltInProfilesFileName), BuiltInProfilesJson);

        var profileStore = new JsonRunProfileStore(new RunProfilePathOptions
        {
            DirectoryPath = Path.Combine(_tempRoot, "profiles")
        });
        _profileService = new RunProfileService(profileStore, dataDir);
        _resolver = new RunConfigurationResolver(_profileService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void WorkflowScenarioCatalog_List_ContainsAllRequiredR2Scenarios()
    {
        var ids = WorkflowScenarioCatalog.List().Select(static scenario => scenario.Id).ToArray();

        Assert.Contains(WorkflowScenarioIds.QuickClean, ids);
        Assert.Contains(WorkflowScenarioIds.FullAudit, ids);
        Assert.Contains(WorkflowScenarioIds.DatVerification, ids);
        Assert.Contains(WorkflowScenarioIds.FormatOptimization, ids);
        Assert.Contains(WorkflowScenarioIds.NewCollectionSetup, ids);
    }

    [Fact]
    public async Task RunConfigurationResolver_AppliesWorkflowDefaults_AndRecommendedProfile()
    {
        var draft = new RunConfigurationDraft
        {
            Roots = [@"C:\Roms"],
            WorkflowScenarioId = WorkflowScenarioIds.FullAudit
        };

        var resolved = await _resolver.ResolveAsync(draft, new RunConfigurationExplicitness());

        Assert.NotNull(resolved.Workflow);
        Assert.Equal(WorkflowScenarioIds.FullAudit, resolved.Workflow!.Id);
        Assert.Equal("default", resolved.EffectiveProfileId);
        Assert.NotNull(resolved.Profile);
        Assert.Equal("default", resolved.Profile!.Id);
        Assert.Equal(RunConstants.ModeDryRun, resolved.Draft.Mode);
        Assert.True(resolved.Draft.RemoveJunk);
        Assert.True(resolved.Draft.SortConsole);
        Assert.True(resolved.Draft.EnableDat);
        Assert.True(resolved.Draft.EnableDatAudit);
    }

    [Fact]
    public async Task RunConfigurationResolver_ExplicitValues_OverrideWorkflowDefaults()
    {
        var draft = new RunConfigurationDraft
        {
            Roots = [@"C:\Roms"],
            WorkflowScenarioId = WorkflowScenarioIds.QuickClean,
            Mode = RunConstants.ModeMove,
            RemoveJunk = false,
            SortConsole = false
        };

        var explicitness = new RunConfigurationExplicitness
        {
            Mode = true,
            RemoveJunk = true,
            SortConsole = true
        };

        var resolved = await _resolver.ResolveAsync(draft, explicitness);

        Assert.Equal(RunConstants.ModeMove, resolved.Draft.Mode);
        Assert.False(resolved.Draft.RemoveJunk);
        Assert.False(resolved.Draft.SortConsole);
    }

    [Fact]
    public async Task RunConfigurationResolver_UnknownWorkflow_ThrowsInvalidOperationException()
    {
        var draft = new RunConfigurationDraft
        {
            Roots = [@"C:\Roms"],
            WorkflowScenarioId = "workflow-does-not-exist"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _resolver.ResolveAsync(draft, new RunConfigurationExplicitness()));

        Assert.Contains("was not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunProfileService_LoadExternalAsync_InvalidProfile_IsRejected()
    {
        var profilePath = Path.Combine(_tempRoot, "invalid-profile.json");
        await File.WriteAllTextAsync(profilePath,
            """
            {
              "version": 1,
              "id": "unsafe-profile",
              "name": "Unsafe Profile",
              "description": "invalid import test",
              "builtIn": false,
              "tags": ["test"],
              "settings": {
                "mode": "DryRun",
                "trashRoot": "C:\\"
              }
            }
            """);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _profileService.LoadExternalAsync(profilePath));

        Assert.Contains("trashRoot", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

  [Fact]
  public async Task RunConfigurationMaterializer_WizardAndExpertInputs_MaterializeToEquivalentOptions()
  {
    var settings = new RomulusSettings
    {
      General = new GeneralSettings
      {
        Mode = RunConstants.ModeMove,
        PreferredRegions = ["JP", "US"],
        Extensions = ".zip,.7z",
        AggressiveJunk = true
      },
      Dat = new DatSettings
      {
        UseDat = false,
        DatRoot = @"C:\DatFallback",
        HashType = "CRC32"
      }
    };

    var materializer = new RunConfigurationMaterializer(_resolver);
    var baselineDraft = new RunConfigurationDraft
    {
      Roots = [@"C:\Roms"],
      Mode = RunConstants.ModeMove,
      PreferRegions = ["JP"],
      Extensions = [".zip"],
      RemoveJunk = false,
      OnlyGames = true,
      KeepUnknownWhenOnlyGames = false,
      AggressiveJunk = true,
      SortConsole = false,
      EnableDat = false,
      EnableDatAudit = false,
      EnableDatRename = false,
      DatRoot = @"C:\BaselineDat",
      HashType = "SHA256",
      ConvertFormat = null,
      ConvertOnly = false,
      ApproveReviews = true,
      ConflictPolicy = "Skip",
      TrashRoot = @"C:\BaselineTrash"
    };

    var wizardMaterialized = await materializer.MaterializeAsync(
      new RunConfigurationDraft
      {
        Roots = [@"C:\Roms"],
        WorkflowScenarioId = WorkflowScenarioIds.FullAudit
      },
      new RunConfigurationExplicitness(),
      settings,
      baselineDraft: baselineDraft);

    var expertMaterialized = await materializer.MaterializeAsync(
      wizardMaterialized.EffectiveDraft with
      {
        WorkflowScenarioId = null,
        ProfileId = null
      },
      BuildAllExplicitness(),
      settings);

    AssertRunOptionsEquivalent(wizardMaterialized.Options, expertMaterialized.Options);
  }

  private static RunConfigurationExplicitness BuildAllExplicitness()
    => new()
    {
      Mode = true,
      PreferRegions = true,
      Extensions = true,
      RemoveJunk = true,
      OnlyGames = true,
      KeepUnknownWhenOnlyGames = true,
      AggressiveJunk = true,
      SortConsole = true,
      EnableDat = true,
      EnableDatAudit = true,
      EnableDatRename = true,
      DatRoot = true,
      HashType = true,
      ConvertFormat = true,
      ConvertOnly = true,
      ApproveReviews = true,
      ConflictPolicy = true,
      TrashRoot = true
    };

  private static void AssertRunOptionsEquivalent(RunOptions expected, RunOptions actual)
  {
    Assert.Equal(expected.Roots, actual.Roots);
    Assert.Equal(expected.Mode, actual.Mode);
    Assert.Equal(expected.PreferRegions, actual.PreferRegions);
    Assert.Equal(expected.Extensions, actual.Extensions);
    Assert.Equal(expected.RemoveJunk, actual.RemoveJunk);
    Assert.Equal(expected.OnlyGames, actual.OnlyGames);
    Assert.Equal(expected.KeepUnknownWhenOnlyGames, actual.KeepUnknownWhenOnlyGames);
    Assert.Equal(expected.AggressiveJunk, actual.AggressiveJunk);
    Assert.Equal(expected.SortConsole, actual.SortConsole);
    Assert.Equal(expected.EnableDat, actual.EnableDat);
    Assert.Equal(expected.EnableDatAudit, actual.EnableDatAudit);
    Assert.Equal(expected.EnableDatRename, actual.EnableDatRename);
    Assert.Equal(expected.DatRoot, actual.DatRoot);
    Assert.Equal(expected.HashType, actual.HashType);
    Assert.Equal(expected.ConvertFormat, actual.ConvertFormat);
    Assert.Equal(expected.ConvertOnly, actual.ConvertOnly);
    Assert.Equal(expected.ApproveReviews, actual.ApproveReviews);
    Assert.Equal(expected.TrashRoot, actual.TrashRoot);
    Assert.Equal(expected.AuditPath, actual.AuditPath);
    Assert.Equal(expected.ReportPath, actual.ReportPath);
    Assert.Equal(expected.ConflictPolicy, actual.ConflictPolicy);
  }

    private const string BuiltInProfilesJson =
        """
        [
          {
            "version": 1,
            "id": "default",
            "name": "Default",
            "description": "Default profile",
            "builtIn": true,
            "tags": ["default"],
            "settings": {
              "mode": "DryRun",
              "removeJunk": true,
              "sortConsole": true,
              "hashType": "SHA1"
            }
          },
          {
            "version": 1,
            "id": "quick-scan",
            "name": "Quick Scan",
            "description": "Quick profile",
            "builtIn": true,
            "tags": ["quick"],
            "settings": {
              "mode": "DryRun",
              "removeJunk": false,
              "sortConsole": false,
              "hashType": "SHA1"
            }
          },
          {
            "version": 1,
            "id": "retro-purist",
            "name": "Retro Purist",
            "description": "Verification profile",
            "builtIn": true,
            "tags": ["verification"],
            "settings": {
              "mode": "DryRun",
              "enableDat": true,
              "enableDatAudit": true,
              "hashType": "SHA1"
            }
          },
          {
            "version": 1,
            "id": "space-saver",
            "name": "Space Saver",
            "description": "Conversion profile",
            "builtIn": true,
            "tags": ["conversion"],
            "settings": {
              "mode": "DryRun",
              "enableDat": true,
              "enableDatAudit": true,
              "convertFormat": "auto",
              "hashType": "SHA1"
            }
          }
        ]
        """;
}