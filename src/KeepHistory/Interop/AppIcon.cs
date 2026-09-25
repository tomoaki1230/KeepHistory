using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace KeepHistory.Interop;

/// <summary>アイコンを実行時に描く（リソースファイルを持たないため）。</summary>
public static class AppIcon
{
    private static Drawing.Icon? _icon;

    public static Drawing.Icon GetIcon()
    {
        if (_icon != null) return _icon;
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var path = new Drawing2D.GraphicsPath();
            const int r = 8;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x25, 0x63, 0xEB));
            g.FillPath(fill, path);
            using var font = new Drawing.Font("Segoe UI", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString("H", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 1, 32, 32), format);
        }
        var handle = bitmap.GetHicon();
        try
        {
            _icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
        return _icon;
    }

    public static ImageSource GetImageSource()
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(GetIcon().Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
