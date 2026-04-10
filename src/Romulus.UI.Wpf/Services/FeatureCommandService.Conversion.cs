using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ KONVERTIERUNG & HASHING ════════════════════════════════════════

    private void ConversionPipeline()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var advisor = FeatureService.GetConversionAdvisor(_vm.LastCandidates);
        var convertibleFiles = 0;
        foreach (var item in advisor.Consoles)
            convertibleFiles += item.FileCount;

        if (convertibleFiles == 0)
        {
            _vm.AddLog("Konvertierungs-Advisor: keine konvertierbaren Dateien gefunden.", "INFO");
            _dialog.Info("Keine konvertierbaren Dateien gefunden.", "Konvertierungs-Advisor");
            return;
        }

        _vm.AddLog($"Konvertierungs-Advisor: {convertibleFiles} Dateien, Ersparnis ~{FeatureService.FormatSize(advisor.SavedBytes)}", "INFO");

        var sb = new StringBuilder();
        sb.AppendLine("Konvertierungs-Advisor");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Konvertierbare Dateien: {convertibleFiles}");
        sb.AppendLine($"Gesamtgroesse vorher: {FeatureService.FormatSize(advisor.TotalSourceBytes)}");
        sb.AppendLine($"Gesamtgroesse nachher: {FeatureService.FormatSize(advisor.EstimatedTargetBytes)}");
        sb.AppendLine($"Gesamtersparnis: {FeatureService.FormatSize(advisor.SavedBytes)}");
        sb.AppendLine();
        sb.AppendLine("Einsparung pro Konsole");
        sb.AppendLine(new string('-', 56));
        foreach (var console in advisor.Consoles)
        {
            sb.AppendLine(
                $"{console.ConsoleKey,-14} Dateien: {console.FileCount,3}  Vorher: {FeatureService.FormatSize(console.SourceBytes),9}  Nachher: {FeatureService.FormatSize(console.EstimatedBytes),9}  Sparen: {FeatureService.FormatSize(console.SavedBytes),9}");
        }

        if (advisor.Recommendations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Empfehlungen");
            sb.AppendLine(new string('-', 56));
            foreach (var recommendation in advisor.Recommendations)
                sb.AppendLine($"- {recommendation}");
        }

        sb.AppendLine();
        sb.AppendLine("Aktiviere 'Konvertierung' und starte einen Move-Lauf.");
        _dialog.ShowText("Konvertierungs-Advisor", sb.ToString());
    }

    private void ConversionVerify()
    {
        var dir = _dialog.BrowseFolder("Konvertierte Dateien prüfen");
        if (dir is null) return;
        var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".chd" or ".rvz" or ".7z").ToList();
        var (passed, failed, missing) = FeatureService.VerifyConversions(files);
        _dialog.ShowText("Konvertierung verifizieren", $"Verifizierung: {dir}\n\n" +
            $"Bestanden: {passed}\nFehlgeschlagen: {failed}\nFehlend: {missing}\nGesamt: {files.Count}");
    }

    private void FormatPriority()
    {
        _dialog.ShowText("Format-Priorität", FeatureService.FormatFormatPriority());
    }

}
