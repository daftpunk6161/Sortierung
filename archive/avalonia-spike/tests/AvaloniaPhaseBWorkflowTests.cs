using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseBWorkflowTests
{
    [Fact]
    public void Constructor_DefaultsToStartScreen()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal(WorkspaceScreen.Start, vm.CurrentScreen);
        Assert.Same(vm.Start, vm.CurrentScreenViewModel);
    }

    [Fact]
    public async Task StartViewModel_AddAndRemoveRoots_UpdatesCollection()
    {
        var picker = new StubFolderPickerService
        {
            NextFolder = @"D:\\Collections\\NeoGeo"
        };
        var vm = new MainWindowViewModel(start: new StartViewModel(picker));
        var start = vm.Start;
        var initialCount = start.Roots.Count;

        await start.AddRootCommand.ExecuteAsync(null);

        Assert.Equal(initialCount + 1, start.Roots.Count);

        start.SelectedRoot = start.Roots[^1];
        start.RemoveRootCommand.Execute(null);

        Assert.Equal(initialCount, start.Roots.Count);
    }

    [Fact]
    public void StartPreviewCommand_SwitchesToProgressAndStartsRun()
    {
        var vm = new MainWindowViewModel();

        vm.StartPreviewCommand.Execute(null);

        Assert.Equal(WorkspaceScreen.Progress, vm.CurrentScreen);
        Assert.True(vm.Progress.IsRunning);
        Assert.Equal("0%", vm.Progress.ProgressText);
    }

    [Fact]
    public void CompleteRunCommand_SwitchesToResultAndBuildsSummary()
    {
        var vm = new MainWindowViewModel();
        vm.StartPreviewCommand.Execute(null);

        vm.CompleteRunCommand.Execute(null);

        Assert.Equal(WorkspaceScreen.Result, vm.CurrentScreen);
        Assert.False(vm.Progress.IsRunning);
        Assert.NotEmpty(vm.Result.RunSummaryText);
    }

    [Fact]
    public void ReturnToStartCommand_ResetsWorkflowToStart()
    {
        var vm = new MainWindowViewModel();
        vm.StartPreviewCommand.Execute(null);
        vm.CompleteRunCommand.Execute(null);

        vm.ReturnToStartCommand.Execute(null);

        Assert.Equal(WorkspaceScreen.Start, vm.CurrentScreen);
        Assert.Equal(0d, vm.Progress.Progress);
        Assert.Equal("0%", vm.Progress.ProgressText);
    }

    private sealed class StubFolderPickerService : IAvaloniaFolderPickerService
    {
        public string? NextFolder { get; set; }

        public Task<string?> BrowseFolderAsync(string title = "")
            => Task.FromResult(NextFolder);
    }
}
