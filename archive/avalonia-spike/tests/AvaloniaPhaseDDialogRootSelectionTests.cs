using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseDDialogRootSelectionTests
{
    [Fact]
    public async Task AddRootCommand_DefaultSafePicker_DoesNotAddPlaceholderRoot()
    {
        var vm = new StartViewModel();
        var initialCount = vm.Roots.Count;

        await vm.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(initialCount, vm.Roots.Count);
    }

    [Fact]
    public async Task AddRootCommand_WithSelectedFolder_AddsUniqueRootAndSelectsIt()
    {
        var picker = new StubFolderPickerService
        {
            NextFolder = @"D:\\Collections\\SNES"
        };

        var vm = new StartViewModel(picker);
        var initialCount = vm.Roots.Count;

        await vm.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(initialCount + 1, vm.Roots.Count);
        Assert.Equal(@"D:\\Collections\\SNES", vm.SelectedRoot);
    }

    [Fact]
    public async Task AddRootCommand_WithDuplicateFolder_DoesNotAppendDuplicate()
    {
        var picker = new StubFolderPickerService
        {
            NextFolder = @"C:\\ROMS\\Arcade"
        };

        var vm = new StartViewModel(picker);
        var initialCount = vm.Roots.Count;

        await vm.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(initialCount, vm.Roots.Count);
        Assert.Equal(@"C:\\ROMS\\Arcade", vm.SelectedRoot);
    }

    private sealed class StubFolderPickerService : IAvaloniaFolderPickerService
    {
        public string? NextFolder { get; set; }

        public Task<string?> BrowseFolderAsync(string title = "")
            => Task.FromResult(NextFolder);
    }
}
