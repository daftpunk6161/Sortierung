using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Avalonia.Services;

namespace Romulus.UI.Avalonia.ViewModels;

public sealed class StartViewModel : ObservableObject
{
    private const string RootImportTitle = "Root-Liste auswählen";
    private const string RootImportFilter = "Textdateien|*.txt|Alle Dateien|*.*";

    private readonly IAvaloniaFolderPickerService _folderPickerService;
    private readonly IAvaloniaFilePickerService _filePickerService;
    private string? _selectedRoot;

    public StartViewModel(
        IAvaloniaFolderPickerService? folderPickerService = null,
        IAvaloniaFilePickerService? filePickerService = null)
    {
        _folderPickerService = folderPickerService ?? new SafeFolderPickerService();
        _filePickerService = filePickerService ?? new SafeFilePickerService();

        Roots =
        [
            @"C:\\ROMS\\Arcade",
            @"C:\\ROMS\\Nintendo"
        ];

        AddRootCommand = new AsyncRelayCommand(AddRootAsync);
        ImportRootsCommand = new AsyncRelayCommand(ImportRootsAsync);
        RemoveRootCommand = new RelayCommand(RemoveSelectedRoot, CanRemoveSelectedRoot);
        RequestPreviewCommand = new RelayCommand(RequestPreview, () => HasRoots);
    }

    public event Action? PreviewRequested;

    public ObservableCollection<string> Roots { get; }

    public string? SelectedRoot
    {
        get => _selectedRoot;
        set
        {
            if (!SetProperty(ref _selectedRoot, value))
                return;

            RemoveRootCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasRoots => Roots.Count > 0;

    public string DropHint => "Quellen konfigurieren und Preview starten";

    public IAsyncRelayCommand AddRootCommand { get; }

    public IAsyncRelayCommand ImportRootsCommand { get; }

    public RelayCommand RemoveRootCommand { get; }

    public RelayCommand RequestPreviewCommand { get; }

    private async Task AddRootAsync()
    {
        var selectedPath = await _folderPickerService.BrowseFolderAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        var normalizedPath = selectedPath.Trim();
        var existingPath = FindExistingRoot(normalizedPath);
        if (existingPath is not null)
        {
            SelectedRoot = existingPath;
            RefreshCommandState();
            return;
        }

        Roots.Add(normalizedPath);
        SelectedRoot = normalizedPath;
        RefreshCommandState();
    }

    private async Task ImportRootsAsync()
    {
        var selectedFile = await _filePickerService.BrowseFileAsync(RootImportTitle, RootImportFilter);
        if (string.IsNullOrWhiteSpace(selectedFile) || !File.Exists(selectedFile))
            return;

        var lines = await File.ReadAllLinesAsync(selectedFile);
        string? lastAdded = null;

        foreach (var line in lines)
        {
            var normalizedPath = line.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            if (FindExistingRoot(normalizedPath) is not null)
                continue;

            Roots.Add(normalizedPath);
            lastAdded = normalizedPath;
        }

        if (lastAdded is null)
            return;

        SelectedRoot = lastAdded;
        RefreshCommandState();
    }

    private bool CanRemoveSelectedRoot()
        => SelectedRoot is not null;

    private void RemoveSelectedRoot()
    {
        if (SelectedRoot is null)
            return;

        var removed = Roots.Remove(SelectedRoot);
        if (!removed)
            return;

        SelectedRoot = Roots.Count > 0 ? Roots[^1] : null;
        RefreshCommandState();
    }

    private void RequestPreview()
        => PreviewRequested?.Invoke();

    private string? FindExistingRoot(string path)
    {
        foreach (var existing in Roots)
        {
            if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                return existing;
        }

        return null;
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(HasRoots));
        RequestPreviewCommand.NotifyCanExecuteChanged();
        RemoveRootCommand.NotifyCanExecuteChanged();
    }
}
