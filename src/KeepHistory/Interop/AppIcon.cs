using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeepHistory.Interop;

/// <summary>
/// アプリのアイコン（Assets/KeepHistory.ico）。
/// 元画像は Assets/icon.png。変更したら tools/make-icon.ps1 で .ico を作り直す。
/// </summary>
public static class AppIcon
{
    public const string ResourceUri = "pack://application:,,,/KeepHistory;component/Assets/KeepHistory.ico";

    private static Drawing.Icon? _trayIcon;
    private static ImageSource? _imageSource;

    /// <summary>通知領域用（画面の拡大率に合った小アイコンのサイズで読む）。</summary>
    public static Drawing.Icon GetIcon()
        => _trayIcon ??= LoadIcon(Forms.SystemInformation.SmallIconSize);

    /// <summary>指定サイズに最も近いアイコンを読む。</summary>
    public static Drawing.Icon LoadIcon(Drawing.Size size)
    {
        using var stream = OpenStream();
        return new Drawing.Icon(stream, size);
    }

    /// <summary>ウインドウ用（タイトルバー・タスクバー）。</summary>
    public static ImageSource GetImageSource()
    {
        if (_imageSource != null) return _imageSource;
        var decoder = BitmapDecoder.Create(new Uri(ResourceUri), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return _imageSource = frame;
    }

    public static Stream OpenStream()
    {
        var info = Application.GetResourceStream(new Uri(ResourceUri))
                   ?? throw new InvalidOperationException("アイコンのリソースが見つかりません: " + ResourceUri);
        return info.Stream;
    }
}
