using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseEStoragePickerWorkflowTests
{
    [Fact]
    public async Task AddRootCommand_ExecuteAsync_UsesFolderPickerSelection()
    {
        var picker = new StubFolderPickerService
        {
            NextFolder = @"E:\\Collections\\PlayStation"
        };

        var vm = new StartViewModel(folderPickerService: picker);
        var initialCount = vm.Roots.Count;

        await vm.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.Calls);
        Assert.Equal(initialCount + 1, vm.Roots.Count);
        Assert.Equal(@"E:\\Collections\\PlayStation", vm.SelectedRoot);
    }

    [Fact]
    public async Task AddRootCommand_ExecuteAsync_WhenPickerCancelled_DoesNotMutateRoots()
    {
        var picker = new StubFolderPickerService
        {
            NextFolder = null
        };

        var vm = new StartViewModel(folderPickerService: picker);
        var initialCount = vm.Roots.Count;

        await vm.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.Calls);
        Assert.Equal(initialCount, vm.Roots.Count);
    }

    private sealed class StubFolderPickerService : IAvaloniaFolderPickerService
    {
        public int Calls { get; private set; }

        public string? NextFolder { get; set; }

        public Task<string?> BrowseFolderAsync(string title = "")
        {
            Calls++;
            return Task.FromResult(NextFolder);
        }
    }
}
