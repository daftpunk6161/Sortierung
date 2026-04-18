using System.Windows;
using System.Windows.Controls;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Views;

public partial class DatCatalogView : UserControl
{
    private bool _initialLoadDone;

    public DatCatalogView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && !_initialLoadDone && DataContext is DatCatalogViewModel vm && !vm.IsBusy)
        {
            // Only auto-load if DatRoot has been configured (settings loaded).
            // Before LoadInitialSettings, DatRoot is "" and we'd get 0 local files.
            var datRoot = vm.GetDatRoot();
            if (string.IsNullOrWhiteSpace(datRoot))
                return;

            _initialLoadDone = true;
            await vm.RefreshCommand.ExecuteAsync(null);
        }
    }
}
