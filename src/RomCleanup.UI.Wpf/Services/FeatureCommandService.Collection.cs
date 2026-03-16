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
    // ═══ SAMMLUNGSVERWALTUNG ════════════════════════════════════════════

    private void CollectionManager()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var byConsole = _vm.LastCandidates.GroupBy(c => FeatureService.DetectConsoleFromPath(c.MainPath))
            .OrderByDescending(g => g.Count()).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Smart Collection Manager\n");
        sb.AppendLine($"Gesamt: {_vm.LastCandidates.Count} ROMs\n");
        foreach (var g in byConsole)
            sb.AppendLine($"  {g.Key,-20} {g.Count(),5} ROMs");
        _dialog.ShowText("Smart Collection", sb.ToString());
    }

    private void CloneListViewer()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Keine Gruppen vorhanden.", "WARN"); return; }
        _dialog.ShowText("Clone-Liste", FeatureService.BuildCloneTree(_vm.LastDedupeGroups));
    }

    private void CoverScraper()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var coverDir = _dialog.BrowseFolder("Cover-Ordner wählen (enthält Cover-Bilder)");
        if (coverDir is null) return;

        var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        var coverFiles = Directory.GetFiles(coverDir, "*.*", SearchOption.AllDirectories)
            .Where(f => imageExts.Contains(Path.GetExtension(f))).ToList();
        if (coverFiles.Count == 0)
        { _dialog.Info($"Keine Cover-Bilder gefunden in:\n{coverDir}", "Cover-Scraper"); return; }

        var gameKeys = _vm.LastCandidates
            .Select(c => RomCleanup.Core.GameKeys.GameKeyNormalizer.Normalize(Path.GetFileNameWithoutExtension(c.MainPath)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = new List<string>();
        var unmatched = new List<string>();
        foreach (var cover in coverFiles)
        {
            var coverName = Path.GetFileNameWithoutExtension(cover);
            var normalizedCover = RomCleanup.Core.GameKeys.GameKeyNormalizer.Normalize(coverName);
            if (gameKeys.Contains(normalizedCover)) matched.Add(coverName);
            else unmatched.Add(coverName);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Cover-Scraper Ergebnis\n");
        sb.AppendLine($"  Cover-Ordner: {coverDir}");
        sb.AppendLine($"  Gefundene Bilder: {coverFiles.Count}");
        sb.AppendLine($"  ROMs in Sammlung: {gameKeys.Count}");
        sb.AppendLine($"\n  Zugeordnet:    {matched.Count}");
        sb.AppendLine($"  Nicht zugeordnet: {unmatched.Count}");
        sb.AppendLine($"  Ohne Cover:    {gameKeys.Count - matched.Count}");
        if (matched.Count > 0)
        {
            sb.AppendLine($"\n  --- Zugeordnet (erste {Math.Min(15, matched.Count)}) ---");
            foreach (var m in matched.Take(15)) sb.AppendLine($"    ✓ {m}");
        }
        if (unmatched.Count > 0)
        {
            sb.AppendLine($"\n  --- Nicht zugeordnet (erste {Math.Min(15, unmatched.Count)}) ---");
            foreach (var u in unmatched.Take(15)) sb.AppendLine($"    ? {u}");
        }
        _dialog.ShowText("Cover-Scraper", sb.ToString());
        _vm.AddLog($"Cover-Scan: {matched.Count} zugeordnet, {unmatched.Count} nicht zugeordnet", "INFO");
    }

    private void GenreClassification()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var genres = _vm.LastCandidates.GroupBy(c => FeatureService.ClassifyGenre(c.GameKey))
            .OrderByDescending(g => g.Count()).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Genre-Klassifikation\n");
        foreach (var g in genres)
        {
            sb.AppendLine($"  {g.Key,-20} {g.Count(),5} Spiele");
            foreach (var item in g.Take(3))
                sb.AppendLine($"    • {Path.GetFileNameWithoutExtension(item.MainPath)}");
        }
        _dialog.ShowText("Genre-Klassifikation", sb.ToString());
    }

    private void PlaytimeTracker()
    {
        var dir = _dialog.BrowseFolder("RetroArch-Spielzeit-Ordner wählen (runtime_log)");
        if (dir is null) return;
        var lrtlFiles = Directory.GetFiles(dir, "*.lrtl", SearchOption.AllDirectories);
        if (lrtlFiles.Length == 0)
        { _dialog.Info("Keine .lrtl Spielzeit-Dateien gefunden.", "Spielzeit-Tracker"); return; }
        var sb = new StringBuilder();
        sb.AppendLine($"Spielzeit-Tracker: {lrtlFiles.Length} Dateien\n");
        sb.AppendLine("Hinweis: Es werden nur RetroArch .lrtl-Dateien unterstützt.\n");
        foreach (var f in lrtlFiles.Take(20))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var lines = File.ReadAllLines(f);
            sb.AppendLine($"  {name}: {lines.Length} Einträge");
        }
        _dialog.ShowText("Spielzeit-Tracker", sb.ToString());
    }

    private void CollectionSharing()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Daten zum Teilen.", "WARN"); return; }
        var path = _dialog.SaveFile("Sammlung exportieren", "JSON (*.json)|*.json|HTML (*.html)|*.html", "meine-sammlung.json");
        if (path is null) return;
        var entries = _vm.LastCandidates.Where(c => c.Category == "GAME")
            .Select(c => new { Name = Path.GetFileNameWithoutExtension(c.MainPath), c.Region, c.Extension, SizeMB = c.SizeBytes / 1048576.0 }).ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        _vm.AddLog($"Sammlung exportiert: {path} ({entries.Count} Spiele, keine Pfade/Hashes)", "INFO");
    }

    private void VirtualFolderPreview()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        _dialog.ShowText("Virtuelle Ordner", FeatureService.BuildVirtualFolderPreview(_vm.LastCandidates));
    }

}
