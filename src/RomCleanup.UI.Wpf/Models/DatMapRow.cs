using System.ComponentModel;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Row in the console → DAT-file mapping DataGrid.
/// </summary>
public sealed class DatMapRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _console = "";
    public string Console
    {
        get => _console;
        set { _console = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Console))); }
    }

    private string _datFile = "";
    public string DatFile
    {
        get => _datFile;
        set { _datFile = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DatFile))); }
    }
}
