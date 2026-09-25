using System;
using System.IO;

namespace KeepHistory.Services;

/// <summary>
/// 「最近使った項目」フォルダの .lnk の作成・更新を監視する。
/// イベントはスレッドプールから上がるので、受け側で UI スレッドへ移すこと。
/// </summary>
public sealed class RecentFolderWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;

    public RecentFolderWatcher(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>.lnk が作成・更新・改名された（引数は .lnk のフルパス）。</summary>
    public event Action<string>? LinkChanged;

    /// <summary>監視バッファのあふれなど。全体を走査し直すこと。</summary>
    public event Action? RescanRequired;

    public bool Start()
    {
        if (!Directory.Exists(Folder)) return false;
        _watcher = new FileSystemWatcher(Folder, "*.lnk")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (_, e) => LinkChanged?.Invoke(e.FullPath);
        _watcher.Changed += (_, e) => LinkChanged?.Invoke(e.FullPath);
        _watcher.Renamed += (_, e) => LinkChanged?.Invoke(e.FullPath);
        _watcher.Error += (_, _) => RescanRequired?.Invoke();
        _watcher.EnableRaisingEvents = true;
        return true;
    }

    public void Dispose()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
