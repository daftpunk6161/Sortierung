using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

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

    [Fact]
    public void BrowseDatMappingFileCommand_IgnoresNonDatMapRowParameter()
    {
        var dialog = new StubDialogService
        {
            BrowseFileResult = @"C:\dats\nes.dat"
        };

        var vm = new MainViewModel(new ThemeService(), dialog);
        var ex = Record.Exception(() => vm.BrowseDatMappingFileCommand.Execute(new object()));

        Assert.Null(ex);
    }
}
