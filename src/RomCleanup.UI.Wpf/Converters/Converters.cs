using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RomCleanup.Contracts.Models;
using RomCleanup.UI.Wpf.Models;
using Color = System.Windows.Media.Color;

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

/// <summary>Inverts a bool value (true → false, false → true). For IsEnabled bindings.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
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

/// <summary>Converts CurrentStep (int) + ConverterParameter (step number string) to a Fill Brush.
/// Parameter is the step's 1-based index. If CurrentStep >= step, returns accent; otherwise transparent.</summary>
public sealed class StepActiveBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = FreezeColor(Color.FromRgb(0x00, 0xF5, 0xFF));
    private static readonly SolidColorBrush Inactive = FreezeColor(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int current && parameter is string s && int.TryParse(s, out var step))
            return current >= step ? Active : Inactive;
        return Inactive;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush FreezeColor(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

/// <summary>Converts CurrentRunState (RunState) to a brush per pipeline phase.
/// ConverterParameter is the phase index (1–7). Active = Cyan, Done = Green, Pending = Muted.</summary>
public sealed class PipelinePhaseBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Active = FreezeColor(Color.FromRgb(0x00, 0xF5, 0xFF));
    private static readonly SolidColorBrush Done = FreezeColor(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly SolidColorBrush Pending = FreezeColor(Color.FromRgb(0x55, 0x55, 0x77));
    private static readonly SolidColorBrush Idle = FreezeColor(Colors.Transparent);

    /// <summary>Maps RunState to a 1-based phase index.</summary>
    private static int PhaseOf(RunState state) => state switch
    {
        RunState.Preflight => 1,
        RunState.Scanning => 2,
        RunState.Deduplicating => 3,
        RunState.Sorting => 4,
        RunState.Moving => 5,
        RunState.Converting => 6,
        RunState.Completed or RunState.CompletedDryRun => 7,
        _ => 0
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RunState state || parameter is not string s || !int.TryParse(s, out var phase))
            return Idle;

        int current = PhaseOf(state);
        if (current == 0) return Idle;
        if (phase < current) return Done;
        if (phase == current) return Active;
        return Pending;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush FreezeColor(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

/// <summary>P1-005: Converts string value equality to Visibility. Shows content when bound string equals ConverterParameter.</summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>RD-006: Converts string value equality to bool. For RadioButton IsChecked binding with ConverterParameter.</summary>
public sealed class StringEqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter as string ?? DependencyProperty.UnsetValue : DependencyProperty.UnsetValue;
}

/// <summary>RD-004: Converts RunState to a phase detail tooltip string via ConverterParameter (phase 1–7).</summary>
public sealed class PhaseDetailConverter : IValueConverter
{
    private static readonly string[] Descriptions =
    [
        "",
        "Preflight: Konfiguration und Pfade prüfen",
        "Scan: ROM-Verzeichnisse durchsuchen",
        "Dedupe: Duplikate erkennen und beste Version wählen",
        "Sort: Dateien nach Konsole gruppieren",
        "Move: Duplikate in Papierkorb verschieben",
        "Convert: Formate optimieren (CHD/RVZ/ZIP)",
        "Fertig: Ergebnis und Report"
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string s || !int.TryParse(s, out var phase) || phase < 1 || phase > 7)
            return "";

        if (value is not RunState state) return Descriptions[phase];

        int current = state switch
        {
            RunState.Preflight => 1,
            RunState.Scanning => 2,
            RunState.Deduplicating => 3,
            RunState.Sorting => 4,
            RunState.Moving => 5,
            RunState.Converting => 6,
            RunState.Completed or RunState.CompletedDryRun => 7,
            _ => 0
        };

        string status = current == 0 ? "⏳ Ausstehend"
            : phase < current ? "✓ Abgeschlossen"
            : phase == current ? "▶ Aktiv"
            : "⏳ Ausstehend";

        return $"{Descriptions[phase]}\n{status}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>GUI-071: Converts (fraction, containerWidth) → pixel width for bar charts.</summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is double fraction
            && values[1] is double containerWidth
            && containerWidth > 0)
        {
            return Math.Max(2, fraction * containerWidth);
        }
        return 2.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts DatAuditStatus to a SolidColorBrush for status badges.
/// Have=green, HaveWrongName=orange, Miss=red, Unknown=grey, Ambiguous=yellow.</summary>
public sealed class DatAuditStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Have = Freeze(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly SolidColorBrush WrongName = Freeze(Color.FromRgb(0xFF, 0xB7, 0x00));
    private static readonly SolidColorBrush Miss = Freeze(Color.FromRgb(0xFF, 0x00, 0x44));
    private static readonly SolidColorBrush Unknown = Freeze(Color.FromRgb(0x99, 0x99, 0xCC));
    private static readonly SolidColorBrush Ambiguous = Freeze(Color.FromRgb(0xFF, 0xD5, 0x00));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DatAuditStatus status ? status switch
        {
            DatAuditStatus.Have => Have,
            DatAuditStatus.HaveWrongName => WrongName,
            DatAuditStatus.Miss => Miss,
            DatAuditStatus.Unknown => Unknown,
            DatAuditStatus.Ambiguous => Ambiguous,
            _ => Unknown
        } : Unknown;
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

/// <summary>Converts DatAuditStatus to a user-friendly label string.</summary>
public sealed class DatAuditStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DatAuditStatus status ? status switch
        {
            DatAuditStatus.Have => "Have",
            DatAuditStatus.HaveWrongName => "Wrong Name",
            DatAuditStatus.Miss => "Miss",
            DatAuditStatus.Unknown => "Unknown",
            DatAuditStatus.Ambiguous => "Ambiguous",
            _ => "–"
        } : "–";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
