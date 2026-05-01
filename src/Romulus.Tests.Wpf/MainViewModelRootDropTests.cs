using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class MainViewModelRootDropTests
{
    [Fact]
    public void HandleDroppedFolders_AddsUniqueExistingDirectories_AndPublishesAnnouncement()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "Romulus_RootDrop_" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "Romulus_RootDrop_" + Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(Path.GetTempPath(), "Romulus_RootDrop_" + Guid.NewGuid().ToString("N") + ".txt");

        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        File.WriteAllText(filePath, "not a folder");

        try
        {
            var vm = new MainViewModel();

            var added = vm.HandleDroppedFolders([rootA, rootB, rootA, filePath]);

            Assert.Equal(2, added);
            Assert.Equal(2, vm.Roots.Count);
            Assert.Contains(rootA, vm.Roots);
            Assert.Contains(rootB, vm.Roots);
            Assert.True(vm.HasRootDropAnnouncement);
            Assert.Contains("2", vm.RootDropAnnouncementText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootA))
                Directory.Delete(rootA, true);
            if (Directory.Exists(rootB))
                Directory.Delete(rootB, true);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void HandleDroppedFolders_IgnoresMissingOrFileEntries_AndLeavesAnnouncementHidden_WhenNothingAdded()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "Romulus_RootDrop_" + Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(Path.GetTempPath(), "Romulus_RootDrop_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(filePath, "not a folder");

        try
        {
            var vm = new MainViewModel();

            var added = vm.HandleDroppedFolders([missingPath, filePath]);

            Assert.Equal(0, added);
            Assert.Empty(vm.Roots);
            Assert.False(vm.HasRootDropAnnouncement);
            Assert.Equal(string.Empty, vm.RootDropAnnouncementText);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void SetRootDropTargetActive_UpdatesBindableState()
    {
        var vm = new MainViewModel();

        vm.SetRootDropTargetActive(true);
        Assert.True(vm.IsRootDropTargetActive);

        vm.SetRootDropTargetActive(false);
        Assert.False(vm.IsRootDropTargetActive);
    }
}
