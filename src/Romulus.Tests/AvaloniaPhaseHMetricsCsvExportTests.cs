using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseHMetricsCsvExportTests
{
    [Fact]
    public async Task ExportMetricsCsvCommand_WhenSavePathSelected_WritesCsvWithHeaderAndRow()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"romulus-metrics-{Guid.NewGuid():N}.csv");
        var picker = new StubFilePickerService
        {
            NextSaveFile = exportPath
        };

        var vm = new ResultViewModel(filePickerService: picker);
        vm.ApplyFromPreview(rootCount: 2);

        await vm.ExportMetricsCsvCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.SaveFileCalls);
        Assert.True(File.Exists(exportPath));

        var csv = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("summary,games,dupes,junk,health", csv);
        Assert.Contains("\"Preview abgeschlossen:", csv);
        Assert.Contains(",\"240\",\"28\",\"12\",\"82\"", csv);

        File.Delete(exportPath);
    }

    [Fact]
    public async Task ExportMetricsCsvCommand_WhenPickerCancelled_DoesNotCreateFile()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"romulus-metrics-{Guid.NewGuid():N}.csv");
        var picker = new StubFilePickerService
        {
            NextSaveFile = null
        };

        var vm = new ResultViewModel(filePickerService: picker);
        vm.ApplyFromPreview(rootCount: 1);

        await vm.ExportMetricsCsvCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.SaveFileCalls);
        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public void ExportMetricsCsvCommand_CanExecute_RequiresRunData()
    {
        var vm = new ResultViewModel();
        Assert.False(vm.ExportMetricsCsvCommand.CanExecute(null));

        vm.ApplyFromPreview(rootCount: 1);

        Assert.True(vm.ExportMetricsCsvCommand.CanExecute(null));
    }

    private sealed class StubFilePickerService : IAvaloniaFilePickerService
    {
        public int SaveFileCalls { get; private set; }

        public string? NextBrowseFile { get; set; }

        public string? NextSaveFile { get; set; }

        public Task<string?> BrowseFileAsync(string title = "", string filter = "")
            => Task.FromResult(NextBrowseFile);

        public Task<string?> SaveFileAsync(string title = "", string filter = "", string? defaultFileName = null)
        {
            SaveFileCalls++;
            return Task.FromResult(NextSaveFile);
        }
    }
}
