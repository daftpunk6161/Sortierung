using System.IO;
using System.Text;
using Romulus.Infrastructure.FileSystem;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ EXPORT & INTEGRATION ═══════════════════════════════════════════

    private void HtmlReport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var path = _dialog.SaveFile("HTML-Report speichern", "HTML (*.html)|*.html", "report.html");
        if (!TryResolveSafeOutputPath(path, "HTML-Report", out var safePath)) return;

        try
        {
            // Wave-2 F-07: report writing centralised in IResultExportService.
            var result = _resultExport.WriteHtmlReport(safePath, _vm);
            if (!result.Success) return;

            _vm.AddLog($"Report erstellt: {result.Path} (Im Browser drucken → PDF) [{result.ChannelUsed}]", "INFO");
            TryOpenWithShell(result.Path, "Report");
        }
        catch (Exception ex) { LogError("GUI-REPORT", $"Report-Fehler: {ex.Message}"); }
    }

    private void DatImport()
    {
        var path = _dialog.BrowseFile("DAT-Datei importieren (ClrMamePro, RomVault, Logiqx)",
            "DAT (*.dat;*.xml)|*.dat;*.xml|Alle (*.*)|*.*");
        if (path is null) return;
        _vm.AddLog($"DAT-Import: {Path.GetFileName(path)}", "INFO");
        if (string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _dialog.Error("DAT-Root ist nicht konfiguriert. Bitte zuerst den DAT-Root-Ordner setzen.", "DAT-Import"); return; }
        try
        {
            var safeName = Path.GetFileName(path);
            var targetPath = Path.GetFullPath(Path.Combine(_vm.DatRoot, safeName));
            if (!targetPath.StartsWith(Path.GetFullPath(_vm.DatRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { _vm.AddLog("DAT-Import blockiert: Pfad außerhalb des DatRoot.", "ERROR"); return; }
            if (!TryResolveSafeOutputPath(targetPath, "DAT-Import", out var safeTargetPath))
                return;

            AtomicFileWriter.CopyFile(path, safeTargetPath, overwrite: true);
            _vm.AddLog($"DAT importiert nach: {safeTargetPath}", "INFO");
            _dialog.Info($"DAT erfolgreich importiert:\n\n  Quelle: {path}\n  Ziel: {safeTargetPath}", "DAT-Import");
        }
        catch (Exception ex) { LogError("DAT-IMPORT", $"DAT-Import fehlgeschlagen: {ex.Message}"); }
    }

    private void ExportDuplicateCsv()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        {
            _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN");
            return;
        }

        var path = _dialog.SaveFile(_vm.Loc["Cmd.DupeExportTitle"], _vm.Loc["Cmd.FilterCsv"], "duplikate.csv");
        if (!TryResolveSafeOutputPath(path, "Duplikat-CSV-Export", out var safePath))
            return;

        var losers = _vm.LastDedupeGroups.SelectMany(static group => group.Losers).ToList();
        var dupeCsv = FeatureService.ExportCollectionCsv(losers);
        AtomicFileWriter.WriteAllText(safePath, dupeCsv, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.DupeExported", safePath, losers.Count), "INFO");
    }
}
