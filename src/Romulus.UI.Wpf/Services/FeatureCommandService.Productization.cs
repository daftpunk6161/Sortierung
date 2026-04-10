using System.IO;
using System.Text;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;

namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        WriteIndented = true
    };

    private bool TryCreateCurrentMaterializedRunConfiguration(out MaterializedRunConfiguration? materialized)
    {
        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            materialized = _vm.RunConfigurationMaterializer.MaterializeAsync(
                _vm.BuildCurrentRunConfigurationDraft(),
                _vm.BuildCurrentRunConfigurationExplicitness(),
                settings).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            materialized = null;
            LogWarning("GUI-CONFIG", $"Run-Konfiguration ungueltig: {ex.Message}");
            return false;
        }
    }

    private bool TryCreateSelectedMaterializedRunConfiguration(out MaterializedRunConfiguration? materialized)
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedWorkflowScenarioId) &&
            string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
        {
            materialized = null;
            LogWarning("GUI-PROFILE", "Kein Workflow oder Profil ausgewaehlt.");
            return false;
        }

        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            var baselineDraft = _vm.BuildCurrentRunConfigurationDraft(includeSelections: false);
            var selectionDraft = new RunConfigurationDraft
            {
                Roots = baselineDraft.Roots,
                WorkflowScenarioId = _vm.SelectedWorkflowScenarioId,
                ProfileId = _vm.SelectedRunProfileId
            };

            materialized = _vm.RunConfigurationMaterializer.MaterializeAsync(
                selectionDraft,
                new RunConfigurationExplicitness(),
                settings,
                baselineDraft: baselineDraft).GetAwaiter().GetResult();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            materialized = null;
            LogWarning("GUI-PROFILE", $"Auswahl konnte nicht materialisiert werden: {ex.Message}");
            return false;
        }
    }

    private bool TryCreateCurrentRunEnvironment(
        out MaterializedRunConfiguration? materialized,
        out IRunEnvironment? environment)
    {
        environment = null;
        if (!TryCreateCurrentMaterializedRunConfiguration(out materialized) || materialized is null)
            return false;

        try
        {
            environment = new RunEnvironmentFactory().Create(materialized.Options);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-ENV", $"Run-Umgebung konnte nicht erstellt werden: {ex.Message}");
            return false;
        }
    }

    private bool TryCopyToClipboard(string text, string successMessage)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            _vm.AddLog(successMessage, "INFO");
            return true;
        }
        catch (Exception ex)
        {
            LogWarning("GUI-CLIPBOARD", $"Zwischenablage nicht verfuegbar: {ex.Message}");
            return false;
        }
    }

    private RunProfileDocument? TryGetSelectedProfileDocument()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
            return null;

        try
        {
            return _vm.RunProfileService.TryGetAsync(_vm.SelectedRunProfileId).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", $"Profil konnte nicht geladen werden: {ex.Message}");
            return null;
        }
    }

    private bool TryPromptProfileDocument(out RunProfileDocument? document)
    {
        document = null;

        var defaultName = !string.IsNullOrWhiteSpace(_vm.SelectedRunProfileName) && _vm.HasSelectedRunProfile
            ? _vm.SelectedRunProfileName
            : _vm.ProfileName;
        var inputName = _dialog.ShowInputBox(
            "Profilname eingeben:",
            "Profil speichern",
            string.IsNullOrWhiteSpace(defaultName) ? "Custom Profile" : defaultName);
        if (string.IsNullOrWhiteSpace(inputName))
            return false;

        var defaultDescription = _vm.HasSelectedRunProfile ? _vm.SelectedRunProfileDescription : string.Empty;
        var inputDescription = _dialog.ShowInputBox(
            "Optionale Beschreibung eingeben:",
            "Profil speichern",
            defaultDescription);

        var profileName = inputName.Trim();
        var profileId = NormalizeProfileId(profileName);
        document = _vm.BuildCurrentRunProfileDocument(profileId, profileName, inputDescription);
        return true;
    }

    internal static string NormalizeProfileId(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-', '.', '_');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "custom-profile";

        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private bool TryLoadSnapshots(
        int limit,
        out IReadOnlyList<CollectionRunSnapshot> snapshots,
        out LiteDbCollectionIndex? collectionIndex)
    {
        snapshots = Array.Empty<CollectionRunSnapshot>();
        collectionIndex = null;

        try
        {
            collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
            snapshots = collectionIndex.ListRunSnapshotsAsync(limit).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            collectionIndex?.Dispose();
            LogWarning("GUI-HISTORY", $"Run-Historie nicht verfuegbar: {ex.Message}");
            collectionIndex = null;
            return false;
        }
    }

    internal static string BuildRunSnapshotChoicePrompt(IReadOnlyList<CollectionRunSnapshot> snapshots)
    {
        var lines = new List<string>
        {
            "Run-IDs fuer Vergleich eingeben (\"aktuell alt\").",
            "Leer lassen, um die zwei neuesten Runs zu vergleichen.",
            string.Empty,
            "Neueste Snapshots:"
        };

        foreach (var snapshot in snapshots.Take(5))
            lines.Add($"  {snapshot.RunId}  [{snapshot.CompletedUtc:yyyy-MM-dd HH:mm}] {snapshot.Mode} {snapshot.Status}");

        return string.Join(Environment.NewLine, lines);
    }

    internal static IReadOnlyList<string> ResolveComparisonPair(
        string? input,
        IReadOnlyList<CollectionRunSnapshot> snapshots)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [snapshots[0].RunId, snapshots[1].RunId];

        var parts = input
            .Split([' ', ';', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return parts.Length == 2 ? parts : [snapshots[0].RunId, snapshots[1].RunId];
    }

    private bool TryLoadFrontendExportResult(
        string frontend,
        string outputPath,
        string defaultCollectionName,
        out FrontendExportResult? result)
    {
        result = null;

        if (!TryCreateCurrentRunEnvironment(out var materialized, out var environment) || materialized is null || environment is null)
            return false;

        using (environment)
        {
            try
            {
                result = FrontendExportService.ExportAsync(
                    new FrontendExportRequest(
                        frontend,
                        outputPath,
                        defaultCollectionName,
                        materialized.Options.Roots.ToArray(),
                        materialized.Options.Extensions.ToArray()),
                    environment.FileSystem,
                    environment.CollectionIndex,
                    environment.EnrichmentFingerprint,
                    runCandidates: _vm.LastCandidates.Count > 0 ? _vm.LastCandidates.ToArray() : null).GetAwaiter().GetResult();
                return true;
            }
            catch (InvalidOperationException ex)
            {
                LogWarning("GUI-EXPORT", $"Export konnte nicht erstellt werden: {ex.Message}");
                return false;
            }
        }
    }

    internal static string FormatFrontendExportSummary(FrontendExportResult exportResult)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Frontend-Export: {exportResult.Frontend}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine($"Quelle: {exportResult.Source}");
        sb.AppendLine($"Spiele: {exportResult.GameCount}");
        sb.AppendLine();

        foreach (var artifact in exportResult.Artifacts)
            sb.AppendLine($"  {artifact.Label,-24} {artifact.ItemCount,5} -> {artifact.Path}");

        return sb.ToString();
    }
}
