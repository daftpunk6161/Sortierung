using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave4RegressionTests : IDisposable
{
    private readonly string _tempDir;

    public Wave4RegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Wave4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void W4_SettingsLoader_InvalidGeneralUserSection_RevertsGeneralButKeepsValidDatChanges()
    {
        var defaultsPath = Path.Combine(_tempDir, "defaults.json");
        File.WriteAllText(defaultsPath, """
        {
          "logLevel": "Info",
          "preferredRegions": ["EU"],
          "dat": {
            "useDat": true,
            "hashType": "SHA1",
            "datFallback": true
          }
        }
        """);

        var userPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(userPath, """
        {
          "general": {
            "preferredRegions": ["EU", "BAD!"]
          },
          "dat": {
            "useDat": false,
            "hashType": "SHA256"
          }
        }
        """);

        var warnings = new List<string>();
        var settings = SettingsLoader.LoadWithExplicitUserPath(
            defaultsPath,
            userPath,
            warnings.Add);

        Assert.Equal(new[] { "EU" }, settings.General.PreferredRegions);
        Assert.False(settings.Dat.UseDat);
        Assert.Equal("SHA256", settings.Dat.HashType);
        Assert.Contains(warnings, warning => warning.Contains("reverting affected sections", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void W4_RunReportWriter_SkippedCount_AlsoIncludesDatRenameSkippedCount()
    {
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            TotalFilesScanned = 1,
            AllCandidates =
            [
                new RomCandidate
                {
                    MainPath = @"C:\roms\game.chd",
                    Category = FileCategory.Game,
                    GameKey = "game"
                }
            ],
            DatRenameSkippedCount = 2,
            ConvertSkippedCount = 0,
            ConvertBlockedCount = 0,
            MoveResult = new MovePhaseResult(MoveCount: 0, FailCount: 0, SavedBytes: 0, SkipCount: 0)
        };

        var summary = RunReportWriter.BuildSummary(result, RunConstants.ModeDryRun);

        Assert.Equal(2, summary.SkippedCount);
    }

    [Fact]
    public void W4_AuditSigningService_NoSidecarAndNoRows_DoesNotInventFailureCount()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var service = new AuditSigningService(new FileSystemAdapter());
        var result = service.Rollback(auditPath, [_tempDir], [_tempDir], dryRun: true);

        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void W4_PhasePlanExecutor_WhenPhaseThrows_SetsFailedPhaseBeforeRethrow()
    {
        var state = new PipelineState();
        var phasePlan = new IPhaseStep[]
        {
            new ThrowingPhaseStep("Deduplicate", new InvalidOperationException("boom"))
        };

        var sut = new PhasePlanExecutor(onProgress: null);

        Assert.Throws<InvalidOperationException>(() => sut.Execute(phasePlan, state, CancellationToken.None));
        Assert.Equal("Deduplicate", state.FailedPhaseName);
        Assert.Equal(RunConstants.StatusFailed, state.FailedPhaseStatus);
    }

    [Fact]
    public void W4_MainViewModel_ApplyMaterializedRunConfiguration_PreservesExplicitConvertFormatInDraft()
    {
        var vm = new MainViewModel();

        var materialized = new MaterializedRunConfiguration(
            EffectiveDraft: new RunConfigurationDraft
            {
                Mode = RunConstants.ModeMove,
                ConvertFormat = "chd",
                ConvertOnly = false
            },
            Workflow: null,
            Profile: null,
            EffectiveProfileId: null,
            Options: new RunOptions());

        vm.ApplyMaterializedRunConfiguration(materialized);

        var draft = vm.BuildCurrentRunConfigurationDraft(includeSelections: false);
        Assert.Equal("chd", draft.ConvertFormat);
    }

    [Fact]
    public void W4_RunMoveStep_UsesAllGroupsAsMoveInput()
    {
        var source = ReadSource("Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs");

        Assert.Contains("var groups = state.AllGroups", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var groups = state.GameGroups", source, StringComparison.Ordinal);
    }

    [Fact]
    public void W4_SettingsService_Load_UsesCentralSafeSettingsLoader()
    {
        var source = ReadSource("Romulus.UI.Wpf/Services/SettingsService.cs");

        Assert.Contains("SettingsLoader.LoadFromSafe", source, StringComparison.Ordinal);
    }

    [Fact]
    public void W4_ProfileService_Import_UsesCentralSafeSettingsLoaderValidation()
    {
        var source = ReadSource("Romulus.UI.Wpf/Services/ProfileService.cs");

        Assert.Contains("SettingsLoader.LoadFromSafe", source, StringComparison.Ordinal);
    }

    [Fact]
    public void W4_FeatureCommandService_CliCopy_EmitsConvertFormatValue()
    {
        var source = ReadSource("Romulus.UI.Wpf/Services/FeatureCommandService.cs");

        Assert.Contains("--convertformat {draft.ConvertFormat}", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativeFromSrc)
    {
        var srcDir = FindSrcDirectory();
        var fullPath = Path.Combine(srcDir, relativeFromSrc.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static string FindSrcDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "Romulus.Infrastructure")))
            dir = Directory.GetParent(dir)?.FullName;

        return dir ?? throw new DirectoryNotFoundException("Could not locate src directory.");
    }

    private sealed class ThrowingPhaseStep : IPhaseStep
    {
        private readonly Exception _exception;

        public ThrowingPhaseStep(string name, Exception exception)
        {
            Name = name;
            _exception = exception;
        }

        public string Name { get; }

        public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }
}
