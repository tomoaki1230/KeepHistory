using System;
using System.Collections.Generic;
using System.Windows;

namespace KeepHistory.Services;

/// <summary>画面を出す位置の計算（座標はすべて WPF の単位）。</summary>
public static class WindowPlacementCalculator
{
    /// <summary>タイトルバーとみなす上端の高さ。</summary>
    private const double TitleBarHeight = 32;

    /// <summary>作業領域の中央に置くときの左上。作業領域より大きければ左上をそろえる（タイトルバーが画面外に出ないように）。</summary>
    public static Point CenterIn(Rect workArea, Size size)
    {
        var left = workArea.Left + Math.Max(0, (workArea.Width - size.Width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - size.Height) / 2);
        return new Point(left, top);
    }

    /// <summary>
    /// その位置でタイトルバーをつかめるか（タイトルバーの一部が、どれかの作業領域に 100×16 以上入っているか）。
    /// モニターを外した・解像度を変えたなどで、前回の位置が画面外になっていないかの判定に使う。
    /// </summary>
    public static bool IsReachable(Rect window, IEnumerable<Rect> workAreas)
    {
        var titleBar = new Rect(window.Left, window.Top, Math.Max(0, window.Width), TitleBarHeight);
        foreach (var area in workAreas)
        {
            var overlap = Rect.Intersect(titleBar, area);
            if (!overlap.IsEmpty && overlap.Width >= 100 && overlap.Height >= 16) return true;
        }
        return false;
    }
}
