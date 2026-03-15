using CommunityToolkit.Mvvm.ComponentModel;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Row in the console → DAT-file mapping DataGrid.
/// GUI-113: Uses ObservableObject to eliminate boilerplate.
/// </summary>
public sealed partial class DatMapRow : ObservableObject
{
    [ObservableProperty]
    private string _console = "";

    [ObservableProperty]
    private string _datFile = "";
}
