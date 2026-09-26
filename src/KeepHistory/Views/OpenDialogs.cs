using System.Collections;
using System.Linq;
using System.Windows;

namespace KeepHistory.Views;

/// <summary>
/// 履歴画面以外に開いている画面（設定・削除した履歴・ライセンス・バージョン情報など）の扱い。
/// 通知領域のメニューは、これらの画面が開いていても操作できるため、その場合の動きをここで決める。
/// </summary>
public static class OpenDialogs
{
    /// <summary>いちばん手前（最後に開いた）の画面。無ければ null。</summary>
    public static Window? FindTopmost(IEnumerable windows, Window? main)
        => windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, main) && w.IsVisible);

    /// <summary>開いている画面をすべて閉じる（手前から）。閉じたものがあれば true。</summary>
    public static bool CloseAll(IEnumerable windows, Window? main)
    {
        var open = windows.OfType<Window>().Where(w => !ReferenceEquals(w, main) && w.IsVisible).Reverse().ToList();
        foreach (var window in open)
        {
            if (window.IsVisible) window.Close();
        }
        return open.Count > 0;
    }
}
