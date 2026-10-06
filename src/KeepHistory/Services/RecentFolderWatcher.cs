using System;
using System.IO;

namespace KeepHistory.Services;

/// <summary>
/// 「最近使った項目」フォルダの .lnk の作成・更新を監視する。
/// イベントはスレッドプールから上がるので、受け側で UI スレッドへ移すこと。
/// フォルダがネットワーク上にあって（フォルダリダイレクトなど）一時的に切断されると、FileSystemWatcher は
/// エラーを 1 回上げて監視をやめてしまう。そのときは IsRunning が false になるので、Restart で始め直すこと。
/// </summary>
public sealed class RecentFolderWatcher : IDisposable
{
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private volatile bool _failed;

    public RecentFolderWatcher(string folder) => Folder = folder;

    public string Folder { get; }

    /// <summary>.lnk が作成・更新・改名された（引数は .lnk のフルパス）。</summary>
    public event Action<string>? LinkChanged;

    /// <summary>監視バッファのあふれや、監視の停止など。全体を走査し直すこと。</summary>
    public event Action? RescanRequired;

    /// <summary>監視しているか（始められなかった・エラーで止まったなら false）。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock) return _watcher != null && !_failed;
        }
    }

    /// <summary>
    /// 監視を始める。フォルダが無い・届かないなら false。
    /// ネットワーク上のフォルダだと時間がかかることがあるので、UI スレッド以外で呼ぶこと。
    /// </summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (_watcher != null && !_failed) return true;
            Stop();
            FileSystemWatcher? watcher = null;
            try
            {
                if (!Directory.Exists(Folder)) return false;
                watcher = new FileSystemWatcher(Folder, "*.lnk")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => LinkChanged?.Invoke(e.FullPath);
                watcher.Changed += (_, e) => LinkChanged?.Invoke(e.FullPath);
                watcher.Renamed += (_, e) => LinkChanged?.Invoke(e.FullPath);
                watcher.Error += (_, e) => HandleError(e.GetException());
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
                _failed = false;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // 確かめた直後に切断された、など
                watcher?.Dispose();
                return false;
            }
        }
    }

    /// <summary>止まっていれば始め直す（UI スレッド以外で呼ぶこと）。始め直したら true。</summary>
    public bool Restart()
    {
        lock (_lock)
        {
            if (_watcher != null && !_failed) return false;
            return Start();
        }
    }

    /// <summary>FileSystemWatcher のエラー。バッファのあふれは監視が続くので走査し直すだけ。それ以外は監視が止まっている。</summary>
    internal void HandleError(Exception? ex)
    {
        if (ex is not InternalBufferOverflowException)
        {
            _failed = true;
            ErrorLog.Write($"最近使った項目の監視が止まりました。始め直します: {Folder}", ex);
        }
        RescanRequired?.Invoke();
    }

    private void Stop()
    {
        if (_watcher == null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        lock (_lock) Stop();
    }
}
