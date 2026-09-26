using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace KeepHistory.Interop;

/// <summary>
/// ファイルを開く・エクスプローラーで表示する。
/// ネットワーク上のファイルで UI が固まらないよう、バックグラウンドで起動する。
/// </summary>
public static class ShellActions
{
    public static Task OpenAsync(string path)
        => Task.Run(() =>
        {
            using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });

    public static Task RevealInExplorerAsync(string path)
        => Task.Run(() =>
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        });

    /// <summary>
    /// Windows の「プログラムから開く」画面を出す（選んだアプリで開く）。キャンセルも成功扱い。失敗したら false。
    /// </summary>
    public static bool ShowOpenWithDialog(IntPtr owner, string path)
    {
        var info = new OpenAsInfo { File = path, Class = null, Flags = OaifAllowRegistration | OaifExec };
        var hr = SHOpenWithDialog(owner, ref info);
        return hr == 0 || hr == HResultCancelled;
    }

    private const int OaifAllowRegistration = 0x1;
    private const int OaifExec = 0x4;
    private const int HResultCancelled = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public int Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo info);

    public static Task OpenFolderAsync(string folder)
        => Task.Run(() =>
        {
            using var _ = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        });
}
