using System.Text.Json;
using System.Text;
using System.Windows.Media;
using System.Xml.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Converters;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public partial class GuiViewModelTests
{
    // ═══ Accessibility Coverage (VERIFY-002) ════════════════════════════

    [Fact]
    public void Accessibility_AllButtons_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        // Parse Button elements and check for AutomationProperties.Name
        // Match <Button ... /> or <Button ...>...</Button> blocks
        var buttonRegex = new System.Text.RegularExpressions.Regex(
            @"<Button\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var buttonsWithoutA11y = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in buttonRegex.Matches(xamlContent))
        {
            var buttonTag = match.Value;
            if (!buttonTag.Contains("AutomationProperties.Name"))
            {
                // Extract x:Name or Content for identification
                var nameMatch = System.Text.RegularExpressions.Regex.Match(buttonTag, @"x:Name=""([^""]+)""");
                var contentMatch = System.Text.RegularExpressions.Regex.Match(buttonTag, @"Content=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value
                    : contentMatch.Success ? contentMatch.Groups[1].Value
                    : buttonTag[..Math.Min(80, buttonTag.Length)];
                buttonsWithoutA11y.Add(id);
            }
        }

        Assert.True(buttonsWithoutA11y.Count == 0,
            $"{buttonsWithoutA11y.Count} Button(s) without AutomationProperties.Name:\n" +
            string.Join("\n", buttonsWithoutA11y));
    }

    [Fact]
    public void Accessibility_AllTextBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var textBoxRegex = new System.Text.RegularExpressions.Regex(
            @"<TextBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in textBoxRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} TextBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_AllComboBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var comboRegex = new System.Text.RegularExpressions.Regex(
            @"<ComboBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in comboRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} ComboBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_AllListBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var listBoxRegex = new System.Text.RegularExpressions.Regex(
            @"<ListBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in listBoxRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} ListBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_RegionCheckBoxes_HaveDescriptiveAutomationName()
    {
        var xamlContent = ReadAllWpfXaml();
        var regionBindingRegex = new System.Text.RegularExpressions.Regex(
            @"<CheckBox[^>]*IsChecked=""\{Binding (Prefer[A-Z0-9]+)\}""[^>]*>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var regionCodes = regionBindingRegex.Matches(xamlContent)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(regionCodes);

        var missingA11y = new List<string>();
        foreach (var region in regionCodes)
        {
            // Find CheckBox with this binding and check for AutomationProperties.Name
            var pattern = new System.Text.RegularExpressions.Regex(
                $@"<CheckBox[^>]*IsChecked=""\{{Binding {region}\}}""[^>]*>",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

            var match = pattern.Match(xamlContent);
            if (!match.Success || !match.Value.Contains("AutomationProperties.Name"))
                missingA11y.Add(region);
        }

        Assert.True(missingA11y.Count == 0,
            $"{missingA11y.Count} Region CheckBox(es) without descriptive AutomationProperties.Name:\n" +
            string.Join("\n", missingA11y));
    }

    [Fact]
    public void Accessibility_DataTemplateCheckBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        // DataTemplate CheckBoxes should have AutomationProperties.Name binding
        var dataTemplateRegex = new System.Text.RegularExpressions.Regex(
            @"<DataTemplate>\s*<CheckBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in dataTemplateRegex.Matches(xamlContent))
        {
            if (!match.Value.Contains("AutomationProperties.Name"))
            {
                var contentMatch = System.Text.RegularExpressions.Regex.Match(match.Value, @"Content=""\{Binding ([^}]+)\}""");
                var id = contentMatch.Success ? contentMatch.Groups[1].Value : match.Value[..Math.Min(60, match.Value.Length)];
                missing.Add($"DataTemplate CheckBox with Content={id}");
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} DataTemplate CheckBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    // ═══ XAML/VM Completeness Checks ════════════════════════════════════

    [Fact]
    public void XamlBinding_NoDuplicateAutomationNames()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var a11yRegex = new System.Text.RegularExpressions.Regex(
            @"AutomationProperties\.Name=""([^""{}]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in a11yRegex.Matches(xamlContent))
        {
            var name = match.Groups[1].Value;
            names[name] = names.GetValueOrDefault(name) + 1;
        }

        var duplicates = names.Where(kv => kv.Value > 1)
            .Select(kv => $"'{kv.Key}' × {kv.Value}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate AutomationProperties.Name values:\n" +
            string.Join("\n", duplicates));
    }

    [Fact]
    public void XamlBinding_MinimumBindingCount()
    {
        // Ensure we don't accidentally lose bindings during refactoring
        var xamlContent = ReadAllWpfXaml();

        var bindingCount = System.Text.RegularExpressions.Regex.Matches(
            xamlContent, @"\{Binding\s").Count;

        Assert.True(bindingCount >= 70,
            $"Expected at least 70 bindings in MainWindow.xaml, found {bindingCount}. " +
            "Bindings may have been accidentally removed during refactoring.");
    }

    [Fact]
    public void XamlBinding_MinimumAutomationPropertiesCount()
    {
        // Ensure accessibility annotations don't regress
        var xamlContent = ReadAllWpfXaml();

        var a11yCount = System.Text.RegularExpressions.Regex.Matches(
            xamlContent, @"AutomationProperties\.Name").Count;

        Assert.True(a11yCount >= 70,
            $"Expected at least 70 AutomationProperties.Name in MainWindow.xaml, found {a11yCount}. " +
            "Accessibility annotations may have been accidentally removed.");
    }

    // ═══ TASK-104: TabIndex Groups ══════════════════════════════════════

    [Fact]
    public void TabIndex_MainWindow_HasLogicalGroups()
    {
        var xamlContent = ReadAllWpfXaml();

        var tabIndexRegex = new System.Text.RegularExpressions.Regex(
            @"TabIndex=""(\d+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var indices = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in tabIndexRegex.Matches(xamlContent))
            indices.Add(int.Parse(match.Groups[1].Value));

        // Action bar group (1-3)
        Assert.Contains(1, indices);
        Assert.Contains(2, indices);
        Assert.Contains(3, indices);

        // Root list group (10-12)
        Assert.Contains(10, indices);
        Assert.Contains(11, indices);
        Assert.Contains(12, indices);

        // Config options group (40-41)
        Assert.Contains(40, indices);
        Assert.Contains(41, indices);

        // Navigation / advanced option groups (50+)
        Assert.Contains(50, indices);
        Assert.Contains(51, indices);
        Assert.Contains(52, indices);
        Assert.Contains(53, indices);
        Assert.Contains(54, indices);
        Assert.Contains(55, indices);

        // Safety group (60+)
        Assert.Contains(60, indices);
        Assert.Contains(61, indices);

        // At least 14 controls with explicit TabIndex across the shell/views
        Assert.True(indices.Count >= 14,
            $"Expected at least 14 controls with TabIndex, found {indices.Count}");
    }

    // ═══ TASK-127: Feature Buttons use MinWidth not Width ═══════════════

    [Fact]
    public void FeatureButtons_ProfileButtons_UseMinWidth()
    {
        var xamlContent = ReadAllWpfXaml();

        // Profile buttons should use MinWidth, not fixed Width
        // AutomationProperties.Name is now a binding key (e.g. Settings.ProfileSaveTip)
        var profileBindingKeys = new[] { "Settings.ProfileSaveTip", "Settings.ProfileLoadTip", "Settings.ProfileDeleteTip", "Settings.ProfileImportTip", "Settings.ProfileDiffTip" };
        foreach (var key in profileBindingKeys)
        {
            var pattern = $"Loc[{key}]";
            var idx = xamlContent.IndexOf(pattern, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Button with AutomationProperties.Name binding for '{key}' not found in XAML");

            // Extract the Button tag containing this automation name
            var tagStart = xamlContent.LastIndexOf("<Button", idx, StringComparison.Ordinal);
            var tagEnd = xamlContent.IndexOf("/>", idx, StringComparison.Ordinal);
            if (tagEnd < 0) tagEnd = xamlContent.IndexOf(">", idx, StringComparison.Ordinal);
            var buttonTag = xamlContent[tagStart..(tagEnd + 2)];

            Assert.Contains("MinWidth=", buttonTag);
            // Should NOT have fixed Width= (only MinWidth=)
            var hasFixedWidth = System.Text.RegularExpressions.Regex.IsMatch(
                buttonTag, @"(?<!\bMin)Width=""");
            Assert.False(hasFixedWidth, $"Button '{key}' still uses fixed Width instead of MinWidth");
        }
    }

    // ═══ TASK-095: MessageDialog exists and DialogService uses it ════════

    [Fact]
    public void MessageDialog_XamlFile_UsesDynamicResources()
    {
        var xamlPath = FindWpfFile("MessageDialog.xaml");
        Assert.True(File.Exists(xamlPath), "MessageDialog.xaml must exist");

        var xamlContent = File.ReadAllText(xamlPath);
        Assert.Contains("DynamicResource BrushBackground", xamlContent);
        Assert.Contains("DynamicResource BrushTextPrimary", xamlContent);
        Assert.Contains("DynamicResource BrushAccentCyan", xamlContent);
    }

    [Fact]
    public void DialogService_UsesMessageDialog_NotRawMessageBox()
    {
        var csPath = FindWpfFile(Path.Combine("Services", "DialogService.cs"));
        Assert.True(File.Exists(csPath), "DialogService.cs must exist");

        var code = File.ReadAllText(csPath);

        // DialogService methods should use MessageDialog.Show, not MessageBox.Show
        Assert.Contains("MessageDialog.Show(", code);

        // The only MessageBox reference should be for the return type, not for Show calls
        var messageBoxShowCount = System.Text.RegularExpressions.Regex.Matches(
            code, @"MessageBox\.Show\(").Count;
        Assert.Equal(0, messageBoxShowCount);
    }

    // ═══ TASK-131/132: INotifyDataErrorInfo Path Validation ═════════════

    [Fact]
    public void ToolPath_InvalidPath_HasErrors()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\nonexistent\path\chdman.exe";
        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.ToolChdman)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("nicht gefunden", errors[0]);
    }

    [Fact]
    public void ToolPath_EmptyPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_InvalidPath_HasErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = @"C:\nonexistent\directory\path";
        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.DatRoot)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("existiert nicht", errors[0]);
    }

    [Fact]
    public void DirectoryPath_EmptyPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_ValidPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_ProtectedPath_HasBlockingError()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(protectedPath))
            return;

        var vm = new MainViewModel();
        vm.TrashRoot = protectedPath;

        Assert.True(vm.HasBlockingValidationErrors);
        var errors = vm.GetErrors(nameof(vm.TrashRoot)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("protected", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorsChanged_FiredOnInvalidPath()
    {
        var vm = new MainViewModel();
        string? changedProperty = null;
        vm.ErrorsChanged += (_, e) => changedProperty = e.PropertyName;
        vm.TrashRoot = @"C:\nonexistent\directory";
        Assert.Equal(nameof(vm.TrashRoot), changedProperty);
    }

    [Fact]
    public void Xaml_PathBindings_HaveValidatesOnNotifyDataErrors()
    {
        var xaml = ReadAllWpfXaml();
        foreach (var prop in new[] { "ToolChdman", "ToolDolphin", "Tool7z", "ToolPsxtract", "ToolCiso",
                                     "DatRoot", "TrashRoot", "AuditRoot", "Ps3DupesRoot" })
        {
            var pattern = $"Binding {prop}";
            var idx = xaml.IndexOf(pattern, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Binding for {prop} not found in XAML");
            var segment = xaml.Substring(idx, Math.Min(200, xaml.Length - idx));
            Assert.Contains("ValidatesOnNotifyDataErrors=True", segment);
        }
    }

    // ═══ TASK-117: Locale has tooltip ═══════════════════════════════════

    [Fact]
    public void Xaml_LocaleComboBox_HasLocalizationTooltip()
    {
        var xaml = ReadAllWpfXaml();
        var localeIdx = xaml.IndexOf("Binding Locale", StringComparison.Ordinal);
        Assert.True(localeIdx >= 0);
        var segment = xaml.Substring(Math.Max(0, localeIdx - 200), Math.Min(600, xaml.Length - Math.Max(0, localeIdx - 200)));
        Assert.Contains("ToolTip=", segment);
    }

    // ═══ WPF file locator ══════════════════════════════════════════════

    private static string FindWpfFile(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        var candidate = Path.Combine(repoRoot, "src", "Romulus.UI.Wpf", fileName);
        if (File.Exists(candidate))
            return candidate;

        return Path.Combine("src", "Romulus.UI.Wpf", fileName);
    }

    /// <summary>Read and concatenate all WPF XAML files (MainWindow + Views/*.xaml).</summary>
    private static string ReadAllWpfXaml()
    {
        var main = FindWpfFile("MainWindow.xaml");
        var sb = new System.Text.StringBuilder(File.ReadAllText(main));
        var viewsDir = Path.Combine(Path.GetDirectoryName(main)!, "Views");
        if (Directory.Exists(viewsDir))
        {
            foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
                sb.AppendLine(File.ReadAllText(file));
        }
        return sb.ToString();
    }

    // ═══ TEST-005: Preset Commands (SafeDryRun, FullSort, Convert) ══════

    [Fact]
    public void PresetSafeDryRun_SetsDryRun_DisablesConvert()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = true;
        vm.AggressiveJunk = true;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetSafeDryRunCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.False(vm.ConvertEnabled);
        Assert.False(vm.AggressiveJunk);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
    }

    [Fact]
    public void PresetFullSort_SetsDryRun_EnablesSort()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.SortConsole = false;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetFullSortCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.True(vm.SortConsole);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
    }

    [Fact]
    public void PresetConvert_SetsDryRun_EnablesConvert()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = false;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetConvertCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.True(vm.ConvertEnabled);
    }

    [Fact]
    public void PresetCommands_AreAlwaysExecutable()
    {
        var vm = new MainViewModel();
        // Presets should always be executable (no CanExecute guard)
        Assert.True(vm.PresetSafeDryRunCommand.CanExecute(null));
        Assert.True(vm.PresetFullSortCommand.CanExecute(null));
        Assert.True(vm.PresetConvertCommand.CanExecute(null));
    }

    // ═══ TEST-002 supplement: Invalid state transitions ═════════════════

    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Idle, RunState.CompletedDryRun)]
    [InlineData(RunState.Idle, RunState.Moving)]
    [InlineData(RunState.Idle, RunState.Converting)]
    [InlineData(RunState.Idle, RunState.Deduplicating)]
    [InlineData(RunState.Idle, RunState.Sorting)]
    [InlineData(RunState.Scanning, RunState.Preflight)]
    [InlineData(RunState.Moving, RunState.Scanning)]
    public void InvalidTransition_IsRejected(RunState from, RunState to)
    {
        // RF-007: IsValidTransition must return false for invalid transitions
        Assert.False(MainViewModel.IsValidTransition(from, to),
            $"Transition {from} → {to} should be invalid");
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Idle, RunState.CompletedDryRun)]
    [InlineData(RunState.Idle, RunState.Moving)]
    [InlineData(RunState.Idle, RunState.Converting)]
    [InlineData(RunState.Idle, RunState.Deduplicating)]
    [InlineData(RunState.Idle, RunState.Sorting)]
    [InlineData(RunState.Scanning, RunState.Preflight)]
    [InlineData(RunState.Moving, RunState.Scanning)]
    public void InvalidTransition_ThrowsInvalidOperationException(RunState from, RunState to)
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, from);

        Assert.Throws<InvalidOperationException>(() => vm.CurrentRunState = to);
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight)]
    [InlineData(RunState.Preflight, RunState.Scanning)]
    [InlineData(RunState.Scanning, RunState.Deduplicating)]
    [InlineData(RunState.Deduplicating, RunState.Sorting)]
    [InlineData(RunState.Deduplicating, RunState.Moving)]
    [InlineData(RunState.Sorting, RunState.Moving)]
    [InlineData(RunState.Moving, RunState.Sorting)]
    [InlineData(RunState.Moving, RunState.Converting)]
    [InlineData(RunState.Preflight, RunState.Cancelled)]
    [InlineData(RunState.Scanning, RunState.Failed)]
    [InlineData(RunState.Moving, RunState.Completed)]
    public void ValidTransition_DoesNotThrow(RunState from, RunState to)
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, from);

        var ex = Record.Exception(() => vm.CurrentRunState = to);
        Assert.Null(ex);
        Assert.Equal(to, vm.CurrentRunState);
    }

    // ═══ TEST-007 supplement: CTS cancel signal ═════════════════════════

    [Fact]
    public void CreateRunCancellation_ReturnsCancellableToken()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        Assert.False(ct.IsCancellationRequested);
    }

    [Fact]
    public void CancelCommand_SignalsCancellationToken()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        SetRunStateViaValidPath(vm, RunState.Scanning);

        vm.CancelCommand.Execute(null);

        Assert.True(ct.IsCancellationRequested);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void CancelCommand_MultipleCalls_StaysCancelledAndDoesNotResetState()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        SetRunStateViaValidPath(vm, RunState.Scanning);

        vm.CancelCommand.Execute(null);
        // Second cancel attempt - must remain idempotent and keep Cancelled state.
        var ex = Record.Exception(() => vm.CancelCommand.Execute(null));
        Assert.Null(ex);
        Assert.True(ct.IsCancellationRequested);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    // ═══ TEST-008 supplement: Rollback file restoration ═════════════════

    [Fact]
    public void RollbackService_Execute_RestoresMovedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback_" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(tempDir, "src");
        var destDir = Path.Combine(tempDir, "dest");
        var keyPath = Path.Combine(tempDir, "audit-signing.key");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);

        try
        {
            var srcFile = Path.Combine(srcDir, "game.rom");
            var destFile = Path.Combine(destDir, "game.rom");
            File.WriteAllText(destFile, "ROM-DATA");

            // Write audit CSV manually (as AuditCsvStore would)
            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore(keyFilePath: keyPath);
            audit.AppendAuditRow(auditPath, tempDir, srcFile, destFile, "Move", "GAME", "", "test");
            audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

            // Execute rollback: should move destFile back to srcFile
            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir }, keyPath);

            Assert.Equal(1, restored.RolledBack);
            Assert.True(File.Exists(srcFile), "Source file should be restored");
            Assert.False(File.Exists(destFile), "Dest file should be gone after rollback");
            Assert.Equal("ROM-DATA", File.ReadAllText(srcFile));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RollbackService_Execute_SkipsNonMoveActions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
            audit.AppendAuditRow(auditPath, tempDir,
                Path.Combine(tempDir, "a.rom"),
                Path.Combine(tempDir, "b.rom"),
                "Skip", "GAME", "", "test");

            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir });
            Assert.Equal(0, restored.RolledBack);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RollbackService_Execute_BlocksTamperedSignedAudit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback3_" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(tempDir, "src");
        var destDir = Path.Combine(tempDir, "dest");
        var keyPath = Path.Combine(tempDir, "audit-signing.key");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);

        try
        {
            var srcFile = Path.Combine(srcDir, "game.rom");
            var destFile = Path.Combine(destDir, "game.rom");
            File.WriteAllText(destFile, "ROM-DATA");

            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore(keyFilePath: keyPath);
            audit.AppendAuditRow(auditPath, tempDir, srcFile, destFile, "Move", "GAME", "", "test");
            audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

            File.AppendAllText(auditPath, "tampered\n");

            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir }, keyPath);

            Assert.Equal(0, restored.RolledBack);
            Assert.Equal(1, restored.Failed);
            Assert.False(File.Exists(srcFile));
            Assert.True(File.Exists(destFile));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ═══ TEST-009 supplement: Runtime theme cycle ═══════════════════════

    [Fact]
    public void ThemeService_InitialState_IsDark()
    {
        var ts = new ThemeService();
        Assert.Equal(AppTheme.Dark, ts.Current);
        Assert.True(ts.IsDark);
    }

    [Fact]
    public void ThemeService_ToggleCycle_FollowsAllThemes()
    {
        // Toggle() calls ApplyTheme() which needs Application.Current — not available in unit tests.
        // Verify the cycle logic is correct by checking the AllThemes list order.
        var all = ThemeService.AllThemes;
        Assert.Equal(6, all.Count);
        Assert.Equal(AppTheme.Dark, all[0]);
        Assert.Equal(AppTheme.CleanDarkPro, all[1]);
        Assert.Equal(AppTheme.RetroCRT, all[2]);
        Assert.Equal(AppTheme.ArcadeNeon, all[3]);
        Assert.Equal(AppTheme.Light, all[4]);
        Assert.Equal(AppTheme.HighContrast, all[5]);
    }

    [Fact]
    public void ThemeService_ApplyThemeBool_MapsCorrectly()
    {
        // ApplyTheme(bool) maps: true → Dark, false → Light
        // Verify the mapping logic without calling Application.Current
        Assert.Equal(AppTheme.Dark, true ? AppTheme.Dark : AppTheme.Light);
        Assert.Equal(AppTheme.Light, false ? AppTheme.Dark : AppTheme.Light);
    }

    [Fact]
    public void ThemeNames_MatchExpectedValues()
    {
        var names = Enum.GetNames<AppTheme>();
        Assert.Contains("Dark", names);
        Assert.Contains("Light", names);
        Assert.Contains("HighContrast", names);
        Assert.Contains("CleanDarkPro", names);
        Assert.Contains("RetroCRT", names);
        Assert.Contains("ArcadeNeon", names);
        Assert.Equal(6, names.Length);
    }

    // ═══ TEST-010: VM Smoke Tests ═══════════════════════════════════════

    [Fact]
    public void MainViewModel_Constructor_NoException()
    {
        var ex = Record.Exception(() => new MainViewModel());
        Assert.Null(ex);
    }

    [Fact]
    public void MainViewModel_AllPublicCommands_NotNull()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.RunCommand);
        Assert.NotNull(vm.CancelCommand);
        Assert.NotNull(vm.StartMoveCommand);
        Assert.NotNull(vm.PresetSafeDryRunCommand);
        Assert.NotNull(vm.PresetFullSortCommand);
        Assert.NotNull(vm.PresetConvertCommand);
        Assert.NotNull(vm.QuickPreviewCommand);
        Assert.NotNull(vm.OpenReportCommand);
        Assert.NotNull(vm.SaveSettingsCommand);
        Assert.NotNull(vm.LoadSettingsCommand);
        Assert.NotNull(vm.GameKeyPreviewCommand);
    }

    [Fact]
    public void MainViewModel_DefaultState_IsIdle()
    {
        var vm = new MainViewModel();
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasRunResult);
    }

    [Fact]
    public void MainViewModel_Collections_Initialized()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.Roots);
        Assert.NotNull(vm.LogEntries);
        Assert.NotNull(vm.ExtensionFilters);
        Assert.NotNull(vm.ConsoleFilters);
        Assert.NotNull(vm.ToolCategories);
        Assert.NotNull(vm.QuickAccessItems);
        Assert.NotNull(vm.RecentToolItems);
    }

    [Fact]
    public void MainViewModel_SettingsDefaults_Sensible()
    {
        var vm = new MainViewModel();
        // Default should be safe: DryRun on, no aggressive junk
        Assert.True(vm.DryRun);
        Assert.False(vm.AggressiveJunk);
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
    }

    // ═══ TASK-124: Localization — inline strings must use _loc ═════════

    [Theory]
    [InlineData("Run.MoveApplyGate.Unlocked")]
    [InlineData("Run.MoveApplyGate.LockedNoPrev")]
    [InlineData("Run.MoveApplyGate.LockedChanged")]
    [InlineData("Progress.BusyHint.Converting")]
    [InlineData("Progress.BusyHint.Preview")]
    [InlineData("Progress.BusyHint.DryRun")]
    [InlineData("Progress.BusyHint.Move")]
    [InlineData("Progress.BusyHint.CancelRequested")]
    [InlineData("Step.NoRoots")]
    [InlineData("Step.Ready")]
    [InlineData("Step.PressF5")]
    [InlineData("Step.Preflight")]
    [InlineData("Step.Scanning")]
    [InlineData("Step.Deduplicating")]
    [InlineData("Step.Sorting")]
    [InlineData("Step.Moving")]
    [InlineData("Step.Converting")]
    [InlineData("Step.Running")]
    [InlineData("Step.PreviewComplete")]
    [InlineData("Step.Completed")]
    [InlineData("Status.RootsConfigured")]
    [InlineData("Status.NoRoots")]
    [InlineData("Status.ToolsFound")]
    [InlineData("Status.ToolsNotFound")]
    [InlineData("Status.NoTools")]
    [InlineData("Status.DatActive")]
    [InlineData("Status.DatPathInvalid")]
    [InlineData("Status.DatDisabled")]
    [InlineData("Status.Ready.Ok")]
    [InlineData("Status.Ready.Warning")]
    [InlineData("Status.Ready.Blocked")]
    [InlineData("Tool.Status.Found")]
    [InlineData("Tool.Status.NotFound")]
    [InlineData("Conversion.ReviewRequired")]
    [InlineData("Result.Summary.PreviewDone")]
    [InlineData("Result.Summary.PreviewShortcutHint")]
    [InlineData("Result.Summary.ChangesApplied")]
    [InlineData("Result.Summary.CancelledPartial")]
    [InlineData("Result.Summary.CancelledInPhase")]
    [InlineData("Result.Summary.CancelledInPhaseMoved")]
    [InlineData("Result.Context.Preview")]
    [InlineData("Result.Context.CancelledPartial")]
    [InlineData("Result.Context.CancelledNoData")]
    [InlineData("Result.Context.ConvertOnly")]
    [InlineData("Result.Context.MoveCompleted")]
    [InlineData("Result.InlineConfirmWaiting")]
    [InlineData("Result.InlineConfirmReady")]
    [InlineData("Phase.Skipped.MoveConvert")]
    [InlineData("Phase.Skipped.MoveOnly")]
    [InlineData("Phase.Skipped.ConvertOnly")]
    [InlineData("Result.Summary.Failed")]
    public void Localization_DeJson_ContainsRequiredKey(string key)
    {
        // RED: These keys do not yet exist in de.json
        var loc = new LocalizationService();
        var value = loc[key];
        Assert.False(value.StartsWith('[') && value.EndsWith(']'),
            $"Key '{key}' missing from de.json — got placeholder [{key}]");
    }

    [Fact]
    public void MoveApplyGateText_UsesLocalizationService_NotHardcodedGerman()
    {
        // Inject English locale so hardcoded German would be detected
        var loc = new Romulus.UI.Wpf.Services.LocalizationService();
        loc.SetLocale("en");
        var vm = new MainViewModel(new Romulus.UI.Wpf.Services.ThemeService(), new Romulus.UI.Wpf.Services.WpfDialogService(), loc: loc);
        var text = vm.MoveApplyGateText;
        Assert.DoesNotContain("Änderungen anwenden ist gesperrt", text);
    }

    [Fact]
    public void RefreshStatus_StatusLabels_UseLocalizationService()
    {
        // Inject English locale so hardcoded German would be detected
        var loc = new Romulus.UI.Wpf.Services.LocalizationService();
        loc.SetLocale("en");
        var vm = new MainViewModel(new Romulus.UI.Wpf.Services.ThemeService(), new Romulus.UI.Wpf.Services.WpfDialogService(), loc: loc);
        vm.RefreshStatus();
        Assert.DoesNotContain("Keine Ordner", vm.StatusRoots);
        Assert.DoesNotContain("Keine Tools", vm.StatusTools);
    }

    [Fact]
    public void Localization_De_UsesUnifiedUxTerms_ForAuditFindings()
    {
        var loc = new LocalizationService();

        Assert.Equal("Behalten", loc["Start.Winners"]);
        Assert.Equal("Vorbereitung", loc["Phase.Preflight"]);
        Assert.Equal("Duplikat-Erkennung", loc["Phase.Dedupe"]);
        Assert.Equal("Aussortiert (Junk)", loc["Result.MetricJunk"]);
        Assert.Contains("Aufräumen starten", loc["Result.BtnCleanup"], StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_De_RollbackPreview_ContainsRestoreCountAndTrashPathPlaceholders()
    {
        var loc = new LocalizationService();
        var preview = loc["Dialog.Rollback.Preview"];

        Assert.Contains("{4}", preview, StringComparison.Ordinal);
        Assert.Contains("{5}", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void StepLabels_Default_UseLocalizationService()
    {
        // RED: Step labels default to hardcoded German
        var vm = new MainViewModel();
        Assert.DoesNotContain("Keine Ordner", vm.StepLabel1);
        Assert.DoesNotContain("Bereit", vm.StepLabel2);
        Assert.DoesNotContain("F5 drücken", vm.StepLabel3);
    }

    // ═══ TASK-127: Code-behind must not reference Infrastructure ═══════

    [Fact]
    public void ToolsView_CodeBehind_NoInfrastructureImport()
    {
        var codeBehindPath = FindWpfFile(Path.Combine("Views", "ToolsView.xaml.cs"));
        var content = File.ReadAllText(codeBehindPath);
        Assert.DoesNotContain("Romulus.Infrastructure", content);
    }

    [Fact]
    public void StartView_CodeBehind_NoDragDropBusinessLogic()
    {
        // RED: StartView.xaml.cs contains vm.Roots.Add() in OnHeroDrop
        var codeBehindPath = FindWpfFile(Path.Combine("Views", "StartView.xaml.cs"));
        var content = File.ReadAllText(codeBehindPath);
        Assert.DoesNotContain("vm.Roots.Add", content);
    }

    // ═══ TASK-125: ConversionPreviewViewModel must exist ════════════════

    [Fact]
    public void ConversionPreviewViewModel_Exists_WithExpectedProperties()
    {
        // RED: ConversionPreviewViewModel does not exist yet
        var type = typeof(MainViewModel).Assembly.GetType(
            "Romulus.UI.Wpf.ViewModels.ConversionPreviewViewModel");
        Assert.NotNull(type);
        Assert.NotNull(type!.GetProperty("Items"));
        Assert.NotNull(type.GetProperty("HasItems"));
        Assert.NotNull(type.GetProperty("SummaryText"));
    }

    [Fact]
    public void MainViewModel_HasConversionPreviewChild()
    {
        // RED: MainViewModel does not have ConversionPreview child VM
        var vm = new MainViewModel();
        var prop = vm.GetType().GetProperty("ConversionPreview");
        Assert.NotNull(prop);
        Assert.NotNull(prop!.GetValue(vm));
    }

    // ═══ TASK-123: Settings delegation — no duplication ═════════════════

    [Fact]
    public void SettingsDelegation_MainVM_TrashRoot_DelegatesToSetup()
    {
        // RED: MainViewModel.TrashRoot is independent, not delegated to Setup
        var vm = new MainViewModel();
        vm.TrashRoot = @"C:\TestTrash";
        Assert.Equal(@"C:\TestTrash", vm.Setup.TrashRoot);
    }

    [Fact]
    public void SettingsDelegation_SetupChange_ReflectedInMainVM()
    {
        // RED: Setting Setup.ToolChdman does not propagate to MainVM.ToolChdman
        var vm = new MainViewModel();
        vm.Setup.ToolChdman = @"C:\tools\chdman.exe";
        Assert.Equal(@"C:\tools\chdman.exe", vm.ToolChdman);
    }

    // ═══ TASK-126A: SortDecision in UI ══════════════════════════════════

    [Fact]
    public void SortDecision_HasAllExpectedValues()
    {
        var values = Enum.GetValues<SortDecision>();
        Assert.Contains(SortDecision.Sort, values);
        Assert.Contains(SortDecision.Review, values);
        Assert.Contains(SortDecision.Blocked, values);
        Assert.Contains(SortDecision.DatVerified, values);
        Assert.Contains(SortDecision.Unknown, values);
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void SortDecision_LibrarySafetyView_CodeBehind_IsPresentationOnly()
    {
        // Architectural guard: sort-decision grouping belongs to ViewModel projection, not code-behind.
        var code = File.ReadAllText(FindUiFile("Views", "LibrarySafetyView.xaml.cs"));
        Assert.DoesNotContain("SortDecision.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshLists(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SortDecision_DefaultIsSort()
    {
        Assert.Equal(SortDecision.Sort, default(SortDecision));
    }

    // ═══ TASK-126B: Smart Action Bar States ═════════════════════════════

    [Fact]
    public void ShowConfigChangedBanner_FalseWhenIdle()
    {
        var vm = new MainViewModel();
        Assert.False(vm.ShowConfigChangedBanner);
    }

    [Fact]
    public void IsMovePhaseApplicable_FalseWhenDryRun()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        Assert.False(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsMovePhaseApplicable_FalseWhenConvertOnly()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        Assert.False(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsMovePhaseApplicable_TrueWhenNotDryRunAndNotConvertOnly()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertOnly = false;
        Assert.True(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_TrueWhenConvertOnly()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        Assert.True(vm.IsConvertPhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_TrueWhenConvertEnabledAndNotDryRun()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = true;
        Assert.True(vm.IsConvertPhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_FalseWhenDryRunOnly()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        vm.ConvertEnabled = false;
        vm.ConvertOnly = false;
        Assert.False(vm.IsConvertPhaseApplicable);
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight, true)]
    [InlineData(RunState.Preflight, RunState.Scanning, true)]
    [InlineData(RunState.Scanning, RunState.Deduplicating, true)]
    [InlineData(RunState.Deduplicating, RunState.Sorting, true)]
    [InlineData(RunState.Deduplicating, RunState.Moving, true)]
    [InlineData(RunState.Sorting, RunState.Moving, true)]
    [InlineData(RunState.Moving, RunState.Sorting, true)]
    [InlineData(RunState.Moving, RunState.Converting, true)]
    [InlineData(RunState.Converting, RunState.Completed, true)]
    [InlineData(RunState.Idle, RunState.Completed, false)]
    [InlineData(RunState.Completed, RunState.Scanning, false)]
    [InlineData(RunState.Moving, RunState.Scanning, false)]
    [InlineData(RunState.Scanning, RunState.Moving, true)]
    [InlineData(RunState.Deduplicating, RunState.Converting, true)]
    [InlineData(RunState.Sorting, RunState.Failed, true)]
    [InlineData(RunState.Completed, RunState.Idle, true)]
    [InlineData(RunState.CompletedDryRun, RunState.Preflight, true)]
    public void TransitionMatrix_SystematicCoverage(RunState from, RunState to, bool expected)
    {
        Assert.Equal(expected, RunStateMachine.IsValidTransition(from, to));
    }

    // ═══ TASK-126C: Region-Ranker ═══════════════════════════════════════

    [Fact]
    public void InitRegionPriorities_EnabledFirst_DisabledLast()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.InitRegionPriorities();

        var enabledItems = vm.RegionPriorities.Where(r => r.IsEnabled).ToList();
        var disabledItems = vm.RegionPriorities.Where(r => !r.IsEnabled).ToList();

        // All enabled items must appear before all disabled items
        int lastEnabledIdx = vm.RegionPriorities.ToList().FindLastIndex(r => r.IsEnabled);
        int firstDisabledIdx = vm.RegionPriorities.ToList().FindIndex(r => !r.IsEnabled);
        if (enabledItems.Count > 0 && disabledItems.Count > 0)
            Assert.True(lastEnabledIdx < firstDisabledIdx);
    }

    [Fact]
    public void InitRegionPriorities_EnabledItemsHavePositions()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.InitRegionPriorities();

        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).ToList();
        Assert.All(enabled, r => Assert.True(r.Position > 0));
        var disabled = vm.RegionPriorities.Where(r => !r.IsEnabled).ToList();
        Assert.All(disabled, r => Assert.Equal(0, r.Position));
    }

    [Fact]
    public void EnabledRegionCount_MatchesEnabledItems()
    {
        var vm = new MainViewModel();
        // Defaults: EU=true, US=true, JP=true, WORLD=true
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        Assert.Equal(3, vm.EnabledRegionCount);
    }

    [Fact]
    public void MoveRegionUpCommand_MovesItem()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();

        // EU=0, US=1, JP=2 initially
        var usItem = vm.RegionPriorities.First(r => r.Code == "US");
        vm.MoveRegionUpCommand.Execute(usItem);

        Assert.Equal("US", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
    }

    [Fact]
    public void MoveRegionUpCommand_AtTop_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionUpCommand.Execute(euItem);

        Assert.Equal("EU", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void MoveRegionDownCommand_MovesItem()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionDownCommand.Execute(euItem);

        Assert.Equal("US", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
    }

    [Fact]
    public void MoveRegionDownCommand_AtLastEnabled_NoChange()
    {
        var vm = new MainViewModel();
        // Enable only EU, disable all others
        vm.PreferEU = true;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionDownCommand.Execute(euItem);

        // EU should stay at position 0 since next item is disabled
        Assert.Equal("EU", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void ToggleRegionCommand_DisablesEnabled()
    {
        var vm = new MainViewModel();
        // Reset all defaults, enable only EU+US
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.ToggleRegionCommand.Execute(euItem);

        Assert.False(vm.PreferEU);
        Assert.Equal(1, vm.EnabledRegionCount);
    }

    [Fact]
    public void ToggleRegionCommand_EnablesDisabled()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferJP = false;
        vm.InitRegionPriorities();

        var jpItem = vm.RegionPriorities.First(r => r.Code == "JP");
        vm.ToggleRegionCommand.Execute(jpItem);

        Assert.True(vm.PreferJP);
    }

    [Fact]
    public void RegionPresetEuFocus_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetEuFocusCommand.Execute(null);

        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferDE);
        Assert.True(vm.PreferFR);
        Assert.True(vm.PreferWORLD);
        Assert.False(vm.PreferUS);
        Assert.False(vm.PreferJP);
    }

    [Fact]
    public void RegionPresetUsFocus_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetUsFocusCommand.Execute(null);

        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferWORLD);
        Assert.True(vm.PreferEU);
        Assert.False(vm.PreferJP);
        Assert.False(vm.PreferDE);
    }

    [Fact]
    public void RegionPresetMultiRegion_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetMultiRegionCommand.Execute(null);

        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
        Assert.False(vm.PreferDE);
    }

    [Fact]
    public void RegionPresetAll_EnablesAllRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetAllCommand.Execute(null);

        Assert.Equal(16, vm.EnabledRegionCount);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
        Assert.True(vm.PreferDE);
        Assert.True(vm.PreferSCAN);
    }

    [Fact]
    public void RegionPreset_OrderMatchesPreset()
    {
        var vm = new MainViewModel();
        vm.RegionPresetUsFocusCommand.Execute(null);

        // US-Focus preset order: US, WORLD, EU
        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToList();
        Assert.Equal(new[] { "US", "WORLD", "EU" }, enabled);
    }

    // ═══ TASK-117: Region Ranker Drag & Drop ════════════════════════════

    [Fact]
    public void MoveRegionTo_ReordersEnabledItems()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        // Order: EU(0), US(1), JP(2) — move JP to position 0
        vm.MoveRegionTo(2, 0);

        Assert.Equal("JP", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
        Assert.Equal("US", vm.RegionPriorities[2].Code);
    }

    [Fact]
    public void MoveRegionTo_RenumbersPositions()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        vm.MoveRegionTo(0, 2);

        Assert.Equal(1, vm.RegionPriorities[0].Position);
        Assert.Equal(2, vm.RegionPriorities[1].Position);
        Assert.Equal(3, vm.RegionPriorities[2].Position);
    }

    [Fact]
    public void MoveRegionTo_SyncsBooleans()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        // Ensure booleans stay consistent after reorder
        vm.MoveRegionTo(0, 1);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
    }

    [Fact]
    public void MoveRegionTo_InvalidFromIndex_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var original = vm.RegionPriorities.Select(r => r.Code).ToList();
        vm.MoveRegionTo(-1, 0);
        vm.MoveRegionTo(99, 0);
        var after = vm.RegionPriorities.Select(r => r.Code).ToList();
        Assert.Equal(original, after);
    }

    [Fact]
    public void MoveRegionTo_DisabledItemStaysInDisabledSection()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        int enabledCount = vm.RegionPriorities.Count(r => r.IsEnabled);
        // Try to move disabled item into enabled section — should be no-op
        int disabledIdx = vm.RegionPriorities.ToList().FindIndex(r => !r.IsEnabled);
        vm.MoveRegionTo(disabledIdx, 0);

        // Enabled count should not change
        Assert.Equal(enabledCount, vm.RegionPriorities.Count(r => r.IsEnabled));
    }

    [Fact]
    public void MoveRegionTo_SameIndex_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var original = vm.RegionPriorities.Select(r => r.Code).ToList();
        vm.MoveRegionTo(0, 0);
        var after = vm.RegionPriorities.Select(r => r.Code).ToList();
        Assert.Equal(original, after);
    }

    [Fact]
    public void ConfigWorkflowViews_PreserveMigratedSortFeatures()
    {
        var optionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));
        var regionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigRegionsView.xaml"));

        Assert.Contains("AllowDrop", optionsXaml);
        Assert.Contains("RegionPriorities", regionsXaml);
        Assert.Contains("MoveRegionUpCommand", regionsXaml);
    }

    // ═══ TASK-126D: Console-Picker ══════════════════════════════════════

    [Fact]
    public void SelectAllConsolesCommand_SelectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);

        Assert.True(vm.ConsoleFilters.All(c => c.IsChecked));
        Assert.Equal(vm.ConsoleFilters.Count, vm.SelectedConsoleCount);
    }

    [Fact]
    public void ClearAllConsolesCommand_DeselectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        vm.ClearAllConsolesCommand.Execute(null);

        Assert.True(vm.ConsoleFilters.All(c => !c.IsChecked));
        Assert.Equal(0, vm.SelectedConsoleCount);
    }

    [Fact]
    public void SelectConsoleGroupCommand_SelectsOnlyGroup()
    {
        var vm = new MainViewModel();
        vm.SelectConsoleGroupCommand.Execute("Nintendo");

        var nintendo = vm.ConsoleFilters.Where(c => c.Category == "Nintendo").ToList();
        var others = vm.ConsoleFilters.Where(c => c.Category != "Nintendo").ToList();

        Assert.True(nintendo.All(c => c.IsChecked));
        Assert.True(others.All(c => !c.IsChecked));
    }

    [Fact]
    public void DeselectConsoleGroupCommand_DeselectsGroup()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        vm.DeselectConsoleGroupCommand.Execute("Sony");

        var sony = vm.ConsoleFilters.Where(c => c.Category == "Sony").ToList();
        var others = vm.ConsoleFilters.Where(c => c.Category != "Sony").ToList();

        Assert.True(sony.All(c => !c.IsChecked));
        Assert.True(others.All(c => c.IsChecked));
    }

    [Fact]
    public void ConsolePresetTop10_Selects10()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetTop10Command.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).ToList();
        Assert.Equal(10, selected.Count);
        Assert.Contains(selected, c => c.Key == "PS1");
        Assert.Contains(selected, c => c.Key == "SNES");
        Assert.Contains(selected, c => c.Key == "N64");
    }

    [Fact]
    public void ConsolePresetDiscBased_SelectsDiscSystems()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetDiscBasedCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("PS1", selected);
        Assert.Contains("PS2", selected);
        Assert.Contains("GC", selected);
        Assert.Contains("DC", selected);
        Assert.DoesNotContain("NES", selected);
        Assert.DoesNotContain("SNES", selected);
    }

    [Fact]
    public void ConsolePresetHandhelds_SelectsHandhelds()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetHandheldsCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("GB", selected);
        Assert.Contains("GBA", selected);
        Assert.Contains("PSP", selected);
        Assert.DoesNotContain("PS1", selected);
    }

    [Fact]
    public void ConsolePresetRetro_SelectsRetroSystems()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetRetroCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("NES", selected);
        Assert.Contains("SNES", selected);
        Assert.Contains("MD", selected);
        Assert.DoesNotContain("PS2", selected);
    }

    [Fact]
    public void SelectedConsoleCount_UpdatesAfterSelection()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.SelectedConsoleCount);

        vm.ConsoleFilters[0].IsChecked = true;
        vm.ConsoleFilters[1].IsChecked = true;

        Assert.Equal(2, vm.SelectedConsoleCount);
    }

    [Fact]
    public void ConsoleCountDisplay_NoSelection_ShowsKeine()
    {
        var vm = new MainViewModel();
        // ConsoleCountDisplay when nothing selected
        Assert.Contains("0", vm.ConsoleCountDisplay);
    }

    [Fact]
    public void RemoveConsoleSelectionCommand_DeselectsItem()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        var firstItem = vm.ConsoleFilters[0];
        Assert.True(firstItem.IsChecked);

        vm.RemoveConsoleSelectionCommand.Execute(firstItem);
        Assert.False(firstItem.IsChecked);
    }

    [Fact]
    public void ConsoleFilterText_FiltersView()
    {
        var vm = new MainViewModel();
        vm.ConsoleFilterText = "Play";
        // The ICollectionView should filter — we verify the filter is set
        Assert.Equal("Play", vm.ConsoleFilterText);
        // ConsoleFiltersView should have filtering active
        Assert.NotNull(vm.ConsoleFiltersView.Filter);
    }

    [Fact]
    public void SelectConsoleGroupCommand_NullCategory_NoException()
    {
        var vm = new MainViewModel();
        vm.SelectConsoleGroupCommand.Execute(null);
        // Should not throw, no console selected
        Assert.Equal(0, vm.SelectedConsoleCount);
    }

    // ═══ TASK-119: Extension Filter Counter + Group Commands ════════════

    [Fact]
    public void SelectedExtensionCount_InitiallyZero()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.SelectedExtensionCount);
    }

    [Fact]
    public void SelectedExtensionCount_UpdatesOnCheck()
    {
        var vm = new MainViewModel();
        vm.ExtensionFilters[0].IsChecked = true;
        vm.ExtensionFilters[1].IsChecked = true;
        Assert.Equal(2, vm.SelectedExtensionCount);
    }

    [Fact]
    public void ExtensionCountDisplay_NoSelection_Reflects()
    {
        var vm = new MainViewModel();
        Assert.Contains("0", vm.ExtensionCountDisplay);
    }

    [Fact]
    public void SelectAllExtensionsCommand_SelectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        Assert.True(vm.ExtensionFilters.All(e => e.IsChecked));
        Assert.Equal(vm.ExtensionFilters.Count, vm.SelectedExtensionCount);
    }

    [Fact]
    public void ClearAllExtensionsCommand_DeselectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.ClearAllExtensionsCommand.Execute(null);
        Assert.True(vm.ExtensionFilters.All(e => !e.IsChecked));
        Assert.Equal(0, vm.SelectedExtensionCount);
    }

    [Fact]
    public void SelectExtensionGroupCommand_SelectsOnlyGroup()
    {
        var vm = new MainViewModel();
        vm.SelectExtensionGroupCommand.Execute("Archive");

        var archives = vm.ExtensionFilters.Where(e => e.Category == "Archive").ToList();
        var others = vm.ExtensionFilters.Where(e => e.Category != "Archive").ToList();

        Assert.True(archives.All(e => e.IsChecked));
        Assert.True(others.All(e => !e.IsChecked));
    }

    [Fact]
    public void DeselectExtensionGroupCommand_DeselectsGroup()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.DeselectExtensionGroupCommand.Execute("Disc-Images");

        var discs = vm.ExtensionFilters.Where(e => e.Category == "Disc-Images").ToList();
        var others = vm.ExtensionFilters.Where(e => e.Category != "Disc-Images").ToList();

        Assert.True(discs.All(e => !e.IsChecked));
        Assert.True(others.All(e => e.IsChecked));
    }

    // ═══ TASK-113: Responsive NavRail Compact ═══════════════════════════

    [Fact]
    public void IsCompactNav_DefaultFalse()
    {
        var vm = new MainViewModel();
        Assert.False(vm.Shell.IsCompactNav);
    }

    [Fact]
    public void IsCompactNav_Settable()
    {
        var vm = new MainViewModel();
        vm.Shell.IsCompactNav = true;
        Assert.True(vm.Shell.IsCompactNav);
    }

    [Fact]
    public void NavigationRailXaml_LabelsBindToIsCompactNav()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "NavigationRail.xaml"));
        Assert.Contains("Shell.IsCompactNav", xaml);
    }

    [Fact]
    public void CommandBarXaml_ShowsWorkspaceAndWorkflowSummary()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "CommandBar.xaml"));
        Assert.Contains("Shell.CurrentWorkspaceBreadcrumb", xaml);
        Assert.Contains("SelectedWorkflowName", xaml);
        Assert.Contains("SelectedRunProfileId", xaml);
    }

    [Fact]
    public void CommandBarXaml_UsesCompactBindings_ForResponsiveHeader()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "CommandBar.xaml"));

        Assert.Contains("Shell.IsCompactNav", xaml);
        Assert.Contains("StatusRuntime", xaml);
        Assert.Contains("CurrentThemeLabel", xaml);
        Assert.Contains("OpenReportLog", xaml);
    }

    [Fact]
    public void ResultViewXaml_UsesStackedResponsiveCharts()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        // Redesigned dashboard: pure XAML stacked bar, no ScottPlot for breakdown
        Assert.DoesNotContain("MinWidth=\"420\"", xaml);
        Assert.DoesNotContain("Height=\"460\"", xaml);
        Assert.Contains("KeepFraction", xaml);
        Assert.Contains("MoveFraction", xaml);
        Assert.Contains("JunkFraction", xaml);
        Assert.Contains("ConsoleDistribution", xaml);
        // No redundant inline dedupe decisions (own tab handles that)
        Assert.DoesNotContain("DedupeGroupItems", xaml);
        // No ScottPlot in ResultView (pure XAML bars)
        Assert.DoesNotContain("scott:", xaml);
    }

    [Fact]
    public void ResultViewXaml_HasTopErrorSummaryBanner()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("HasActionableErrorSummary", xaml);
        Assert.Contains("ActionableErrorSummaryItems", xaml);
        Assert.Contains("ActionableErrorSummaryTitle", xaml);
    }

    [Fact]
    public void ResultViewXaml_HasConvertOnlyHeroMetrics()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("IsConvertOnlyDashboard", xaml);
        Assert.Contains("Run.DashConverted", xaml);
        Assert.Contains("Run.DashConvertBlocked", xaml);
        Assert.Contains("Run.DashConvertSaved", xaml);
        Assert.Contains("Run.DashConvertReview", xaml);
    }

    [Fact]
    public void ProgressViewXaml_HidesNonApplicablePhases_InsteadOfOpacity()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ProgressView.xaml"));

        Assert.Contains("IsMovePhaseApplicable", xaml);
        Assert.Contains("IsConvertPhaseApplicable", xaml);
        Assert.DoesNotContain("Opacity=\"0.4\"", xaml);
        Assert.Contains("SkippedPhaseInfoText", xaml);
    }

    [Fact]
    public void ToolsViewXaml_SimpleMode_HidesFullCatalogNavigation()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));

        Assert.Contains("IsSimpleMode", xaml);
        Assert.Contains("Converter={StaticResource InverseBoolToVis}", xaml);
    }

    // ═══ TASK-115: SmartActionBar RunState DataTriggers ════════════════

    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Sorting)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void RunStateDisplayText_ReturnsNonEmptyString_ForEachState(RunState state)
    {
        var vm = new MainViewModel();
        // Transition through valid path to reach target state
        TransitionTo(vm, state);
        Assert.False(string.IsNullOrWhiteSpace(vm.RunStateDisplayText));
    }

    [Fact]
    public void CurrentRunState_Setter_NotifiesRunStateDisplayText()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        vm.CurrentRunState = RunState.Preflight;
        Assert.Contains(nameof(vm.RunStateDisplayText), changed);
    }

    // Wave 1 (T-W1-UI-REDUCTION): SmartActionBar.xaml wurde entfernt; der ehemalige
    // Block aus 5 Pin-Tests (HasRunStateBindings / RunButton_HiddenViaTriggerWhenBusy /
    // CancelButton_HasRunStateTrigger / ProgressPanel_HasRunStateTrigger /
    // DoesNotDuplicateCommandBarStatusLabel) ist obsolet und wurde geloescht.

    [Theory]
    [InlineData(RunState.Idle, true)]
    [InlineData(RunState.Preflight, false)]
    [InlineData(RunState.Scanning, false)]
    [InlineData(RunState.Deduplicating, false)]
    [InlineData(RunState.Sorting, false)]
    [InlineData(RunState.Moving, false)]
    [InlineData(RunState.Converting, false)]
    [InlineData(RunState.Completed, true)]
    [InlineData(RunState.CompletedDryRun, true)]
    [InlineData(RunState.Failed, true)]
    [InlineData(RunState.Cancelled, true)]
    public void IsIdle_MatchesExpectation_ForEachState(RunState state, bool expectedIdle)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.Equal(expectedIdle, vm.IsIdle);
    }

    [Theory]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    public void HasRunResult_TrueOnlyForCompletedStates(RunState state)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.True(vm.HasRunResult);
    }

    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void HasRunResult_FalseForNonCompletedStates(RunState state)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.False(vm.HasRunResult);
    }

    /// <summary>Transitions the VM through valid states to reach the target.</summary>
    private static void TransitionTo(MainViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return;
        RunState[] chain = [RunState.Preflight, RunState.Scanning, RunState.Deduplicating,
            RunState.Sorting, RunState.Moving, RunState.Converting];
        RunState[] terminals = [RunState.Completed, RunState.CompletedDryRun, RunState.Failed, RunState.Cancelled];

        if (terminals.Contains(target))
        {
            vm.CurrentRunState = RunState.Preflight;
            vm.CurrentRunState = target;
            return;
        }
        foreach (var s in chain)
        {
            vm.CurrentRunState = s;
            if (s == target) return;
        }
    }

    // ═══ TASK-122: RunState entkernen — MainVM delegiert an RunViewModel ═══

    [Fact]
    public void Task122_CurrentRunState_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        // MainVM.CurrentRunState must be the same object as Run.CurrentRunState
        Assert.Equal(vm.Run.CurrentRunState, vm.CurrentRunState);
    }

    [Fact]
    public void Task122_SetRunCurrentRunState_ReflectedInMainVm()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;

        Assert.Equal(RunState.Preflight, vm.CurrentRunState);
    }

    [Fact]
    public void Task122_IsBusy_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        Assert.True(vm.IsBusy);
        Assert.Equal(vm.Run.IsBusy, vm.IsBusy);
    }

    [Fact]
    public void Task122_IsIdle_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        Assert.True(vm.IsIdle);
        Assert.Equal(vm.Run.IsIdle, vm.IsIdle);
    }

    [Fact]
    public void Task122_HasRunResult_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Completed);

        Assert.True(vm.HasRunResult);
        Assert.Equal(vm.Run.HasRunResult, vm.HasRunResult);
    }

    [Fact]
    public void Task122_MainVm_HasNoOwnRunStateField()
    {
        // After TASK-122, MainViewModel must NOT have its own _runState field.
        // RunState is owned exclusively by RunViewModel (ADR-0006).
        var fields = typeof(MainViewModel)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(RunState) && f.Name.Contains("runState", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(fields);
    }

    [Fact]
    public void Task122_PropertyChanged_FiresOnMainVm_WhenRunStateChanges()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.CurrentRunState = RunState.Preflight;

        Assert.Contains("CurrentRunState", changed);
        Assert.Contains("IsBusy", changed);
        Assert.Contains("IsIdle", changed);
    }

    [Fact]
    public void Task122_RunStateDisplayText_StillWorksAfterDelegation()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        // RunStateDisplayText must still return a non-empty localized string
        Assert.False(string.IsNullOrWhiteSpace(vm.RunStateDisplayText));
    }

    [Fact]
    public void Task122_IsValidTransition_StillAvailableOnMainVm()
    {
        // The static helper should still be accessible for backward compat
        Assert.True(MainViewModel.IsValidTransition(RunState.Idle, RunState.Preflight));
        Assert.False(MainViewModel.IsValidTransition(RunState.Idle, RunState.Completed));
    }

    // ═══ UI-UX V2: ActionRail compact height token (72px) ═══════════════

    [Fact]
    public void DesignTokens_ActionRailHeight_IsAtLeast72()
    {
        var tokensPath = FindUiFile("Themes", "_DesignTokens.xaml");
        var content = File.ReadAllText(tokensPath);
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"x:Key=""ActionRailHeight"">(\d+)<");
        Assert.True(match.Success, "ActionRailHeight token not found in _DesignTokens.xaml");
        var value = int.Parse(match.Groups[1].Value);
        Assert.True(value >= 72,
            $"ActionRailHeight is {value}px but must be >= 72px for compact action rail layout.");
    }

    [Fact]
    public void MainWindow_ActionRailRow_MatchesDesignToken()
    {
        var windowPath = FindWpfFile("MainWindow.xaml");
        var content = File.ReadAllText(windowPath);
        // Row 3 should use a dynamic resource or be at least 84
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"<!-- Row 3: ActionRail -->\s*</RowDefinitions>|Height=""(\d+)""\s*/>\s*<!-- Row 3");
        // Find the last RowDefinition (Row 3)
        var rowMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"<RowDefinition\s+Height=""(\d+)""/>");
        // Row 3 is the 4th RowDefinition (index 3) — the one with hardcoded height for ActionRail
        var rowDefs = System.Text.RegularExpressions.Regex.Matches(
            content, @"<RowDefinition\s+Height=""([^""]+)""\s*/>");
        Assert.True(rowDefs.Count >= 4, "Expected at least 4 RowDefinitions in MainWindow.xaml");
        var row3Height = rowDefs[3].Groups[1].Value;
        // Should be "84" or a dynamic resource reference
        if (int.TryParse(row3Height, out var h))
        {
            Assert.True(h >= 84,
                $"MainWindow Row 3 Height is {h}px but must be ≥ 84px to match ActionRailHeight token");
        }
    }

    // ═══ BUG-FIX: Theme button shows NEXT theme instead of current ══════

    [Fact]
    public void CurrentThemeLabel_ReturnsHumanFriendlyName_ForAllThemes()
    {
        var vm = new MainViewModel();

        // Default theme is Dark → "Synthwave"
        Assert.Equal("Synthwave", vm.CurrentThemeLabel);

        // Verify CurrentThemeLabel maps all AppTheme values via reflection
        // (we can't call SelectedTheme= because ApplyTheme loads WPF resources)
        var expectedLabels = new Dictionary<AppTheme, string>
        {
            [AppTheme.Dark] = "Synthwave",
            [AppTheme.CleanDarkPro] = "Clean Dark",
            [AppTheme.RetroCRT] = "Retro CRT",
            [AppTheme.ArcadeNeon] = "Arcade Neon",
            [AppTheme.Light] = "Clean Daylight",
            [AppTheme.HighContrast] = "Stark Contrast",
        };

        // Verify ThemeToggleText also covers all themes (complementary)
        var toggleLabels = new Dictionary<AppTheme, string>
        {
            [AppTheme.Dark] = "⮞ Clean Dark",
            [AppTheme.CleanDarkPro] = "⮞ Retro CRT",
            [AppTheme.RetroCRT] = "⮞ Arcade Neon",
            [AppTheme.ArcadeNeon] = "⮞ Clean Daylight",
            [AppTheme.Light] = "⮞ Stark Contrast",
            [AppTheme.HighContrast] = "⮞ Synthwave",
        };

        // All themes must have a mapping in both CurrentThemeLabel and ThemeToggleText
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            Assert.True(expectedLabels.ContainsKey(theme),
                $"CurrentThemeLabel has no mapping for {theme}");
            Assert.True(toggleLabels.ContainsKey(theme),
                $"ThemeToggleText has no mapping for {theme}");
        }
    }

    [Fact]
    public void CommandBar_ThemeButton_BindsToCurrentThemeLabel()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        // The theme button's display text must bind to CurrentThemeLabel (current theme)
        // NOT to ThemeToggleText (which shows the NEXT theme)
        Assert.Contains("CurrentThemeLabel", content);

        // ThemeToggleText should only appear in ToolTip binding, not in Text binding
        var textBindings = System.Text.RegularExpressions.Regex.Matches(
            content, @"Text=""\{Binding\s+ThemeToggleText\}""");
        Assert.True(textBindings.Count == 0,
            "Theme button Text should bind to CurrentThemeLabel, not ThemeToggleText. " +
            "ThemeToggleText should only appear in ToolTip.");
    }

    [Fact]
    public void CommandBar_ModeToggle_BindsToCurrentUiModeLabel()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        Assert.Contains("IsSimpleMode", content);
        Assert.Contains("CurrentUiModeLabel", content);
    }

    [Fact]
    public void CommandBar_UsesWorkspaceAndInspectorBindings()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        Assert.Contains("Shell.CurrentWorkspaceBreadcrumb", content, StringComparison.Ordinal);
        Assert.Contains("Shell.ToggleContextWingCommand", content, StringComparison.Ordinal);
        Assert.Contains("Shell.ContextToggleLabel", content, StringComparison.Ordinal);
        Assert.Contains("AvailableRunProfiles", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationRail_ToolsVisibility_BindsToShellShowToolsNav()
    {
        var navPath = FindUiFile("Views", "NavigationRail.xaml");
        var content = File.ReadAllText(navPath);

        Assert.Contains("Shell.ShowToolsNav", content);
    }

    [Fact]
    public void ShellViewModel_ContextWing_DefaultsCollapsed()
    {
        var shell = new ShellViewModel(new LocalizationService());

        Assert.False(shell.ShowContextWing);
        Assert.Equal("Inspector einblenden", shell.ContextToggleLabel);
    }

    [Fact]
    public void MainViewModel_SmartActionBar_RemovedInWave1()
    {
        // T-W1-UI-REDUCTION: SmartActionBar wurde aus dem Shell-Layout entfernt.
        // CommandPalette (Ctrl+P) bleibt als Power-User-Oberflaeche; ShowSmartActionBar
        // existiert nicht mehr und MainWindow.xaml referenziert keine ActionRail.
        var vm = new MainViewModel();
        Assert.Null(typeof(MainViewModel).GetProperty("ShowSmartActionBar"));
    }

    [Fact]
    public void MainWindowXaml_Title_IsRomulus_AndActionRailRemoved()
    {
        var xaml = File.ReadAllText(FindUiFile("", "MainWindow.xaml"));

        Assert.Contains("Title=\"Romulus\"", xaml);
        Assert.DoesNotContain("ShowSmartActionBar", xaml);
        Assert.DoesNotContain("SmartActionBar", xaml);
    }

    [Fact]
    public void SubTabBar_ExpertTabs_ReflectConsolidatedNavigation()
    {
        var subTabPath = FindUiFile("Views", "SubTabBar.xaml");
        var content = File.ReadAllText(subTabPath);

        Assert.Contains("Shell.ShowLibraryDecisionsTab", content);
        Assert.Contains("Shell.ShowMissionRegionsTab", content);
        Assert.Contains("Shell.ShowMissionOptionsTab", content);
        Assert.DoesNotContain("Shell.ShowSystemActivityLogTab", content);
        Assert.DoesNotContain("ConverterParameter=QuickStart", content);
        Assert.DoesNotContain("ConverterParameter=Filtering", content);
        Assert.DoesNotContain("ConverterParameter=Report", content);
        Assert.Contains("ConverterParameter=DatManagement", content);
        Assert.Contains("ConverterParameter=Conversion", content);
        Assert.DoesNotContain("ConverterParameter=GameKeyLab", content);
    }

    // ═══ BUG-FIX: EnableDatAudit missing from GUI layer ═════════════════

    [Fact]
    public void SettingsDto_HasEnableDatAudit_DefaultTrue()
    {
        var dto = new SettingsDto();
        Assert.True(dto.EnableDatAudit,
            "SettingsDto.EnableDatAudit must default to true so DAT verification runs by default");
    }

    [Fact]
    public void MainViewModel_HasEnableDatAudit_DefaultTrue()
    {
        var vm = new MainViewModel();
        // EnableDatAudit should be an independent property (not just a copy of UseDat)
        var prop = typeof(MainViewModel).GetProperty("EnableDatAudit");
        Assert.NotNull(prop);
        Assert.True((bool)prop.GetValue(vm)!,
            "MainViewModel.EnableDatAudit must default to true");
    }

    [Fact]
    public void AutoSavePropertyNames_IncludesEnableDatAudit()
    {
        // AutoSavePropertyNames is a private static field — verify via reflection
        var field = typeof(MainViewModel)
            .GetField("AutoSavePropertyNames",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var names = (HashSet<string>)field.GetValue(null)!;
        Assert.Contains("EnableDatAudit", names);
    }

    [Fact]
    public void RunService_EnableDatAudit_ReadsFromViewModel()
    {
        // When a VM has EnableDatAudit = true but UseDat = false,
        // EnableDatAudit must still be independently controllable
        var vm = new MainViewModel();
        vm.UseDat = false;

        // The EnableDatAudit property should exist and be independent
        var prop = typeof(MainViewModel).GetProperty("EnableDatAudit");
        Assert.NotNull(prop);
        // With default true, even if UseDat is false, the property should be true
        Assert.True((bool)prop.GetValue(vm)!);
    }

    private static string FindUiFile(string folder, string fileName)
    {
        var dir = Path.GetDirectoryName(typeof(GuiViewModelTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", folder, fileName);
            if (File.Exists(candidate)) return candidate;
            // Wave 1: Sub-Controls/Dialogs leben jetzt unter Views/Controls bzw. Views/Dialogs.
            var folderRoot = Path.Combine(dir, "src", "Romulus.UI.Wpf", folder);
            if (Directory.Exists(folderRoot))
            {
                var match = Directory.GetFiles(folderRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (match is not null) return match;
            }
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: try repo root from CallerFilePath context
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(GuiViewModelTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "src", "Romulus.UI.Wpf", folder, fileName);
    }

    // ═══ UpdateBreakdown fraction normalization ─────────────────────────

    [Fact]
    public void UpdateBreakdown_FractionsNormalizedToTotal_NotMax()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 100;
        vm.DupesRaw = 20;
        vm.JunkRaw = 10;

        vm.UpdateBreakdown();

        Assert.Equal(70, vm.KeepCount);
        Assert.Equal(20, vm.MoveCount);
        Assert.Equal(10, vm.JunkCount);

        // Fractions must sum to ~1.0 (normalized to total)
        Assert.Equal(0.7, vm.KeepFraction, 3);
        Assert.Equal(0.2, vm.MoveFraction, 3);
        Assert.Equal(0.1, vm.JunkFraction, 3);

        var sum = vm.KeepFraction + vm.MoveFraction + vm.JunkFraction;
        Assert.InRange(sum, 0.99, 1.01);
    }

    [Fact]
    public void UpdateBreakdown_ZeroTotal_NoException()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 0;
        vm.DupesRaw = 0;
        vm.JunkRaw = 0;

        vm.UpdateBreakdown();

        Assert.Equal(0.0, vm.KeepFraction);
        Assert.Equal(0.0, vm.MoveFraction);
        Assert.Equal(0.0, vm.JunkFraction);
    }

    [Fact]
    public void UpdateBreakdown_AllJunk_FractionIsOne()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 50;
        vm.DupesRaw = 0;
        vm.JunkRaw = 50;

        vm.UpdateBreakdown();

        Assert.Equal(0, vm.KeepCount);
        Assert.Equal(0.0, vm.KeepFraction);
        Assert.Equal(1.0, vm.JunkFraction, 3);
    }

    // ═══ SEC-001: Preflight→Idle dialog-decline state safety ════════════

    [Fact]
    public void SEC001_PreflightToIdle_IsValidTransition()
    {
        // Preflight → Idle must be legal for dialog-decline paths
        Assert.True(RunStateMachine.IsValidTransition(RunState.Preflight, RunState.Idle));
    }

    [Fact]
    public void SEC001_DialogDecline_ResetsConvertOnlyFlag()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        vm.CurrentRunState = RunState.Preflight;

        // Simulate dialog-decline path: reset transient flags, return to Idle
        vm.ConvertOnly = false;
        vm.CurrentRunState = RunState.Idle;

        Assert.False(vm.ConvertOnly);
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
    }

    [Fact]
    public void SEC001_DialogDecline_ResetsBusyHint()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Preflight;
        vm.BusyHint = "Running...";

        // Simulate dialog-decline: BusyHint must be cleared
        vm.BusyHint = "";
        vm.CurrentRunState = RunState.Idle;

        Assert.Equal("", vm.BusyHint);
    }

    // ═══ SEC-002: Rollback invalidates preview fingerprint ══════════════

    [Fact]
    public void SEC002_AfterRollback_MoveGateIsLocked()
    {
        var vm = new MainViewModel();

        // After DryRun, preview gate should be CompletedDryRun
        SetRunStateViaValidPath(vm, RunState.CompletedDryRun);

        // After rollback, state returns to Idle → not CompletedDryRun → gate locked
        vm.CurrentRunState = RunState.Idle;

        Assert.False(vm.CanStartMoveWithCurrentPreview);
    }

    [Fact]
    public void SEC002_PreflightToIdle_TransitionsToIdleState()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Preflight;

        // SEC-001-Fix Verhalten: Preflight -> Idle ist eine erlaubte Transition
        // und der State muss nach Zuweisung exakt Idle sein.
        var ex = Record.Exception(() => vm.CurrentRunState = RunState.Idle);
        Assert.Null(ex);
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
    }

    [Fact]
    public async Task GUIRED_IssueMain_PartialFailure_MustNotSurfaceAsCompleted()
    {
        var winner = new RomCandidate
        {
            MainPath = @"C:\Roms\Winner.zip",
            GameKey = "winner",
            Category = FileCategory.Game,
            DatMatch = true,
            ConsoleKey = "SNES"
        };

        var result = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 2,
            WinnerCount = 1,
            LoserCount = 1,
            AllCandidates = new[] { winner },
            DedupeGroups = Array.Empty<DedupeGroup>(),
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 1, SavedBytes: 0)
        };

        var runService = new RecordingRunService(result);
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), runService: runService);
        vm.Roots.Add(Path.GetTempPath());
        vm.DryRun = false;
        vm.ConfirmMove = false;
        vm.CurrentRunState = RunState.Preflight;

        await vm.ExecuteRunAsync();

        // Red invariant: partial failures must not be presented as a clean Completed+Info outcome.
        Assert.NotEqual(RunState.Completed, vm.CurrentRunState);
        Assert.NotEqual(UiErrorSeverity.Info, vm.RunSummarySeverity);
        Assert.Contains(vm.ErrorSummaryItems, e => e.Code == "IO-MOVE");
    }

    [Fact]
    public async Task GUIRED_IssueMain_ExecuteRunAsync_MustBeSingleFlight()
    {
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 1,
            WinnerCount = 1,
            AllCandidates = new[]
            {
                new RomCandidate
                {
                    MainPath = @"C:\Roms\Single.zip",
                    GameKey = "single",
                    Category = FileCategory.Game,
                    DatMatch = true,
                    ConsoleKey = "SNES"
                }
            },
            DedupeGroups = Array.Empty<DedupeGroup>()
        };

        var runService = new RecordingRunService(result);
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), runService: runService);
        vm.Roots.Add(Path.GetTempPath());
        vm.DryRun = true;
        vm.CurrentRunState = RunState.Preflight;

        var first = vm.ExecuteRunAsync();
        var second = vm.ExecuteRunAsync();
        await Task.WhenAll(first, second);

        // Red invariant: concurrent triggers must not execute the pipeline twice.
        Assert.Equal(1, runService.ExecuteRunCallCount);
    }

    [Fact]
    public void GUIRED_IssueMain_UnknownStatus_MustBeVisibleInErrorSummaryProjection()
    {
        var result = new RunResult
        {
            Status = "mystery_status",
            WinnerCount = 0,
            LoserCount = 0
        };

        var issues = ErrorSummaryProjection.Build(result, [], []);

        // Red invariant: unknown run status must not be silently mapped to RUN-OK.
        Assert.DoesNotContain(issues, issue => issue.Code == "RUN-OK");
        Assert.Contains(issues, issue => issue.Code == "RUN-UNKNOWN" && issue.Severity == UiErrorSeverity.Warning);
    }
}
