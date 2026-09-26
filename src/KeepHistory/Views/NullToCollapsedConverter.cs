using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeepHistory.Views;

/// <summary>null（または空文字）なら Collapsed、それ以外は Visible。</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
