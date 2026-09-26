using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using KeepHistory.Models;

namespace KeepHistory.Interop;

public interface IHotkeyService
{
    bool IsRegistered { get; }

    /// <summary>登録する（既存の登録は解除してから）。無効設定なら登録せず true。</summary>
    bool Register(HotkeySetting setting);

    void Unregister();
}

/// <summary>RegisterHotKey によるグローバルホットキー。メッセージ専用ウインドウで WM_HOTKEY を受ける。</summary>
public sealed class GlobalHotkey : IHotkeyService, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4B48;
    private const int ProbeId = 0x4B49;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;

    public GlobalHotkey()
    {
        var parameters = new HwndSourceParameters("KeepHistory.Hotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public bool IsRegistered { get; private set; }

    public bool Register(HotkeySetting setting)
    {
        Unregister();
        if (!setting.Enabled) return true;
        if (!TryGetVirtualKey(setting, out var vk)) return false;
        IsRegistered = RegisterHotKey(_source.Handle, HotkeyId, setting.ToNativeModifiers(), vk);
        return IsRegistered;
    }

    /// <summary>設定のキー名を仮想キーコードにする。</summary>
    internal static bool TryGetVirtualKey(HotkeySetting setting, out uint vk)
    {
        vk = 0;
        if (!Enum.TryParse<Key>(setting.Key, ignoreCase: true, out var key) || key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    /// <summary>
    /// その組み合わせをいま登録できるか（ほかのアプリや Windows が使っていないか）を確かめる。
    /// 一時的に登録してすぐ解除する。KeepHistory 自身の登録は、設定画面を開いている間は解除済み。
    /// </summary>
    public static bool IsAvailable(HotkeySetting setting)
    {
        if (!TryGetVirtualKey(setting, out var vk)) return false;
        if (!RegisterHotKey(IntPtr.Zero, ProbeId, setting.ToNativeModifiers(), vk)) return false;
        UnregisterHotKey(IntPtr.Zero, ProbeId);
        return true;
    }

    public void Unregister()
    {
        if (!IsRegistered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        IsRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
