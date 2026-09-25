using System;
using System.Threading;

namespace KeepHistory.Interop;

/// <summary>
/// 多重起動ガード。2 つ目のプロセスは既存プロセスに「画面を出して」と合図して終了する。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _eventName;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(true, $@"Local\{name}.Mutex", out var createdNew);
        IsFirstInstance = createdNew;
        _eventName = $@"Local\{name}.Show";
    }

    public bool IsFirstInstance { get; }

    /// <summary>後から起動されたプロセスからの合図を待つ（コールバックはスレッドプールで呼ばれる）。</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    public void SignalExistingInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(_eventName);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _showEvent?.Dispose();
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
        _mutex.Dispose();
    }
}
