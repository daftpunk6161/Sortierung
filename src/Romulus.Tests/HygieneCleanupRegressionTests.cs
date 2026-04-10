using System.Reflection;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Paths;
using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Regression tests for the release-clean hygiene cleanup.
/// Ensures removed dead code stays removed and centralized path logic works.
/// </summary>
public sealed class HygieneCleanupRegressionTests
{
    private static readonly Assembly WpfAssembly = typeof(Romulus.UI.Wpf.App).Assembly;

    // ═══ Dead wrapper services must stay removed ═══════════════════════

    [Theory]
    [InlineData("ICollectionService")]
    [InlineData("CollectionService")]
    [InlineData("IHealthAnalyzer")]
    [InlineData("HealthAnalyzer")]
    [InlineData("IConversionEstimator")]
    [InlineData("ConversionEstimator")]
    [InlineData("IExportService")]
    [InlineData("ExportService")]
    [InlineData("IDatManagementService")]
    [InlineData("DatManagementService")]
    [InlineData("IWorkflowService")]
    [InlineData("WorkflowService")]
    public void RemovedWrapperService_MustNotExistInWpfAssembly(string typeName)
    {
        var type = WpfAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == typeName);

        Assert.Null(type);
    }

    [Theory]
    [InlineData("MissionControlViewModel")]
    [InlineData("LibraryViewModel")]
    [InlineData("ConfigViewModel")]
    [InlineData("InspectorViewModel")]
    [InlineData("SystemViewModel")]
    public void RemovedAdditiveAreaViewModel_MustNotExistInWpfAssembly(string typeName)
    {
        var type = WpfAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == typeName);

        Assert.Null(type);
    }

    // ═══ ArtifactPathResolver.FindContainingRoot (centralized) ════════

    [Fact]
    public void FindContainingRoot_ReturnsMatchingRoot()
    {
        var roots = new List<string>
        {
            ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES"),
            ArtifactPathResolver.NormalizeRoot(@"C:\Games\NES")
        };

        var result = ArtifactPathResolver.FindContainingRoot(@"C:\Games\SNES\Mario.zip", roots);

        Assert.NotNull(result);
        Assert.EndsWith("SNES", result);
    }

    [Fact]
    public void FindContainingRoot_ReturnsNull_WhenNoRootMatches()
    {
        var roots = new List<string>
        {
            ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES")
        };

        var result = ArtifactPathResolver.FindContainingRoot(@"D:\Other\file.zip", roots);

        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_DoesNotMatchPartialName()
    {
        // "C:\Games\SNES" must NOT match "C:\Games\SNES-Hacks\file.zip"
        var roots = new List<string>
        {
            ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES")
        };

        var result = ArtifactPathResolver.FindContainingRoot(@"C:\Games\SNES-Hacks\file.zip", roots);

        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_IsCaseInsensitive()
    {
        var roots = new List<string>
        {
            ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES")
        };

        var result = ArtifactPathResolver.FindContainingRoot(@"c:\games\snes\mario.zip", roots);

        Assert.NotNull(result);
    }

    // ═══ NormalizeRoot consistency ════════════════════════════════════

    [Fact]
    public void NormalizeRoot_ProducesConsistentOutput()
    {
        var a = ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES\");
        var b = ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES");
        var c = ArtifactPathResolver.NormalizeRoot(@"C:\Games\SNES\\");

        Assert.Equal(a, b);
        Assert.Equal(b, c);
    }

    // ═══ Dead Result<T> type must stay removed ════════════════════════

    [Fact]
    public void RemovedResultT_MustNotExistInWpfAssembly()
    {
        // Result<T> was dead code (GUI-042 discriminated result, never used in production).
        // OperationResult from Contracts is the canonical result type.
        var resultType = WpfAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "Result`1");

        Assert.Null(resultType);
    }

    // ═══ FeatureCommandKeys completeness ══════════════════════════════

    [Fact]
    public void FeatureCommandKeys_AllConstantsAreNonEmpty()
    {
        var fields = typeof(FeatureCommandKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string));

        foreach (var field in fields)
        {
            var value = (string?)field.GetValue(null);
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"FeatureCommandKeys.{field.Name} must not be null or empty");
        }
    }

    [Theory]
    [InlineData(nameof(FeatureCommandKeys.ProfileShare))]
    [InlineData(nameof(FeatureCommandKeys.CliCommandCopy))]
    [InlineData(nameof(FeatureCommandKeys.SchedulerApply))]
    [InlineData(nameof(FeatureCommandKeys.SystemTray))]
    public void NewlyAddedCommandKeys_ExistInFeatureCommandKeys(string fieldName)
    {
        var field = typeof(FeatureCommandKeys).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
    }

    // ═══ Round 2: Dead methods must stay removed ══════════════════════

    [Theory]
    [InlineData("BuildCoverReport")]
    [InlineData("BuildFilterReport")]
    public void RemovedFeatureServiceMethods_MustNotExist(string methodName)
    {
        var featureServiceType = WpfAssembly.GetTypes()
            .First(t => t.Name == "FeatureService");
        var method = featureServiceType.GetMethod(methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.Null(method);
    }

    [Fact]
    public void RemovedInitFeatureCommands_MustNotExistOnMainViewModel()
    {
        var vmType = WpfAssembly.GetTypes()
            .First(t => t.Name == "MainViewModel");
        var method = vmType.GetMethod("InitFeatureCommands",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Null(method);
    }

    // ═══ Round 2: ToolPathValidator security checks ═══════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToolPathValidator_EmptyPath_ReturnsNullWithNoReason(string? path)
    {
        var (normalized, reason) = ToolPathValidator.Validate(path);
        Assert.Null(normalized);
        Assert.Null(reason);
    }

    [Fact]
    public void ToolPathValidator_NonExistentFile_RejectsWithReason()
    {
        var (normalized, reason) = ToolPathValidator.Validate(@"C:\NonExistent\tool.exe");
        Assert.Null(normalized);
        Assert.NotNull(reason);
        Assert.Contains("nicht gefunden", reason);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".dll")]
    [InlineData(".ps1")]
    [InlineData(".sh")]
    public void ToolPathValidator_DisallowedExtension_Rejects(string ext)
    {
        // Use a temp file with the wrong extension to test extension check
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-tool{ext}");
        try
        {
            File.WriteAllText(tempFile, "dummy");
            var (normalized, reason) = ToolPathValidator.Validate(tempFile);
            Assert.Null(normalized);
            Assert.NotNull(reason);
            Assert.Contains("nicht erlaubt", reason);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".bat")]
    [InlineData(".cmd")]
    public void ToolPathValidator_AllowedExtension_Accepts(string ext)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-tool{ext}");
        try
        {
            File.WriteAllText(tempFile, "dummy");
            var (normalized, reason) = ToolPathValidator.Validate(tempFile);
            Assert.NotNull(normalized);
            Assert.Null(reason);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ToolPathValidator_ValidateOrEmpty_ReturnsEmptyForInvalid()
    {
        var result = ToolPathValidator.ValidateOrEmpty(@"C:\NonExistent\tool.exe");
        Assert.Equal("", result);
    }

    // ═══ Round 2: Orphaned AllowedToolExtensions must stay removed ════

    [Fact]
    public void AllowedToolExtensions_MustNotExistInSettingsLoader()
    {
        var settingsLoaderType = typeof(Romulus.Infrastructure.Configuration.SettingsLoader);
        var field = settingsLoaderType.GetField("AllowedToolExtensions",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.Null(field);
    }

    // ═══ Shutdown hygiene: prevent zombie .NET Host processes ═════════

    [Fact]
    public void App_MustHave_SingleInstanceMutex()
    {
        var appType = WpfAssembly.GetTypes().First(t => t.Name == "App");
        var mutexField = appType.GetField("_singleInstanceMutex",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(mutexField);
        Assert.Equal(typeof(System.Threading.Mutex), mutexField!.FieldType);
    }

    [Fact]
    public void App_MustOverride_OnExit()
    {
        var appType = WpfAssembly.GetTypes().First(t => t.Name == "App");
        var onExit = appType.GetMethod("OnExit",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(onExit);
    }

    [Fact]
    public void MainWindow_SafeKillApiProcess_MustCallWaitForExit()
    {
        var codePath = FindMainWindowCodePath();
        var code = File.ReadAllText(codePath);
        Assert.Contains("WaitForExit", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CleanupResources_MustCallShutdown()
    {
        var codePath = FindMainWindowCodePath();
        var code = File.ReadAllText(codePath);
        Assert.Contains("Application.Current?.Shutdown()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_MustNotUseStaticDialogServiceForCloseConfirmation()
    {
        var codePath = FindMainWindowCodePath();
        var code = File.ReadAllText(codePath);
        Assert.DoesNotContain("DialogService.Confirm(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DatAuditViewModel_MustNotUseStaticDialogService()
    {
        var codePath = FindWpfViewModelPath("DatAuditViewModel.cs");
        var code = File.ReadAllText(codePath);
        Assert.DoesNotContain("DialogService.SaveFile(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModelRunPipeline_MustNotUseStaticDialogServiceForConversionReview()
    {
        var codePath = FindWpfViewModelPath("MainViewModel.RunPipeline.cs");
        var code = File.ReadAllText(codePath);
        Assert.DoesNotContain("DialogService.ConfirmConversionReview(", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ToolsDatView.xaml")]
    [InlineData("ToolsConversionView.xaml")]
    [InlineData("ToolsGameKeyLabView.xaml")]
    [InlineData("ToolsDatView.xaml.cs")]
    [InlineData("ToolsConversionView.xaml.cs")]
    [InlineData("ToolsGameKeyLabView.xaml.cs")]
    public void RetiredSpecialistToolViews_MustStayRemoved(string fileName)
    {
        var viewPath = FindWpfViewPath(fileName);

        Assert.False(File.Exists(viewPath), $"{fileName} should stay removed after navigation consolidation.");
    }

    [Fact]
    public void DecisionsView_MustNotUseTextChangedFilterCodeBehind()
    {
        var xamlPath = FindWpfViewPath("DecisionsView.xaml");
        var codeBehindPath = FindWpfViewPath("DecisionsView.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.DoesNotContain("TextChanged=\"OnSearchTextChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSearchTextChanged", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_MustUseRootsDragDropBehavior_InsteadOfViewHandlers()
    {
        var xamlPath = FindWpfViewPath("StartView.xaml");
        var codeBehindPath = FindWpfViewPath("StartView.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Contains("helpers:RootsDragDropHelper.Enabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Drop=\"OnHeroDrop\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DragEnter=\"OnHeroDragEnter\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DragLeave=\"OnHeroDragLeave\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnHeroDrop", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("OnHeroDragEnter", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("OnHeroDragLeave", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigOptionsView_MustUseRootsDragDropBehavior_InsteadOfCodeBehindHookup()
    {
        var xamlPath = FindWpfViewPath("ConfigOptionsView.xaml");
        var codeBehindPath = FindWpfViewPath("ConfigOptionsView.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Contains("helpers:RootsDragDropHelper.Enabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RootsDragDropHelper.OnDragEnter", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("RootsDragDropHelper.OnDrop", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DragEnter +=", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Drop +=", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandPaletteView_MustUseBoundCommands_InsteadOfInteractiveCodeBehindHandlers()
    {
        var xamlPath = FindWpfViewPath("CommandPaletteView.xaml");
        var codeBehindPath = FindWpfViewPath("CommandPaletteView.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.Contains("CommandPalette.CloseCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyBinding Key=\"Down\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseBinding Gesture=\"LeftDoubleClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseDown=\"OnBackdropClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewKeyDown=\"OnSearchKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseDoubleClick=\"OnResultDoubleClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnBackdropClick", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSearchKeyDown", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("OnResultDoubleClick", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_MustNotUseShortcutOverlayClickHandler()
    {
        var xamlPath = FindMainWindowCodePath().Replace("MainWindow.xaml.cs", "MainWindow.xaml", StringComparison.Ordinal);
        var codePath = FindMainWindowCodePath();
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.DoesNotContain("MouseDown=\"OnShortcutOverlayClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnShortcutOverlayClick", code, StringComparison.Ordinal);
        Assert.Contains("Shell.ToggleShortcutSheetCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppXaml_MustSetExplicitShutdownMode()
    {
        var xamlPath = FindMainWindowCodePath().Replace("MainWindow.xaml.cs", "App.xaml");
        var xaml = File.ReadAllText(xamlPath);
        Assert.Contains("ShutdownMode=\"OnExplicitShutdown\"", xaml, StringComparison.Ordinal);
    }

    // ── Round 3: RunConstants, Formatting, magic-string guards ──

    [Fact]
    public void RunConstants_ModeDryRun_IsCorrectValue()
        => Assert.Equal("DryRun", RunConstants.ModeDryRun);

    [Fact]
    public void RunConstants_ModeMove_IsCorrectValue()
        => Assert.Equal("Move", RunConstants.ModeMove);

    [Fact]
    public void RunConstants_ValidModes_ContainsBothModes()
    {
        Assert.Contains(RunConstants.ModeDryRun, RunConstants.ValidModes);
        Assert.Contains(RunConstants.ModeMove, RunConstants.ValidModes);
        Assert.Equal(2, RunConstants.ValidModes.Count);
    }

    [Fact]
    public void RunConstants_StatusConstants_AreCorrectValues()
    {
        Assert.Equal("ok", RunConstants.StatusOk);
        Assert.Equal("completed_with_errors", RunConstants.StatusCompletedWithErrors);
        Assert.Equal("blocked", RunConstants.StatusBlocked);
        Assert.Equal("cancelled", RunConstants.StatusCancelled);
        Assert.Equal("failed", RunConstants.StatusFailed);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.00 KB")]
    [InlineData(1_048_576L, "1.00 MB")]
    [InlineData(1_073_741_824L, "1.00 GB")]
    [InlineData(1_099_511_627_776L, "1.00 TB")]
    public void Formatting_FormatSize_ReturnsExpectedOutput(long bytes, string expected)
        => Assert.Equal(expected, Formatting.FormatSize(bytes));

    [Fact]
    public void OperationResult_Ok_UsesStatusConstant()
        => Assert.Equal(OperationResult.StatusOk, OperationResult.Ok().Status);

    [Fact]
    public void OperationResult_Completed_UsesStatusConstant()
        => Assert.Equal(OperationResult.StatusCompleted, OperationResult.Completed().Status);

    [Fact]
    public void OperationResult_Blocked_UsesStatusConstant()
        => Assert.Equal(OperationResult.StatusBlocked, OperationResult.Blocked("x").Status);

    [Fact]
    public void OperationResult_Error_UsesStatusConstant()
        => Assert.Equal(OperationResult.StatusError, OperationResult.Error("x").Status);

    [Fact]
    public void OperationResult_Skipped_UsesStatusConstant()
        => Assert.Equal(OperationResult.StatusSkipped, OperationResult.Skipped("x").Status);

    [Fact]
    public void RunOutcome_ToStatusString_UsesRunConstants()
    {
        Assert.Equal(RunConstants.StatusOk, RunOutcome.Ok.ToStatusString());
        Assert.Equal(RunConstants.StatusCompletedWithErrors, RunOutcome.CompletedWithErrors.ToStatusString());
        Assert.Equal(RunConstants.StatusBlocked, RunOutcome.Blocked.ToStatusString());
        Assert.Equal(RunConstants.StatusCancelled, RunOutcome.Cancelled.ToStatusString());
        Assert.Equal(RunConstants.StatusFailed, RunOutcome.Failed.ToStatusString());
    }

    [Theory]
    [InlineData("RunOptions.cs")]
    [InlineData("RunResult")]
    public void RunOptions_And_RunResult_DefaultToConstants(string _)
    {
        var opts = new RunOptions();
        Assert.Equal(RunConstants.ModeDryRun, opts.Mode);

        var result = new RunResult();
        Assert.Equal(RunConstants.StatusOk, result.Status);
    }

    [Fact]
    public void NoMagicDryRunMove_InKeyInfrastructureFiles()
    {
        var srcDir = ResolveSrcDir();
        var targetFiles = new[]
        {
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "PhasePlanning.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "RunOrchestrator.StandardPhaseSteps.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Deduplication", "FolderDeduplicator.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Quarantine", "QuarantineService.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Reporting", "RunReportWriter.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Configuration", "SettingsLoader.cs"),
        };

        // Check for mode comparison patterns like == "DryRun" or == "Move"
        // but NOT phase display labels like new ActionPhaseStep("Move", ...)
        var forbidden = new[] { "== \"DryRun\"", "== \"Move\"", "!= \"DryRun\"", "!= \"Move\"" };

        foreach (var file in targetFiles)
        {
            if (!File.Exists(file)) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                // Skip comments
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                foreach (var magic in forbidden)
                {
                    Assert.False(
                        line.Contains(magic, StringComparison.Ordinal),
                        $"Magic string {magic} found in {Path.GetFileName(file)} line {i + 1}: {line.Trim()}");
                }
            }
        }
    }

    private static string ResolveSrcDir([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            var src = Path.Combine(dir, "src");
            if (Directory.Exists(src) && File.Exists(Path.Combine(src, "Romulus.sln")))
                return src;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine("src");
    }

    private static string FindMainWindowCodePath([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Romulus.sln")) ||
                Directory.Exists(Path.Combine(dir, "src")))
            {
                var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", "MainWindow.xaml.cs");
                if (File.Exists(candidate)) return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine("src", "Romulus.UI.Wpf", "MainWindow.xaml.cs");
    }

    private static string FindWpfViewModelPath(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Romulus.sln")) ||
                Directory.Exists(Path.Combine(dir, "src")))
            {
                var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", "ViewModels", fileName);
                if (File.Exists(candidate)) return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine("src", "Romulus.UI.Wpf", "ViewModels", fileName);
    }

    private static string FindWpfViewPath(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Romulus.sln")) ||
                Directory.Exists(Path.Combine(dir, "src")))
            {
                var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", "Views", fileName);
                if (File.Exists(candidate)) return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine("src", "Romulus.UI.Wpf", "Views", fileName);
    }

    // ═══ Round 4: Orphaned services must stay removed ═════════════════

    private static readonly Assembly InfraAssembly = typeof(Romulus.Infrastructure.Configuration.SettingsLoader).Assembly;

    [Theory]
    [InlineData("InsightsEngine")]
    [InlineData("RunHistoryService")]
    [InlineData("ScanIndexService")]
    public void OrphanedService_MustNotExistInInfraAssembly(string typeName)
    {
        // Audit O01/O03/O04: These services were built with tests but never wired
        // into DI or production code. They are dead maintenance weight.
        var type = InfraAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == typeName);

        Assert.Null(type);
    }

    // ═══ Round 5: Well-known folder magic strings must use RunConstants ═══

    [Fact]
    public void NoMagicTrashFolderNames_InKeyInfrastructureFiles()
    {
        var srcDir = ResolveSrcDir();
        var targetFiles = new[]
        {
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "MovePipelinePhase.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "JunkRemovalPipelinePhase.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "ConversionPhaseHelper.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "PipelinePhaseHelpers.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "ExecutionHelpers.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Sorting", "ConsoleSorter.cs"),
            Path.Combine(srcDir, "Romulus.Infrastructure", "Deduplication", "FolderDeduplicator.cs"),
            Path.Combine(srcDir, "Romulus.CLI", "Program.cs"),
        };

        var forbidden = new[]
        {
            "\"_TRASH_REGION_DEDUPE\"",
            "\"_TRASH_JUNK\"",
            "\"_TRASH_CONVERTED\"",
            "\"_TRASH\"",
            "\"_FOLDER_DUPES\"",
            "\"PS3_DUPES\"",
            "\"_QUARANTINE\"",
            "\"_BACKUP\"",
            "\"_BIOS\"",
            "\"_JUNK\"",
            "\"_REVIEW\"",
        };

        foreach (var file in targetFiles)
        {
            if (!File.Exists(file)) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                    continue;

                foreach (var magic in forbidden)
                {
                    Assert.False(
                        lines[i].Contains(magic, StringComparison.Ordinal),
                        $"Magic folder name {magic} found in {Path.GetFileName(file)} line {i + 1}: {trimmed}");
                }
            }
        }
    }

    [Fact]
    public void CliUpdateDats_DoesNotBlockWithGetAwaiterGetResult()
    {
        var srcDir = ResolveSrcDir();
        var codePath = Path.Combine(srcDir, "Romulus.CLI", "Program.cs");
        Assert.True(File.Exists(codePath), $"Missing file: {codePath}");

        var code = File.ReadAllText(codePath);

        Assert.DoesNotContain(
            "DownloadDatByFormatAsync(entry.Url, fileName, entry.Format).GetAwaiter().GetResult()",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MovePipelinePhase_UsesCentralAuditActionConstant_ForMoveRows()
    {
        var srcDir = ResolveSrcDir();
        var codePath = Path.Combine(srcDir, "Romulus.Infrastructure", "Orchestration", "MovePipelinePhase.cs");
        Assert.True(File.Exists(codePath), $"Missing file: {codePath}");

        var code = File.ReadAllText(codePath);

        Assert.Contains("RunConstants.AuditActions.Move", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiReviewApproval_UsesTypeInfoDeserialization()
    {
        var srcDir = ResolveSrcDir();
        var codePath = Path.Combine(srcDir, "Romulus.Api", "Program.cs");
        Assert.True(File.Exists(codePath), $"Missing file: {codePath}");

        var code = File.ReadAllText(codePath);

        Assert.DoesNotContain("ReadFromJsonAsync<ApiReviewApprovalRequest>()", code, StringComparison.Ordinal);
        Assert.Contains("ReadFromJsonAsync(ApiJsonSerializerContext.Default.ApiReviewApprovalRequest)", code, StringComparison.Ordinal);
    }
}
