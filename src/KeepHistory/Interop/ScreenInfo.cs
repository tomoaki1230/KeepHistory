using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace KeepHistory.Interop;

/// <summary>モニターの作業領域（WPF の単位に直したもの）。</summary>
public static class ScreenInfo
{
    /// <summary>マウスカーソルのあるモニターの作業領域。</summary>
    public static Rect WorkAreaAtCursor() => ToDip(Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea);

    /// <summary>すべてのモニターの作業領域。</summary>
    public static IReadOnlyList<Rect> AllWorkAreas() => Forms.Screen.AllScreens.Select(s => ToDip(s.WorkingArea)).ToList();

    // WPF はシステムの DPI で拡大しているので、ピクセルをシステムの倍率で割る
    private static Rect ToDip(Drawing.Rectangle pixels)
    {
        using var g = Drawing.Graphics.FromHwnd(System.IntPtr.Zero);
        var scaleX = g.DpiX / 96.0;
        var scaleY = g.DpiY / 96.0;
        return new Rect(pixels.X / scaleX, pixels.Y / scaleY, pixels.Width / scaleX, pixels.Height / scaleY);
    }
}
