using System.Windows.Controls;

namespace Romulus.UI.Wpf.Views;

/// <summary>
/// Policy Governance editor + validation report. Extracted from MainWindow.xaml
/// (T-W1-LAYOUT-P1) to keep Shell-XAML free from Tools-bucket business UI and
/// honour the gui.instructions.md "no business UI in Code-Behind/Shell" rule.
/// DataContext is set by MainWindow to <see cref="ViewModels.PolicyGovernanceViewModel"/>.
/// </summary>
public partial class PolicyGovernanceView : UserControl
{
    public PolicyGovernanceView()
    {
        InitializeComponent();
    }
}
