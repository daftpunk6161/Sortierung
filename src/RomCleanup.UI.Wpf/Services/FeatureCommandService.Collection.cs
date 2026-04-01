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
        var byConsole = _vm.LastCandidates.GroupBy(FeatureService.ResolveConsoleLabel)
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

    private void VirtualFolderPreview()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        _dialog.ShowText("Virtuelle Ordner", FeatureService.BuildVirtualFolderPreview(_vm.LastCandidates));
    }

}
