using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.App.Converters;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DiagnosticSeverity s
            ? s switch
            {
                DiagnosticSeverity.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)),
                DiagnosticSeverity.Warning => new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0x4B)),
                DiagnosticSeverity.Success => new SolidColorBrush(Color.FromRgb(0x79, 0xD6, 0x6B)),
                _ => new SolidColorBrush(Color.FromRgb(0xA8, 0xB5, 0xC1)),
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RunningToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x79, 0xD6, 0x6B))
            : new SolidColorBrush(Color.FromRgb(0xA8, 0xB5, 0xC1));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps <see cref="ServerHealth"/> to the indicator brush. This is the single
/// source of truth for the world status dot — process presence is not health,
/// so binding to <c>IsRunningState</c> was lying (briefly-running corrupt
/// world = green dot + "Blocked" text).
/// </summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Stopped = new(Color.FromRgb(0xA8, 0xB5, 0xC1)); // grey
    public static readonly SolidColorBrush Starting = new(Color.FromRgb(0xE6, 0xB8, 0x4B)); // yellow
    public static readonly SolidColorBrush Online = new(Color.FromRgb(0x79, 0xD6, 0x6B)); // green
    public static readonly SolidColorBrush Blocked = new(Color.FromRgb(0xFF, 0x5F, 0x57)); // red

    public static Brush BrushFor(ServerHealth health) => HealthIndicators.For(health) switch
    {
        HealthIndicator.Grey => Stopped,
        HealthIndicator.Yellow => Starting,
        HealthIndicator.Green => Online,
        HealthIndicator.Red => Blocked,
        _ => Stopped,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ServerHealth h ? BrushFor(h) : (Brush)Stopped;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CheckStatus s
            ? s switch
            {
                CheckStatus.Pass => new SolidColorBrush(Color.FromRgb(0x79, 0xD6, 0x6B)),
                CheckStatus.Fail => new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)),
                _ => new SolidColorBrush(Color.FromRgb(0xA8, 0xB5, 0xC1)),
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NetworkCheckStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is NetworkCheckStatus s
            ? s switch
            {
                NetworkCheckStatus.Pass => new SolidColorBrush(Color.FromRgb(0x79, 0xD6, 0x6B)),
                NetworkCheckStatus.Warn => new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0x4B)),
                NetworkCheckStatus.Fail => new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x57)),
                NetworkCheckStatus.NeedsAdmin => new SolidColorBrush(Color.FromRgb(0xE6, 0xB8, 0x4B)),
                _ => new SolidColorBrush(Color.FromRgb(0xA8, 0xB5, 0xC1)),
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>Visible when the value is non-null. Pass "invert" to flip.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visibleWhenNotNull = !string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var hasValue = value is not null;
        return hasValue == visibleWhenNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the integer value is greater than zero. Used by the §3.2 ban-count badge.</summary>
public sealed class PositiveIntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True / a brush when the bound row is flagged admin (§3.3). Lets the existing
/// roster template stay declarative — one converter, no code-behind.
/// </summary>
public sealed class IsAdminToBrushConverter : IValueConverter
{
    public Brush AdminBrush { get; set; } = Brushes.Goldenrod;
    public Brush DefaultBrush { get; set; } = Brushes.WhiteSmoke;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? AdminBrush : DefaultBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
