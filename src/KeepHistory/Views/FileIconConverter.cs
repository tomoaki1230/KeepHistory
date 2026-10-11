using System;
using System.Globalization;
using System.Windows.Data;
using KeepHistory.Interop;

namespace KeepHistory.Views;

/// <summary>拡張子 → ファイルの種類の小さいアイコン（FileIcons。ファイルにはアクセスしない）。</summary>
public sealed class FileIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => FileIcons.SmallIconFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
