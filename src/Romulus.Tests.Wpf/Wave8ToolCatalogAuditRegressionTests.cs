using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave-8 tool-catalog deep-dive audit regressions.
/// Pin the fixes for findings F-T01 (orphan rollback alias keys),
/// F-T02 (Quarantine maturity drift), F-T03 (AutoProfile UI-thread block),
/// F-T07 (RuleEngine / ConversionPipeline maturity), F-T09 (CommandPalette
/// magic settings tab index), and the "every catalog tile must have a
/// registered command" invariant.
/// </summary>
public sealed class Wave8ToolCatalogAuditRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public Wave8ToolCatalogAuditRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Wave8_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    private (FeatureCommandService sut, MainViewModel vm) BuildSut(bool withWindowHost = true)
    {
        var dialog = new StubDialogService();
        var settings = new StubSettingsService();
        var vm = new MainViewModel(new StubThemeService(), dialog, settings);
        var fileSystem = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fileSystem, _ => { }, Path.Combine(_tempDir, "audit-signing.key"));
        var sut = new FeatureCommandService(vm, settings, dialog, fileSystem, auditStore, new HeaderRepairService(fileSystem));
        if (withWindowHost)
            sut.AttachWindowHost(new MinimalWindowHost());
        return (sut, vm);
    }

    private sealed class MinimalWindowHost : IWindowHost
    {
        public double FontSize { get; set; } = 14;
        public void SelectTab(int index) { }
        public void ShowTextDialog(string title, string content) { }
        public void ToggleSystemTray() { }
        public void StartApiProcess(string projectPath) { }
        public void StopApiProcess() { }
    }

    // ── F-T01: Orphan alias keys must be gone ─────────────────────────

    [Fact]
    public void FT01_FeatureCommandKeys_RollbackUndoAndRedoAliases_AreRemoved()
    {
        var fields = typeof(FeatureCommandKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("RollbackUndo", fields);
        Assert.DoesNotContain("RollbackRedo", fields);
    }

    [Fact]
    public void FT01_FeatureCommands_DoesNotRegister_RollbackUndoOrRedoAliases()
    {
        var (sut, vm) = BuildSut();
        sut.RegisterCommands();

        Assert.False(vm.FeatureCommands.ContainsKey("RollbackUndo"));
        Assert.False(vm.FeatureCommands.ContainsKey("RollbackRedo"));
    }

    [Fact]
    public void FT01_RollbackHistory_KeysHaveKeyboardShortcutBinding()
    {
        // Wave-8 wires RollbackHistoryBack/Forward into Window.InputBindings
        // so the registered commands are reachable from the keyboard
        // (Ctrl+Shift+Z back, Ctrl+Y forward) instead of being orphaned.
        var xamlPath = LocateRepoFile("src/Romulus.UI.Wpf/MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("FeatureCommands[RollbackHistoryBack]", xaml);
        Assert.Contains("FeatureCommands[RollbackHistoryForward]", xaml);
    }

    // ── F-T02: Quarantine is preview-only -> Guided ────────────────────

    [Fact]
    public void FT02_QuarantineTile_MaturityIsGuided_NotProduction()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var quarantine = vm.ToolItems.Single(t => t.Key == FeatureCommandKeys.Quarantine);

        Assert.Equal(ToolMaturity.Guided, quarantine.Maturity);
    }

    // ── F-T03: AutoProfile must not block UI thread ───────────────────

    [Fact]
    public void FT03_AutoProfile_IsAsyncRelayCommand_NotSyncRelayCommand()
    {
        var (sut, vm) = BuildSut();
        sut.RegisterCommands();

        var command = vm.FeatureCommands[FeatureCommandKeys.AutoProfile];
        Assert.IsAssignableFrom<IAsyncRelayCommand>(command);
    }

    // ── F-T07: Maturity correction for fully implemented tools ────────

    [Fact]
    public void FT07_RuleEngineTile_MaturityIsProduction()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var tile = vm.ToolItems.Single(t => t.Key == FeatureCommandKeys.RuleEngine);

        Assert.Equal(ToolMaturity.Production, tile.Maturity);
    }

    [Fact]
    public void FT07_ConversionPipelineTile_MaturityIsProduction()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var tile = vm.ToolItems.Single(t => t.Key == FeatureCommandKeys.ConversionPipeline);

        Assert.Equal(ToolMaturity.Production, tile.Maturity);
    }

    // ── F-T09: CommandPalette must not use a magic tab index ──────────

    [Fact]
    public void FT09_CommandPalette_SettingsFallback_DoesNotUseMagicNumber()
    {
        // T-W6 cleanup: original Infra-Partial wurde konsolidiert; Pin scannt jetzt
        // jede FeatureCommandService-Partial nach dem urspruenglichen Magic-Number-Anti-Pattern.
        var servicesDir = LocateRepoDirectory("src/Romulus.UI.Wpf/Services");
        var partials = Directory.GetFiles(servicesDir, "FeatureCommandService*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(partials);

        foreach (var path in partials)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("SelectTab(3)", source);
        }
    }

    // ── Tile <-> Registration invariant ───────────────────────────────

    [Fact]
    public void EveryCatalogTile_HasRegisteredFeatureCommand_AfterBootstrap()
    {
        var (sut, vm) = BuildSut();
        sut.RegisterCommands();
        var tools = new ToolsViewModel(new LocalizationService());

        var missing = tools.ToolItems
            .Select(t => t.Key)
            .Where(key => !vm.FeatureCommands.ContainsKey(key))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Catalog tiles without registered FeatureCommand: {string.Join(", ", missing)}");
    }

    // ── Helper ────────────────────────────────────────────────────────

    private static string LocateRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Repo file not found: {relative}");
    }

    private static string LocateRepoDirectory(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException($"Repo directory not found: {relative}");
    }
}
