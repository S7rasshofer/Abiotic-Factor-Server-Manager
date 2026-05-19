using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AbioticServerManager.Core.Diagnostics;

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
