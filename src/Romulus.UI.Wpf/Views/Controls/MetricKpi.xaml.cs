using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Romulus.UI.Wpf.Views;

/// <summary>
/// D1 (UX-Redesign Phase 3): Deduplizierte KPI-Kachel fuer ResultView.
/// Keine Businesslogik, rein visuelle Komposition via DependencyProperties.
/// Verwendet das bestehende MetricCard-Style als visuellen Wrapper.
/// </summary>
public partial class MetricKpi : UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(MetricKpi),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricKpi),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MetricKpi),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(MetricKpi),
            new PropertyMetadata(null));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public MetricKpi()
    {
        InitializeComponent();
    }
}
