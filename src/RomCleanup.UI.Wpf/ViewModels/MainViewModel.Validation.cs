using System.Collections;
using System.ComponentModel;
using System.IO;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel : INotifyDataErrorInfo
{
    private readonly Dictionary<string, string> _validationErrors = new();

    public bool HasErrors => _validationErrors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is not null && _validationErrors.TryGetValue(propertyName, out var error))
            return new[] { error };
        return Array.Empty<string>();
    }

    private void ValidateToolPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!File.Exists(value))
            SetError(propertyName, $"Datei nicht gefunden: {Path.GetFileName(value)}");
        else
            ClearError(propertyName);
    }

    private void ValidateDirectoryPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!Directory.Exists(value))
            SetError(propertyName, "Verzeichnis existiert nicht");
        else
            ClearError(propertyName);
    }

    private void SetError(string propertyName, string error)
    {
        _validationErrors[propertyName] = error;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ClearError(string propertyName)
    {
        if (_validationErrors.Remove(propertyName))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }
}
