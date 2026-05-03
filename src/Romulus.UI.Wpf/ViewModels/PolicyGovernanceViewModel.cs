using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Policy;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Policy;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Time;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

public sealed partial class PolicyGovernanceViewModel : ObservableObject
{
    private readonly IPolicyEngine _policyEngine;
    private readonly ICollectionIndex? _collectionIndex;
    private readonly IDialogService _dialog;
    private readonly ITimeProvider _timeProvider;
    private readonly object _violationsLock = new();

    public PolicyGovernanceViewModel(
        IPolicyEngine? policyEngine = null,
        ICollectionIndex? collectionIndex = null,
        IDialogService? dialog = null,
        ITimeProvider? timeProvider = null)
    {
        _policyEngine = policyEngine ?? new PolicyEngine();
        _collectionIndex = collectionIndex;
        _dialog = dialog ?? new WpfDialogService();
        _timeProvider = timeProvider ?? new SystemTimeProvider();

        BindingOperations.EnableCollectionSynchronization(Violations, _violationsLock);
        ViolationsView = CollectionViewSource.GetDefaultView(Violations);
        ViolationsView.SortDescriptions.Add(new SortDescription(nameof(PolicyRuleViolation.Severity), ListSortDirection.Ascending));
        ViolationsView.SortDescriptions.Add(new SortDescription(nameof(PolicyRuleViolation.RuleId), ListSortDirection.Ascending));
        ViolationsView.SortDescriptions.Add(new SortDescription(nameof(PolicyRuleViolation.Path), ListSortDirection.Ascending));
        LoadEuPreferredExample();
    }

    public ObservableCollection<PolicyRuleViolation> Violations { get; } = [];
    public ICollectionView ViolationsView { get; }

    [ObservableProperty]
    private string _policyText = "";

    [ObservableProperty]
    private string _rootsText = "";

    [ObservableProperty]
    private string _extensionsText = string.Join(",", RunOptions.DefaultExtensions);

    [ObservableProperty]
    private string _statusText = "Policy bereit.";

    [ObservableProperty]
    private string _lastError = "";

    [ObservableProperty]
    private string _policyFingerprint = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isCompliant;

    public int ViolationCount => Violations.Count;

    [RelayCommand]
    private void LoadPolicyFile()
    {
        var path = _dialog.BrowseFile("Policy-Datei laden", "Policy (*.json;*.yaml;*.yml)|*.json;*.yaml;*.yml|Alle Dateien|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            PolicyText = File.ReadAllText(path);
            LastError = "";
            StatusText = $"Policy geladen: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Policy konnte nicht geladen werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadEuPreferredExample()
    {
        PolicyText = """
            id: eu-preferred
            name: EU bevorzugt
            preferredRegions: [EU, Europe]
            """;
        StatusText = "Beispiel geladen: EU bevorzugt";
        LastError = "";
    }

    [RelayCommand]
    private void LoadNoDemosExample()
    {
        PolicyText = """
            id: no-demos
            name: Keine Demos
            deniedTitleTokens:
              - Demo
              - Prototype
              - Sample
            """;
        StatusText = "Beispiel geladen: Keine Demos";
        LastError = "";
    }

    [RelayCommand]
    private void LoadAllZipExample()
    {
        PolicyText = """
            id: all-zip
            name: Alle ZIP
            allowedExtensions: [.zip]
            """;
        StatusText = "Beispiel geladen: Alle ZIP";
        LastError = "";
    }

    [RelayCommand]
    private async Task ValidateAsync()
    {
        if (_collectionIndex is null)
        {
            LastError = "Collection Index ist nicht verfuegbar.";
            return;
        }

        var roots = ParseList(RootsText, ';');
        if (roots.Length == 0)
        {
            LastError = "Mindestens ein Root-Pfad ist erforderlich.";
            return;
        }

        IsBusy = true;
        LastError = "";
        try
        {
            var policy = PolicyDocumentLoader.Parse(PolicyText);
            var extensions = ParseList(ExtensionsText, ',');
            if (extensions.Length == 0)
                extensions = RunOptions.DefaultExtensions.ToArray();

            var entries = await _collectionIndex.ListEntriesInScopeAsync(roots, extensions).ConfigureAwait(true);
            var snapshot = LibrarySnapshotProjection.FromCollectionIndex(
                entries,
                roots,
                _timeProvider.UtcNow.UtcDateTime);
            var fingerprint = PolicyDocumentLoader.ComputeFingerprint(PolicyText);
            var report = _policyEngine.Validate(snapshot, policy, fingerprint);

            Violations.Clear();
            foreach (var violation in report.Violations)
                Violations.Add(violation);
            ViolationsView.Refresh();

            PolicyFingerprint = report.PolicyFingerprint;
            IsCompliant = report.IsCompliant;
            StatusText = report.IsCompliant
                ? $"Policy erfuellt. {snapshot.Summary.TotalEntries} Eintraege geprueft."
                : $"{report.Violations.Length} Policy-Verstoesse in {snapshot.Summary.TotalEntries} Eintraegen.";
            OnPropertyChanged(nameof(ViolationCount));
        }
        catch (FormatException ex)
        {
            LastError = $"Policy ungueltig: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Policy-Validierung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ExportReport()
    {
        var path = _dialog.SaveFile(
            "Policy-Report exportieren",
            "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
            "policy-validation.json");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var report = new PolicyValidationReport
        {
            PolicyFingerprint = PolicyFingerprint,
            GeneratedUtc = _timeProvider.UtcNow.UtcDateTime,
            Summary = new PolicyViolationSummary
            {
                Total = Violations.Count,
                BySeverity = Violations.GroupBy(static v => v.Severity, StringComparer.Ordinal)
                    .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal),
                ByRule = Violations.GroupBy(static v => v.RuleId, StringComparer.Ordinal)
                    .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal)
            },
            Violations = Violations.ToArray()
        };

        try
        {
            var content = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                ? PolicyValidationReportExporter.ToCsv(report)
                : PolicyValidationReportExporter.ToJson(report);
            var safePath = SafetyValidator.EnsureSafeOutputPath(path, allowUnc: false);
            var directory = Path.GetDirectoryName(safePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            AtomicFileWriter.WriteAllText(safePath, content);
            StatusText = $"Policy-Report exportiert: {safePath}";
            LastError = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = $"Export fehlgeschlagen: {ex.Message}";
        }
    }

    private static string[] ParseList(string raw, char separator)
        => (raw ?? "")
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
