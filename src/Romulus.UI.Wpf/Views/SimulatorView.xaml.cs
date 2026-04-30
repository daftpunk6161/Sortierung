using System.Windows.Controls;

namespace Romulus.UI.Wpf.Views;

/// <summary>
/// T-W5-BEFORE-AFTER-SIMULATOR pass 4 — code-behind. No business logic
/// (project rule: GUI/Code-Behind frei von Businesslogik). Pure UserControl
/// shell; all behavior lives in <c>SimulatorViewModel</c>.
/// </summary>
public partial class SimulatorView : UserControl
{
    public SimulatorView()
    {
        InitializeComponent();
    }
}
