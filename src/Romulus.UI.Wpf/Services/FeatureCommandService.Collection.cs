using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

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

    private void CollectionMerge()
    {
        var leftRoot = _dialog.BrowseFolder("Linke Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(leftRoot))
            return;

        var rightRoot = _dialog.BrowseFolder("Rechte Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(rightRoot))
            return;

        var targetRoot = _dialog.BrowseFolder("Ziel-Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(targetRoot))
            return;

        var moveDecision = _dialog.YesNoCancel(
            "Soll Romulus die gewaehlten Quellen nach erfolgreicher Verifikation in das Ziel verschieben? 'Nein' erzeugt einen Copy-Merge.",
            "Merge-Modus");
        if (moveDecision == ConfirmResult.Cancel)
            return;

        var mergeRequest = new CollectionMergeRequest
        {
            CompareRequest = BuildCollectionCompareRequest(leftRoot, rightRoot),
            TargetRoot = targetRoot,
            AllowMoves = moveDecision == ConfirmResult.Yes
        };

        using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath(), msg => _vm.AddLog(msg, "INFO"));
        var fileSystem = new FileSystemAdapter();
        var build = CollectionMergeService.BuildPlanAsync(collectionIndex, fileSystem, mergeRequest).GetAwaiter().GetResult();
        if (!build.CanUse || build.Plan is null)
        {
            _vm.AddLog($"[CollectionMerge] Nicht verfuegbar: {build.Reason}", "WARN");
            _dialog.Info(build.Reason ?? "Collection Merge nicht verfuegbar.", "Collection Merge");
            return;
        }

        var planText = FormatCollectionMergePlan(build.Plan);
        _dialog.ShowText("Collection Merge Plan", planText);

        if (!_dialog.DangerConfirm(
                "Collection Merge anwenden",
                "Der Merge-Plan wird jetzt mit Audit-Trail ausgefuehrt. Ohne Bestaetigung bleibt es beim Preview.",
                "MERGE",
                "Merge anwenden"))
        {
            return;
        }

        var auditStore = new AuditCsvStore(fileSystem, msg => _vm.AddLog(msg, "INFO"));
        var applyResult = CollectionMergeService.ApplyAsync(
            collectionIndex,
            fileSystem,
            auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = mergeRequest,
                AuditPath = CollectionMergeService.CreateDefaultAuditPath(targetRoot)
            }).GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(applyResult.BlockedReason))
        {
            _vm.AddLog($"[CollectionMerge] Apply blockiert: {applyResult.BlockedReason}", "WARN");
            _dialog.Info(applyResult.BlockedReason, "Collection Merge");
            return;
        }

        _dialog.ShowText("Collection Merge Ergebnis", FormatCollectionMergeApply(applyResult));
    }

    private CollectionCompareRequest BuildCollectionCompareRequest(string leftRoot, string rightRoot)
    {
        var extensions = _vm.GetSelectedExtensions();
        if (extensions.Length == 0)
            extensions = RunOptions.DefaultExtensions;

        return new CollectionCompareRequest
        {
            Left = new CollectionSourceScope
            {
                SourceId = "left",
                Label = Path.GetFileName(leftRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Roots = [leftRoot],
                Extensions = extensions
            },
            Right = new CollectionSourceScope
            {
                SourceId = "right",
                Label = Path.GetFileName(rightRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Roots = [rightRoot],
                Extensions = extensions
            },
            Limit = 500
        };
    }

    internal static string FormatCollectionMergePlan(CollectionMergePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Merge Plan");
        sb.AppendLine();
        sb.AppendLine($"Target: {plan.Request.TargetRoot}");
        sb.AppendLine($"AllowMoves: {plan.Request.AllowMoves}");
        sb.AppendLine();
        sb.AppendLine($"Total: {plan.Summary.TotalEntries}");
        sb.AppendLine($"Copy: {plan.Summary.CopyToTarget}");
        sb.AppendLine($"Move: {plan.Summary.MoveToTarget}");
        sb.AppendLine($"Keep existing: {plan.Summary.KeepExistingTarget}");
        sb.AppendLine($"Skip duplicate: {plan.Summary.SkipAsDuplicate}");
        sb.AppendLine($"Review: {plan.Summary.ReviewRequired}");
        sb.AppendLine($"Blocked: {plan.Summary.Blocked}");
        sb.AppendLine();
        foreach (var entry in plan.Entries.Take(25))
            sb.AppendLine($"[{entry.Decision}] {entry.DiffKey} -> {entry.TargetPath ?? "-"} ({entry.ReasonCode})");

        if (plan.Entries.Count > 25)
            sb.AppendLine($"... und {plan.Entries.Count - 25} weitere Eintraege");

        return sb.ToString();
    }

    internal static string FormatCollectionMergeApply(CollectionMergeApplyResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Merge Ergebnis");
        sb.AppendLine();
        sb.AppendLine($"Applied: {result.Summary.Applied}");
        sb.AppendLine($"Copied: {result.Summary.Copied}");
        sb.AppendLine($"Moved: {result.Summary.Moved}");
        sb.AppendLine($"Keep existing: {result.Summary.KeptExistingTarget}");
        sb.AppendLine($"Skip duplicate: {result.Summary.SkippedAsDuplicate}");
        sb.AppendLine($"Review: {result.Summary.ReviewRequired}");
        sb.AppendLine($"Blocked: {result.Summary.Blocked}");
        sb.AppendLine($"Failed: {result.Summary.Failed}");
        if (!string.IsNullOrWhiteSpace(result.AuditPath))
            sb.AppendLine($"Audit: {result.AuditPath}");
        sb.AppendLine();
        foreach (var entry in result.Entries.Take(25))
            sb.AppendLine($"[{entry.Outcome}] {entry.DiffKey} -> {entry.TargetPath ?? "-"} ({entry.ReasonCode})");

        if (result.Entries.Count > 25)
            sb.AppendLine($"... und {result.Entries.Count - 25} weitere Eintraege");

        return sb.ToString();
    }

}
