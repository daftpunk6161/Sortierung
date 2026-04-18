using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Avalonia.Services;

namespace Romulus.UI.Avalonia.ViewModels;

public sealed class ResultViewModel : ObservableObject
{
    private const string ExportTitle = "Ergebnis exportieren";
    private const string ExportFilter = "Textdatei|*.txt|Alle Dateien|*.*";
    private const string ExportDefaultFileName = "romulus-result.txt";
    private const string ExportMetricsTitle = "Metriken exportieren";
    private const string ExportMetricsFilter = "CSV-Datei|*.csv|Alle Dateien|*.*";
    private const string ExportMetricsDefaultFileName = "romulus-metrics.csv";

    private readonly IAvaloniaFilePickerService _filePickerService;
    private string _runSummaryText = string.Empty;
    private string _dashGames = "0";
    private string _dashDupes = "0";
    private string _dashJunk = "0";
    private string _healthScore = "0";
    private string _exportStatusText = string.Empty;

    public ResultViewModel(IAvaloniaFilePickerService? filePickerService = null)
    {
        _filePickerService = filePickerService ?? new SafeFilePickerService();
        ExportSummaryCommand = new AsyncRelayCommand(ExportSummaryAsync, () => HasRunData);
        ExportMetricsCsvCommand = new AsyncRelayCommand(ExportMetricsCsvAsync, () => HasRunData);
    }

    public string RunSummaryText
    {
        get => _runSummaryText;
        private set
        {
            if (!SetProperty(ref _runSummaryText, value))
                return;

            OnPropertyChanged(nameof(HasRunData));
            ExportSummaryCommand.NotifyCanExecuteChanged();
            ExportMetricsCsvCommand.NotifyCanExecuteChanged();
        }
    }

    public string DashGames
    {
        get => _dashGames;
        private set => SetProperty(ref _dashGames, value);
    }

    public string DashDupes
    {
        get => _dashDupes;
        private set => SetProperty(ref _dashDupes, value);
    }

    public string DashJunk
    {
        get => _dashJunk;
        private set => SetProperty(ref _dashJunk, value);
    }

    public string HealthScore
    {
        get => _healthScore;
        private set => SetProperty(ref _healthScore, value);
    }

    public string ExportStatusText
    {
        get => _exportStatusText;
        private set => SetProperty(ref _exportStatusText, value);
    }

    public bool HasRunData => !string.IsNullOrWhiteSpace(RunSummaryText);

    public IAsyncRelayCommand ExportSummaryCommand { get; }

    public IAsyncRelayCommand ExportMetricsCsvCommand { get; }

    public void ApplyFromPreview(int rootCount)
    {
        var games = Math.Max(1, rootCount * 120);
        var dupes = rootCount * 14;
        var junk = rootCount * 6;
        var health = Math.Max(0, 100 - dupes / 2 - junk / 3);

        DashGames = games.ToString();
        DashDupes = dupes.ToString();
        DashJunk = junk.ToString();
        HealthScore = health.ToString();
        RunSummaryText = $"Preview abgeschlossen: {games} Kandidaten, {dupes} Duplikate, {junk} Junk.";
        ExportStatusText = string.Empty;
    }

    private async Task ExportSummaryAsync()
    {
        if (!HasRunData)
            return;

        var targetFile = await _filePickerService.SaveFileAsync(ExportTitle, ExportFilter, ExportDefaultFileName);
        if (string.IsNullOrWhiteSpace(targetFile))
            return;

        try
        {
            var summary = BuildExportSummary();
            await File.WriteAllTextAsync(targetFile, summary, Encoding.UTF8);
            ExportStatusText = $"Exportiert: {targetFile}";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"Export fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task ExportMetricsCsvAsync()
    {
        if (!HasRunData)
            return;

        var targetFile = await _filePickerService.SaveFileAsync(
            ExportMetricsTitle,
            ExportMetricsFilter,
            ExportMetricsDefaultFileName);
        if (string.IsNullOrWhiteSpace(targetFile))
            return;

        try
        {
            var csv = BuildMetricsCsv();
            await File.WriteAllTextAsync(targetFile, csv, Encoding.UTF8);
            ExportStatusText = $"CSV exportiert: {targetFile}";
        }
        catch (Exception ex)
        {
            ExportStatusText = $"CSV-Export fehlgeschlagen: {ex.Message}";
        }
    }

    private string BuildExportSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Romulus Preview Summary");
        builder.AppendLine(RunSummaryText);
        builder.AppendLine($"Games: {DashGames}");
        builder.AppendLine($"Dupes: {DashDupes}");
        builder.AppendLine($"Junk: {DashJunk}");
        builder.AppendLine($"Health: {HealthScore}");
        return builder.ToString();
    }

    private string BuildMetricsCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("summary,games,dupes,junk,health");
        builder.Append(EscapeCsvField(RunSummaryText));
        builder.Append(',').Append(EscapeCsvField(DashGames));
        builder.Append(',').Append(EscapeCsvField(DashDupes));
        builder.Append(',').Append(EscapeCsvField(DashJunk));
        builder.Append(',').Append(EscapeCsvField(HealthScore));
        builder.AppendLine();
        return builder.ToString();
    }

    private static string EscapeCsvField(string? value)
    {
        var normalized = string.IsNullOrEmpty(value) ? string.Empty : value;
        if (normalized.Length > 0)
        {
            var first = normalized[0];
            if (first is '=' or '+' or '-' or '@')
                normalized = "'" + normalized;
        }

        var escaped = normalized.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    public void Reset()
    {
        DashGames = "0";
        DashDupes = "0";
        DashJunk = "0";
        HealthScore = "0";
        RunSummaryText = string.Empty;
        ExportStatusText = string.Empty;
    }
}
