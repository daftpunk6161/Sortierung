using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Avalonia.Services;

namespace Romulus.UI.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private WorkspaceScreen _currentScreen;
    private readonly AvaloniaThemeService _themeService;
    private readonly ITrayService _trayService;

    public MainWindowViewModel(
        StartViewModel? start = null,
        ProgressViewModel? progress = null,
        ResultViewModel? result = null,
        AvaloniaThemeService? themeService = null,
        ITrayService? trayService = null)
    {
        Start = start ?? new StartViewModel();
        Progress = progress ?? new ProgressViewModel();
        Result = result ?? new ResultViewModel();
        _themeService = themeService ?? new AvaloniaThemeService(new AvaloniaApplicationThemeHost());
        _trayService = trayService ?? new NoOpTrayService();

        Start.PreviewRequested += StartPreview;
        Start.PropertyChanged += OnStartPropertyChanged;
        Result.PropertyChanged += OnResultPropertyChanged;

        StartPreviewCommand = new RelayCommand(StartPreview, () => Start.HasRoots);
        CompleteRunCommand = new RelayCommand(CompleteRun, () => Progress.IsRunning);
        ReturnToStartCommand = new RelayCommand(ReturnToStart);

        NavigateStartCommand = new RelayCommand(() => CurrentScreen = WorkspaceScreen.Start);
        NavigateProgressCommand = new RelayCommand(() => CurrentScreen = WorkspaceScreen.Progress);
        NavigateResultCommand = new RelayCommand(
            () => CurrentScreen = WorkspaceScreen.Result,
            () => Result.HasRunData);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ToggleTrayCommand = new RelayCommand(ToggleTray, () => TraySupported);

        CurrentScreen = WorkspaceScreen.Start;
    }

    public string Headline => "Romulus Avalonia Migration";

    public string ThemeLabel => $"Theme: {_themeService.CurrentLabel}";

    public bool TraySupported => _trayService.IsSupported;

    public bool TrayActive => _trayService.IsActive;

    public StartViewModel Start { get; }

    public ProgressViewModel Progress { get; }

    public ResultViewModel Result { get; }

    public WorkspaceScreen CurrentScreen
    {
        get => _currentScreen;
        set
        {
            if (!SetProperty(ref _currentScreen, value))
                return;

            OnPropertyChanged(nameof(CurrentScreenViewModel));
            OnPropertyChanged(nameof(NavigationSubtitle));
            OnPropertyChanged(nameof(IsStartScreen));
            OnPropertyChanged(nameof(IsProgressScreen));
            OnPropertyChanged(nameof(IsResultScreen));
        }
    }

    public object CurrentScreenViewModel => CurrentScreen switch
    {
        WorkspaceScreen.Start => Start,
        WorkspaceScreen.Progress => Progress,
        WorkspaceScreen.Result => Result,
        _ => Start
    };

    public string NavigationSubtitle => CurrentScreen switch
    {
        WorkspaceScreen.Start => "Start / Quellen konfigurieren",
        WorkspaceScreen.Progress => "Progress / Laufüberwachung",
        WorkspaceScreen.Result => "Result / Auswertung",
        _ => "Start / Quellen konfigurieren"
    };

    public bool IsStartScreen => CurrentScreen == WorkspaceScreen.Start;

    public bool IsProgressScreen => CurrentScreen == WorkspaceScreen.Progress;

    public bool IsResultScreen => CurrentScreen == WorkspaceScreen.Result;

    public RelayCommand StartPreviewCommand { get; }

    public RelayCommand CompleteRunCommand { get; }

    public RelayCommand ReturnToStartCommand { get; }

    public RelayCommand NavigateStartCommand { get; }

    public RelayCommand NavigateProgressCommand { get; }

    public RelayCommand NavigateResultCommand { get; }

    public RelayCommand ToggleThemeCommand { get; }

    public RelayCommand ToggleTrayCommand { get; }

    private void StartPreview()
    {
        CurrentScreen = WorkspaceScreen.Progress;
        Progress.BeginPreviewRun(Start.Roots.Count);
        CompleteRunCommand.NotifyCanExecuteChanged();
    }

    private void CompleteRun()
    {
        Progress.UpdateProgress(100d, "Done", "Analyse abgeschlossen");
        Progress.Complete("Preview abgeschlossen");
        Result.ApplyFromPreview(Start.Roots.Count);
        CurrentScreen = WorkspaceScreen.Result;
        CompleteRunCommand.NotifyCanExecuteChanged();
        NavigateResultCommand.NotifyCanExecuteChanged();
    }

    private void ReturnToStart()
    {
        Progress.Reset();
        Result.Reset();
        CurrentScreen = WorkspaceScreen.Start;
    }

    private void ToggleTheme()
    {
        _themeService.Toggle();
        OnPropertyChanged(nameof(ThemeLabel));
    }

    private void ToggleTray()
    {
        _trayService.Toggle();
        OnPropertyChanged(nameof(TrayActive));
    }

    private void OnStartPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StartViewModel.HasRoots))
            return;

        StartPreviewCommand.NotifyCanExecuteChanged();
    }

    private void OnResultPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResultViewModel.HasRunData))
            return;

        NavigateResultCommand.NotifyCanExecuteChanged();
    }
}
