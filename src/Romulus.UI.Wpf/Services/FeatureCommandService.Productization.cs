using System.IO;
using System.Text;
using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
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

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized)> TryCreateCurrentMaterializedRunConfigurationAsync()
    {
        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            var materialized = await _vm.RunConfigurationMaterializer.MaterializeAsync(
                _vm.BuildCurrentRunConfigurationDraft(),
                _vm.BuildCurrentRunConfigurationExplicitness(),
                settings);
            return (true, materialized);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-CONFIG", $"Run-Konfiguration ungueltig: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized)> TryCreateSelectedMaterializedRunConfigurationAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedWorkflowScenarioId) &&
            string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
        {
            LogWarning("GUI-PROFILE", "Kein Workflow oder Profil ausgewaehlt.");
            return (false, null);
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

            var materialized = await _vm.RunConfigurationMaterializer.MaterializeAsync(
                selectionDraft,
                new RunConfigurationExplicitness(),
                settings,
                baselineDraft: baselineDraft);
            return (true, materialized);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", $"Auswahl konnte nicht materialisiert werden: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized, IRunEnvironment? Environment)> TryCreateCurrentRunEnvironmentAsync()
    {
        var (success, materialized) = await TryCreateCurrentMaterializedRunConfigurationAsync();
        if (!success || materialized is null)
            return (false, null, null);

        try
        {
            var environment = new RunEnvironmentFactory().Create(materialized.Options);
            return (true, materialized, environment);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-ENV", $"Run-Umgebung konnte nicht erstellt werden: {ex.Message}");
            return (false, null, null);
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

    private async Task<RunProfileDocument?> TryGetSelectedProfileDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
            return null;

        try
        {
            return await _vm.RunProfileService.TryGetAsync(_vm.SelectedRunProfileId);
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

    private async Task<(bool Success, IReadOnlyList<CollectionRunSnapshot> Snapshots, LiteDbCollectionIndex? CollectionIndex)> TryLoadSnapshotsAsync(int limit)
    {
        try
        {
            var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
            var snapshots = await collectionIndex.ListRunSnapshotsAsync(limit);
            return (true, snapshots, collectionIndex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogWarning("GUI-HISTORY", $"Run-Historie nicht verfuegbar: {ex.Message}");
            return (false, Array.Empty<CollectionRunSnapshot>(), null);
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
}
