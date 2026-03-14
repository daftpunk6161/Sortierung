using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.UI.Wpf.ViewModels;
namespace RomCleanup.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ KONVERTIERUNG & HASHING ════════════════════════════════════════

    private void ConversionPipeline()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var est = FeatureService.GetConversionEstimate(_vm.LastCandidates);
        _vm.AddLog($"Konvertierungs-Pipeline: {est.Details.Count} Dateien, Ersparnis ~{FeatureService.FormatSize(est.SavedBytes)}", "INFO");
        _dialog.Info($"Konvertierungs-Pipeline bereit:\n\n{est.Details.Count} Dateien konvertierbar\n" +
            $"Geschätzte Ersparnis: {FeatureService.FormatSize(est.SavedBytes)}\n\n" +
            "Aktiviere 'Konvertierung' und starte einen Move-Lauf.", "Konvertierungs-Pipeline");
    }

    private void NKitConvert()
    {
        var path = _dialog.BrowseFile("NKit-Image wählen", "NKit (*.nkit.iso;*.nkit.gcz;*.nkit)|*.nkit.iso;*.nkit.gcz;*.nkit|Alle (*.*)|*.*");
        if (path is null) return;
        var isNkit = path.Contains(".nkit", StringComparison.OrdinalIgnoreCase);
        _vm.AddLog($"NKit erkannt: {isNkit}, Datei: {Path.GetFileName(path)}", isNkit ? "INFO" : "WARN");
        try
        {
            var runner = new ToolRunnerAdapter(null);
            var nkitPath = runner.FindTool("nkit");
            if (nkitPath is not null)
                _dialog.Info($"NKit-Tool gefunden: {nkitPath}\n\nImage: {Path.GetFileName(path)}\nNKit-Format: {(isNkit ? "Ja" : "Nein")}\n\nKonvertierungs-Anleitung:\n  NKit → ISO: NKit.exe recover <Datei>\n  NKit → RVZ: Erst recover, dann dolphintool convert\n\nEmpfohlenes Zielformat: RVZ (GameCube/Wii)", "NKit-Konvertierung");
            else
                _dialog.Info($"NKit-Tool nicht gefunden.\n\nImage: {Path.GetFileName(path)}\n\nDownload: https://vimm.net/vault/nkit\n\nNach dem Download das Tool in den PATH aufnehmen\noder im Programmverzeichnis ablegen.", "NKit-Konvertierung");
        }
        catch (Exception ex)
        {
            _vm.AddLog($"NKit-Tool-Suche fehlgeschlagen: {ex.Message}", "WARN");
            _dialog.Info($"NKit-Image: {Path.GetFileName(path)}\n\nKonvertierung nach ISO/RVZ erfordert das Tool 'NKit'.\nDownload: https://vimm.net/vault/nkit", "NKit-Konvertierung");
        }
    }

    private void ConvertQueue()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Dateien in der Konvert-Warteschlange.", "WARN"); return; }
        var est = FeatureService.GetConversionEstimate(_vm.LastCandidates);

        var sb = new StringBuilder();
        sb.AppendLine("Konvert-Warteschlange");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"\n  Dateien: {est.Details.Count}");
        sb.AppendLine($"  Quellgröße: {FeatureService.FormatSize(est.TotalSourceBytes)}");
        sb.AppendLine($"  Geschätzte Zielgröße: {FeatureService.FormatSize(est.EstimatedTargetBytes)}");
        sb.AppendLine($"  Ersparnis: {FeatureService.FormatSize(est.SavedBytes)}\n");
        if (est.Details.Count > 0)
        {
            sb.AppendLine($"  {"Datei",-40} {"Quelle",-8} {"Ziel",-8} {"Größe",12}");
            sb.AppendLine($"  {new string('-', 40)} {new string('-', 8)} {new string('-', 8)} {new string('-', 12)}");
            foreach (var d in est.Details)
                sb.AppendLine($"  {d.FileName,-40} {d.SourceFormat,-8} {d.TargetFormat,-8} {FeatureService.FormatSize(d.SourceBytes),12}");
        }
        else
            sb.AppendLine("  Keine konvertierbaren Dateien gefunden.");
        _dialog.ShowText("Konvert-Warteschlange", sb.ToString());
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

    private void ParallelHashing()
    {
        var cores = Environment.ProcessorCount;
        var optimal = Math.Max(1, cores - 1);
        var input = _dialog.ShowInputBox(
            $"CPU-Kerne: {cores}\nAktuell: {optimal} Threads\n\nGewünschte Thread-Anzahl eingeben (1-{cores}):",
            "Parallel-Hashing Konfiguration", optimal.ToString());
        if (string.IsNullOrWhiteSpace(input)) return;
        if (int.TryParse(input, out var threads) && threads >= 1 && threads <= cores * 2)
        {
            Environment.SetEnvironmentVariable("ROMCLEANUP_HASH_THREADS", threads.ToString());
            _vm.AddLog($"Parallel-Hashing: {threads} Threads konfiguriert (experimentell – wird in einer zukünftigen Version unterstützt)", "INFO");
            _dialog.ShowText("Parallel-Hashing", $"Parallel-Hashing Konfiguration\n\nCPU-Kerne: {cores}\nThreads (neu): {threads}\n\nDie Änderung wird beim nächsten Hash-Vorgang wirksam.");
        }
        else
            _vm.AddLog($"Ungültige Thread-Anzahl: {input} (erlaubt: 1-{cores * 2})", "WARN");
    }

    private void GpuHashing()
    {
        var openCl = File.Exists(Path.Combine(Environment.SystemDirectory, "OpenCL.dll"));
        var currentSetting = Environment.GetEnvironmentVariable("ROMCLEANUP_GPU_HASHING") ?? "off";
        var isEnabled = currentSetting.Equals("on", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("GPU-Hashing Konfiguration\n");
        sb.AppendLine($"  OpenCL verfügbar: {(openCl ? "Ja" : "Nein")}");
        sb.AppendLine($"  CPU-Kerne:        {Environment.ProcessorCount}");
        sb.AppendLine($"  Aktueller Status: {(isEnabled ? "AKTIVIERT" : "Deaktiviert")}");
        if (!openCl)
        {
            sb.AppendLine("\n  GPU-Hashing benötigt OpenCL-Treiber.\n  Installiere aktuelle GPU-Treiber für Unterstützung.");
            _dialog.ShowText("GPU-Hashing", sb.ToString());
            return;
        }
        sb.AppendLine("\n  GPU-Hashing kann SHA1/SHA256-Berechnungen\n  um 5-20x beschleunigen (experimentell).");
        _dialog.ShowText("GPU-Hashing", sb.ToString());
        var toggle = isEnabled ? "deaktivieren" : "aktivieren";
        if (_dialog.Confirm($"GPU-Hashing {toggle}?\n\nAktuell: {(isEnabled ? "AN" : "AUS")}", "GPU-Hashing"))
        {
            var newValue = isEnabled ? "off" : "on";
            Environment.SetEnvironmentVariable("ROMCLEANUP_GPU_HASHING", newValue);
            _vm.AddLog($"GPU-Hashing: {(isEnabled ? "deaktiviert" : "aktiviert")} (experimentell – wird in einer zukünftigen Version unterstützt)", "INFO");
        }
    }

}
