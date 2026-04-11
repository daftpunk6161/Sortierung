using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ INFRASTRUKTUR & DEPLOYMENT ═════════════════════════════════════

    private async Task StorageTieringAsync()
    {
        var (success, snapshots, collectionIndex) = await TryLoadSnapshotsAsync(30);
        if (!success || collectionIndex is null)
            return;

        using (collectionIndex)
        {
            try
            {
                var insights = await RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 30);
                var trends = await RunHistoryTrendService.LoadTrendHistoryAsync(collectionIndex, 30);

                var sb = new StringBuilder();
                sb.AppendLine(RunHistoryInsightsService.FormatStorageInsightReport(insights));
                sb.AppendLine();
                sb.AppendLine(RunHistoryTrendService.FormatTrendReport(
                    trends,
                    _vm.Loc["Cmd.StorageTiering.TrendsTitle"],
                    _vm.Loc["Cmd.StorageTiering.NoHistory"],
                    _vm.Loc["Cmd.StorageTiering.Current"],
                    _vm.Loc["Cmd.StorageTiering.DeltaFiles"],
                    _vm.Loc["Cmd.StorageTiering.DeltaDuplicates"],
                    _vm.Loc["Cmd.StorageTiering.History"],
                    _vm.Loc["Cmd.StorageTiering.Files"],
                    _vm.Loc["Cmd.StorageTiering.Quality"]));

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText(_vm.Loc["Cmd.StorageTiering.Title"], sb.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                LogWarning("GUI-HISTORY", _vm.Loc.Format("Cmd.StorageTiering.NoHistoryWarning", ex.Message));
                var sb = new StringBuilder();
                sb.AppendLine(_vm.Loc["Cmd.StorageTiering.Title"]);
                sb.AppendLine();
                sb.AppendLine(_vm.Loc["Cmd.StorageTiering.NoHistory"]);

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText(_vm.Loc["Cmd.StorageTiering.Title"], sb.ToString());
            }
        }
    }

    private void NasOptimization()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.NasOptimization.NoRoots"], "WARN"); return; }
        _dialog.ShowText(_vm.Loc["Cmd.NasOptimization.Title"], FeatureService.GetNasInfo(_vm.Roots.ToList()));
    }

    private void PortableMode()
    {
        var isPortable = FeatureService.IsPortableMode();
        var resolvedSettingsDir = AppStoragePathResolver.ResolveRoamingAppDirectory();
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc["Cmd.PortableMode.Title"]);
        sb.AppendLine();
        var modeLabel = isPortable
            ? _vm.Loc["Cmd.PortableMode.ModePortable"]
            : _vm.Loc["Cmd.PortableMode.ModeStandard"];
        sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.ModeLine", modeLabel));
        sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.ProgramDirLine", AppContext.BaseDirectory));
        if (isPortable) sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.SettingsDirLine", resolvedSettingsDir));
        else
        {
            sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.SettingsDirLine", resolvedSettingsDir));
            sb.AppendLine();
            sb.AppendLine(_vm.Loc["Cmd.PortableMode.PortableHint"]);
        }
        _dialog.ShowText(_vm.Loc["Cmd.PortableMode.Title"], sb.ToString());
    }

    private void HardlinkMode()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.RunRequired"], "WARN"); return; }
        var estimate = FeatureService.GetHardlinkEstimate(_vm.LastDedupeGroups);
        var firstRoot = _vm.LastDedupeGroups.FirstOrDefault()?.Winner.MainPath;
        var isNtfs = false;
        if (firstRoot is not null)
        {
            try
            {
                var driveRoot = Path.GetPathRoot(firstRoot);
                if (driveRoot is not null) isNtfs = new DriveInfo(driveRoot).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { /* drive info unavailable — show as not available */ }
            catch (UnauthorizedAccessException) { /* no access to drive info */ }
        }
        var ntfsStatus = isNtfs
            ? _vm.Loc["Cmd.HardlinkMode.NtfsAvailable"]
            : _vm.Loc["Cmd.HardlinkMode.NtfsUnavailable"];
        _dialog.ShowText(
            _vm.Loc["Cmd.HardlinkMode.Title"],
            _vm.Loc.Format("Cmd.HardlinkMode.Body", estimate, ntfsStatus));
    }

    // ═══ WINDOW-LEVEL COMMANDS (require IWindowHost) ════════════════════

    private void CommandPalette()
    {
        var input = _dialog.ShowInputBox(_vm.Loc["Cmd.CommandPalette.SearchPrompt"], _vm.Loc["Cmd.CommandPalette.Title"], string.Empty);
        if (string.IsNullOrWhiteSpace(input)) return;
        var results = FeatureService.SearchCommands(input, _vm.FeatureCommands);
        if (results.Count == 0)
        { _vm.AddLog(_vm.Loc.Format("Cmd.CommandPalette.NotFound", input), "WARN"); return; }

        _dialog.ShowText(_vm.Loc["Cmd.CommandPalette.Title"], FeatureService.BuildCommandPaletteReport(input, results));
        if (results[0].score == 0) ExecuteCommand(results[0].key);
    }

    internal void ExecuteCommand(string key)
    {
        // 1. Try FeatureCommands dictionary (all registered tool commands)
        if (_vm.FeatureCommands.TryGetValue(key, out var featureCmd))
        {
            featureCmd.Execute(null);
            _vm.RecordToolUsage(key);
            return;
        }

        // 2. Fallback: core VM-level shortcuts not in FeatureCommands
        switch (key)
        {
            case "dryrun": if (!_vm.IsBusy) { _vm.DryRun = true; _vm.RunCommand.Execute(null); } break;
            case "move": if (!_vm.IsBusy) { _vm.DryRun = false; _vm.RunCommand.Execute(null); } break;
            case "cancel": _vm.CancelCommand.Execute(null); break;
            case "rollback": _vm.RollbackCommand.Execute(null); break;
            case "theme": _vm.ThemeToggleCommand.Execute(null); break;
            case "clear-log": _vm.ClearLogCommand.Execute(null); break;
            case "settings": _windowHost?.SelectTab(3); break;
            default: _vm.AddLog(_vm.Loc.Format("Cmd.CommandPalette.UnknownCommand", key), "WARN"); break;
        }
    }

    private void ApiServer()
    {
        var apiProject = FeatureService.FindApiProjectPath();
        if (apiProject is not null)
        {
            if (_dialog.Confirm(_vm.Loc["Cmd.ApiServer.StartPrompt"], _vm.Loc["Cmd.ApiServer.Title"]))
            {
                _windowHost?.StartApiProcess(apiProject);
                return;
            }
        }
        else
        {
            _dialog.ShowText(_vm.Loc["Cmd.ApiServer.Title"], _vm.Loc["Cmd.ApiServer.NotFoundMessage"]);
        }
    }

    private void Accessibility()
    {
        if (_windowHost is null) return;
        var isHC = FeatureService.IsHighContrastActive();
        var currentSize = _windowHost.FontSize;

        var input = _dialog.ShowInputBox(
            _vm.Loc.Format(
                "Cmd.Accessibility.Prompt",
                isHC ? _vm.Loc["Cmd.Accessibility.HighContrastActive"] : _vm.Loc["Cmd.Accessibility.HighContrastInactive"],
                currentSize),
            _vm.Loc["Cmd.Accessibility.Title"],
            currentSize.ToString("0"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (double.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, out var newSize) && newSize >= 10 && newSize <= 24)
        {
            _windowHost.FontSize = newSize;
            _vm.AddLog(_vm.Loc.Format("Cmd.Accessibility.FontSizeChanged", newSize), "INFO");
        }
        else
        {
            _vm.AddLog(_vm.Loc.Format("Cmd.Accessibility.InvalidFontSize", input), "WARN");
        }
    }
}
