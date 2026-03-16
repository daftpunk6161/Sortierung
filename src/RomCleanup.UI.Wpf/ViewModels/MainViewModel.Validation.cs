using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel : INotifyDataErrorInfo
{
    private readonly Dictionary<string, ValidationIssue> _validationErrors = new();

    private enum ValidationSeverity
    {
        Warning,
        Blocker
    }

    private sealed record ValidationIssue(string Message, ValidationSeverity Severity);

    private readonly record struct ValidationSummary(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings)
    {
        public int BlockerCount => Blockers.Count;
        public int WarningCount => Warnings.Count;
        public bool HasBlockers => BlockerCount > 0;
        public bool HasWarnings => WarningCount > 0;
    }

    public bool HasErrors => _validationErrors.Count > 0;
    public bool HasBlockingValidationErrors => _validationErrors.Values.Any(issue => issue.Severity == ValidationSeverity.Blocker);
    public bool HasValidationWarnings => _validationErrors.Values.Any(issue => issue.Severity == ValidationSeverity.Warning);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is not null && _validationErrors.TryGetValue(propertyName, out var error))
            return new[] { error.Message };
        return Array.Empty<string>();
    }

    private void ValidateToolPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!TryNormalizePath(value, out _))
            SetError(propertyName, "Ungültiger Dateipfad", ValidationSeverity.Blocker);
        else if (!File.Exists(value))
            SetError(propertyName, $"Datei nicht gefunden: {Path.GetFileName(value)}", ValidationSeverity.Warning);
        else
            ClearError(propertyName);
    }

    private void ValidateDirectoryPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!TryNormalizePath(value, out _))
            SetError(propertyName, "Ungültiger Verzeichnispfad", ValidationSeverity.Blocker);
        else if (!Directory.Exists(value))
            SetError(propertyName, "Verzeichnis existiert nicht", ValidationSeverity.Warning);
        else
            ClearError(propertyName);
    }

    private ValidationSummary GetValidationSummary()
    {
        var blockers = _validationErrors.Values
            .Where(issue => issue.Severity == ValidationSeverity.Blocker)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warnings = _validationErrors.Values
            .Where(issue => issue.Severity == ValidationSeverity.Warning)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ValidationSummary(blockers, warnings);
    }

    private string GetBlockingValidationMessage()
    {
        var summary = GetValidationSummary();
        if (!summary.HasBlockers)
            return "Start gesperrt: Konfiguration enthält blockierende Fehler.";

        var builder = new StringBuilder("Start gesperrt: Konfiguration enthält blockierende Fehler.");
        foreach (var blocker in summary.Blockers.Take(3))
        {
            builder.AppendLine();
            builder.Append("- ").Append(blocker);
        }

        if (summary.BlockerCount > 3)
        {
            builder.AppendLine();
            builder.Append($"- weitere {summary.BlockerCount - 3} Fehler");
        }

        return builder.ToString();
    }

    private static bool TryNormalizePath(string value, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private void SetError(string propertyName, string error, ValidationSeverity severity)
    {
        if (_validationErrors.TryGetValue(propertyName, out var existing)
            && existing.Message == error
            && existing.Severity == severity)
            return;

        _validationErrors[propertyName] = new ValidationIssue(error, severity);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnValidationStateChanged();
    }

    private void ClearError(string propertyName)
    {
        if (_validationErrors.Remove(propertyName))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnValidationStateChanged();
        }
    }

    private void OnValidationStateChanged()
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasBlockingValidationErrors));
        OnPropertyChanged(nameof(HasValidationWarnings));
        RefreshStatus();
        DeferCommandRequery();
    }
}
