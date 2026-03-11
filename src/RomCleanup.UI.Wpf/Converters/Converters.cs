using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RomCleanup.UI.Wpf.Models;

namespace RomCleanup.UI.Wpf.Converters;

/// <summary>Converts bool to Visibility (true → Visible, false → Collapsed).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Inverted bool to Visibility (true → Collapsed, false → Visible).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>Converts StatusLevel enum to a SolidColorBrush for status dots.</summary>
public sealed class StatusLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush BrushOk = Freeze(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly SolidColorBrush BrushWarning = Freeze(Color.FromRgb(0xFF, 0xB7, 0x00));
    private static readonly SolidColorBrush BrushBlocked = Freeze(Color.FromRgb(0xFF, 0x00, 0x44));
    private static readonly SolidColorBrush BrushMissing = Freeze(Color.FromRgb(0x99, 0x99, 0xCC));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is StatusLevel level ? level switch
        {
            StatusLevel.Ok => BrushOk,
            StatusLevel.Warning => BrushWarning,
            StatusLevel.Blocked => BrushBlocked,
            _ => BrushMissing
        } : BrushMissing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>Converts LogEntry.Level to a SolidColorBrush for log coloring.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Danger = Freeze(Color.FromRgb(0xFF, 0x00, 0x44));
    private static readonly SolidColorBrush Warning = Freeze(Color.FromRgb(0xFF, 0xB7, 0x00));
    private static readonly SolidColorBrush Muted = Freeze(Color.FromRgb(0x99, 0x99, 0xCC));
    private static readonly SolidColorBrush Cyan = Freeze(Color.FromRgb(0x00, 0xF5, 0xFF));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string level ? level switch
        {
            "ERROR" => Danger,
            "WARN" or "WARNING" => Warning,
            "DEBUG" => Muted,
            _ => Cyan
        } : Cyan;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
