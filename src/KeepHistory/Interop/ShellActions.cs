using System;
using System.Diagnostics;
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

    public static Task OpenFolderAsync(string folder)
        => Task.Run(() =>
        {
            using var _ = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        });
}
