using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseFRootImportWorkflowTests
{
    [Fact]
    public async Task ImportRootsCommand_WhenFileSelected_ImportsUniqueTrimmedRoots()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFile,
            [
                "  ",
                @"C:\\ROMS\\Arcade",
                @"D:\\Collections\\PSX",
                @"D:\\Collections\\PSX",
                @"E:\\Collections\\N64"
            ]);

            var picker = new StubFilePickerService
            {
                NextBrowseFile = tempFile
            };

            var vm = new StartViewModel(filePickerService: picker);
            var initialCount = vm.Roots.Count;

            await vm.ImportRootsCommand.ExecuteAsync(null);

            Assert.Equal(initialCount + 2, vm.Roots.Count);
            Assert.Equal(@"E:\\Collections\\N64", vm.SelectedRoot);
            Assert.Equal(1, picker.BrowseFileCalls);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportRootsCommand_WhenPickerCancelled_DoesNotMutateRoots()
    {
        var picker = new StubFilePickerService
        {
            NextBrowseFile = null
        };

        var vm = new StartViewModel(filePickerService: picker);
        var initialCount = vm.Roots.Count;

        await vm.ImportRootsCommand.ExecuteAsync(null);

        Assert.Equal(initialCount, vm.Roots.Count);
        Assert.Equal(1, picker.BrowseFileCalls);
    }

    private sealed class StubFilePickerService : IAvaloniaFilePickerService
    {
        public int BrowseFileCalls { get; private set; }

        public string? NextBrowseFile { get; set; }

        public string? NextSaveFile { get; set; }

        public Task<string?> BrowseFileAsync(string title = "", string filter = "")
        {
            BrowseFileCalls++;
            return Task.FromResult(NextBrowseFile);
        }

        public Task<string?> SaveFileAsync(string title = "", string filter = "", string? defaultFileName = null)
            => Task.FromResult(NextSaveFile);
    }
}
