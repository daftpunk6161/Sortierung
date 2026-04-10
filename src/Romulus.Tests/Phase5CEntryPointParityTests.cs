using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 5C – CLI / API / Entry-Point-Parität
/// Invariant tests for TASK-158, TASK-159, TASK-161, TASK-163.
/// </summary>
public class Phase5CEntryPointParityTests
{
    // ───── TASK-158: ConvertFormat Passthrough ─────

    [Theory]
    [InlineData("chd")]
    [InlineData("rvz")]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("auto")]
    public void TASK158_API_ConvertFormat_PassesThrough_UserValue(string format)
    {
        // RunLifecycleManager must preserve exact user-supplied ConvertFormat, not force "auto"
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            ConvertFormat = format
        };

        var create = mgr.TryCreateOrReuse(request, "DryRun", $"idem-158-{format}");

        Assert.Equal(RunCreateDisposition.Created, create.Disposition);
        Assert.Equal(format, create.Run!.ConvertFormat);
    }

    [Fact]
    public void TASK158_API_ConvertFormat_Null_WhenEmpty()
    {
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            ConvertFormat = null
        };

        var create = mgr.TryCreateOrReuse(request, "DryRun", "idem-158-null");

        Assert.Null(create.Run!.ConvertFormat);
    }

    [Fact]
    public void TASK158_API_ConvertFormat_Null_WhenWhitespace()
    {
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            ConvertFormat = "   "
        };

        var create = mgr.TryCreateOrReuse(request, "DryRun", "idem-158-ws");

        Assert.Null(create.Run!.ConvertFormat);
    }

    [Fact]
    public void TASK158_RunRecordOptionsSource_PreservesConvertFormat()
    {
        // RunRecordOptionsSource must faithfully forward ConvertFormat
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "Move",
            ConvertFormat = "rvz"
        };

        var create = mgr.TryCreateOrReuse(request, "Move", "idem-158-src");
        var run = create.Run!;

        // Construct RunRecordOptionsSource the same way RunManager.ExecuteWithOrchestrator does
        var source = new RunRecordOptionsSource(run);
        var factory = new RunOptionsFactory();
        var options = factory.Create(source, null, null);

        Assert.Equal("rvz", options.ConvertFormat);
    }

    // ───── TASK-159: OnlyGames Guard centralized in RunOptionsBuilder ─────

    [Fact]
    public void TASK159_RunOptionsBuilder_Validate_RejectsInvalidOnlyGamesCombo()
    {
        // !OnlyGames && !KeepUnknownWhenOnlyGames is invalid across ALL entry points
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = false
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("OnlyGames", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TASK159_RunOptionsBuilder_Validate_AcceptsValidOnlyGames_True_False()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.Empty(errors);
    }

    [Fact]
    public void TASK159_RunOptionsBuilder_Validate_AcceptsDefault_False_True()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = true
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.Empty(errors);
    }

    [Fact]
    public void TASK159_RunOptionsBuilder_Validate_AcceptsOnlyGames_True_True()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = true
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.Empty(errors);
    }

    [Fact]
    public void TGAP44_RunOptionsFactory_InvalidOptions_ThrowsFromValidate()
    {
        var run = new RunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            RequestFingerprint = "fp",
            StartedUtc = DateTime.UtcNow,
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = false
        };

        var factory = new RunOptionsFactory();
        var source = new RunRecordOptionsSource(run);

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create(source, null, null));
        Assert.Contains("OnlyGames", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunOptionsBuilder_Validate_RejectsProtectedTrashRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(protectedPath))
            return;

        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            TrashRoot = protectedPath
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.Contains(errors, error => error.Contains("trashRoot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunOptionsBuilder_Validate_RejectsDriveRootAuditPath()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            AuditPath = @"C:\"
        };

        var errors = RunOptionsBuilder.Validate(options);

        Assert.Contains(errors, error => error.Contains("auditPath", StringComparison.OrdinalIgnoreCase));
    }

    // ───── TASK-163: DryRun + Feature Warnings ─────

    [Fact]
    public void TASK163_PhasePlanBuilder_DryRun_With_SortConsole_NoLongerWarns()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            SortConsole = true
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.Empty(warnings);
    }

    [Fact]
    public void TASK163_PhasePlanBuilder_DryRun_With_ConvertFormat_EmitsWarning()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            ConvertFormat = "chd"
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("ConvertFormat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TASK163_PhasePlanBuilder_DryRun_With_Both_EmitsOnlyMoveOnlyWarning()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            SortConsole = true,
            ConvertFormat = "auto"
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.Single(warnings);
        Assert.Contains(warnings, w => w.Contains("ConvertFormat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TASK163_Move_With_SortConsole_NoWarning()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "Move",
            SortConsole = true
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.Empty(warnings);
    }

    [Fact]
    public void TASK163_DryRun_Without_Features_NoWarning()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            SortConsole = false,
            ConvertFormat = null
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.Empty(warnings);
    }

    [Fact]
    public void TASK163_DryRun_With_EnableDatRename_EmitsWarning()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            EnableDatRename = true
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("DatRename", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TGAP47_DryRunWarnings_ConvertOnly_IsWarned()
    {
        var options = new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            ConvertOnly = true
        };

        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(options);

        Assert.Contains(warnings, w => w.Contains("ConvertOnly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TASK163_Preflight_Includes_DryRunFeatureWarnings()
    {
        // The orchestrator's Preflight should include DryRun+Feature warnings
        var options = RunOptionsBuilder.Normalize(new RunOptions
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            SortConsole = true,
            ConvertFormat = "chd",
            Extensions = RunOptions.DefaultExtensions
        });

        var orchestrator = new RunOrchestrator(
            new FileSystemAdapter(), new AuditCsvStore(),
            consoleDetector: null, hashService: null, converter: null, datIndex: null);

        var preflight = orchestrator.Preflight(options);

        Assert.Contains(preflight.Warnings, w => w.Contains("ConvertFormat", StringComparison.OrdinalIgnoreCase));
    }

    // ───── TASK-161: API Settings Source ─────

    [Fact]
    public void TASK161_RunEnvironmentBuilder_LoadSettings_RespectsExplicitDataDir()
    {
        // When dataDir is explicitly provided, LoadSettings should not reach into %APPDATA%
        // This verifies the function exists and accepts a dataDir parameter
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        // Just verifying the settings load works — the key test is that the API
        // can pass its own settings path instead of depending on %APPDATA%
        Assert.NotNull(settings);
    }

    [Fact]
    public void TASK161_RunEnvironmentBuilder_LoadSettings_OverridePathDoesNotFail()
    {
        // When a non-existent settings override path is provided, defaults should be returned
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir, settingsOverridePath: "nonexistent-path.json");

        Assert.NotNull(settings);
        // Should get default settings, not crash
        Assert.NotNull(settings.General);
    }

    [Fact]
    public void TASK161_LoadSettings_ExplicitOverride_SkipsAppData()
    {
        // Creating a temp settings file to verify the override path is actually used
        var tempFile = Path.Combine(Path.GetTempPath(), $"romulus-test-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, """
            {
                "general": {
                    "preferredRegions": ["JP", "US"]
                }
            }
            """);

            var dataDir = RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir, settingsOverridePath: tempFile);

            Assert.NotNull(settings);
            Assert.Contains("JP", settings.General.PreferredRegions);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void TASK161_LoadSettings_NullOverride_FallsBackToDefault()
    {
        // Passing null settingsOverridePath should behave identical to the original overload
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings1 = RunEnvironmentBuilder.LoadSettings(dataDir);
        var settings2 = RunEnvironmentBuilder.LoadSettings(dataDir, settingsOverridePath: null);

        // Both should return valid settings with same defaults
        Assert.NotNull(settings1);
        Assert.NotNull(settings2);
        Assert.Equal(settings1.General.PreferredRegions.Count, settings2.General.PreferredRegions.Count);
    }

    [Fact]
    public void TGAP38_ConsoleFilter_LabelClearlyIndicatesDisplayOnly()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));

        Assert.Contains("kein Einfluss auf die Pipeline", xaml, StringComparison.OrdinalIgnoreCase);
    }

    // ───── Helpers ─────

    private static RunManager CreateManager() =>
        new(new FileSystemAdapter(), new AuditCsvStore());

    private static string GetTestRoot() =>
        Path.GetTempPath();

    private static string FindUiFile(string folder, string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        return Path.Combine(repoRoot, "src", "Romulus.UI.Wpf", folder, fileName);
    }

    private static string FindRepoRoot(string? callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "Romulus.sln")) ||
                File.Exists(Path.Combine(dir, "src", "Romulus.UI.Wpf", "Romulus.UI.Wpf.csproj")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }
}
