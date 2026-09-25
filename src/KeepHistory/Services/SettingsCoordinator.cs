using System;
using KeepHistory.Interop;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 設定画面の開閉。設定画面を開いている間はグローバルホットキーを解除する
/// （ホットキーを変更している最中に、押したキーで履歴画面が飛び出すため）。
/// </summary>
public sealed class SettingsCoordinator
{
    private readonly IHotkeyService _hotkey;
    private readonly Func<AppSettings, AppSettings?> _showDialog;

    /// <param name="showDialog">設定画面をモーダルで開き、OK なら新しい設定、キャンセルなら null を返す。</param>
    public SettingsCoordinator(IHotkeyService hotkey, Func<AppSettings, AppSettings?> showDialog)
    {
        _hotkey = hotkey;
        _showDialog = showDialog;
    }

    public bool IsOpen { get; private set; }

    /// <summary>設定画面を開く。既に開いていれば何もせず null。</summary>
    public AppSettings? Open(AppSettings current)
    {
        if (IsOpen) return null;
        IsOpen = true;
        _hotkey.Unregister();
        AppSettings? result = null;
        try
        {
            result = _showDialog(current.Clone());
            return result;
        }
        finally
        {
            IsOpen = false;
            _hotkey.Register((result ?? current).Hotkey);
        }
    }
}
