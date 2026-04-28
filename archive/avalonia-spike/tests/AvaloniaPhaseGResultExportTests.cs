using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseGResultExportTests
{
    [Fact]
    public async Task ExportSummaryCommand_WhenSavePathSelected_WritesSummaryFile()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"romulus-result-{Guid.NewGuid():N}.txt");
        var picker = new StubFilePickerService
        {
            NextSaveFile = exportPath
        };

        var vm = new ResultViewModel(filePickerService: picker);
        vm.ApplyFromPreview(rootCount: 2);

        await vm.ExportSummaryCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.SaveFileCalls);
        Assert.True(File.Exists(exportPath));
        var content = await File.ReadAllTextAsync(exportPath);
        Assert.Contains("Preview abgeschlossen", content);
        Assert.Contains("Games:", content);

        File.Delete(exportPath);
    }

    [Fact]
    public async Task ExportSummaryCommand_WhenPickerCancelled_DoesNotCreateFile()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"romulus-result-{Guid.NewGuid():N}.txt");
        var picker = new StubFilePickerService
        {
            NextSaveFile = null
        };

        var vm = new ResultViewModel(filePickerService: picker);
        vm.ApplyFromPreview(rootCount: 1);

        await vm.ExportSummaryCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.SaveFileCalls);
        Assert.False(File.Exists(exportPath));
    }

    [Fact]
    public void ExportSummaryCommand_CanExecute_RequiresRunData()
    {
        var vm = new ResultViewModel();
        Assert.False(vm.ExportSummaryCommand.CanExecute(null));

        vm.ApplyFromPreview(rootCount: 1);

        Assert.True(vm.ExportSummaryCommand.CanExecute(null));
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
