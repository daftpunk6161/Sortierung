using RomCleanup.Tests.TestFixtures;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;

namespace RomCleanup.Tests;

public sealed class MainViewModelDatMappingTests
{
    [Fact]
    public void BrowseDatMappingFileCommand_UsesInjectedDialogService()
    {
        var dialog = new StubDialogService
        {
            BrowseFileResult = @"C:\dats\nes.dat"
        };

        var vm = new MainViewModel(new ThemeService(), dialog);
        var row = new DatMapRow
        {
            Console = "NES",
            DatFile = string.Empty
        };

        vm.BrowseDatMappingFileCommand.Execute(row);

        Assert.Equal(@"C:\dats\nes.dat", row.DatFile);
    }
}
