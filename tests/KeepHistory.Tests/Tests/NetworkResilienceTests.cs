using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

/// <summary>
/// ネットワークの一時的な切断や、ほかのソフトがファイルをつかんでいるときに、データを失わず、固まらず、元に戻るか。
/// </summary>
public sealed class NetworkResilienceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0);
    private readonly TempDirectory _dir = new("network");
    private readonly int _attempts = DataStore.ReadAttempts;
    private readonly TimeSpan _delay = DataStore.RetryDelay;

    public NetworkResilienceTests()
    {
        DataStore.ReadAttempts = 2;
        DataStore.RetryDelay = TimeSpan.FromMilliseconds(10);
    }

    public void Dispose()
    {
        DataStore.ReadAttempts = _attempts;
        DataStore.RetryDelay = _delay;
        _dir.Dispose();
    }

    private static HistoryRecord Record(string path, DateTime lastUsed, int count = 1, bool kept = false)
        => new() { Path = path, LastUsed = DateFormat.ToStorage(lastUsed), OpenCount = count, IsKept = kept };

    // ---- 読めないときに、空や既定値として扱わない ----

    [Test]
    public void LockedFiles_AreReportedAsUnavailable_NotAsEmpty()
    {
        var data = new DataStore(_dir.Path);
        data.SaveHistory(new() { Record(@"C:\a.txt", Now, 3, kept: true) });
        data.SaveDeleted(new());
        data.SaveSettings(new AppSettings { RetentionDays = 30 });

        foreach (var (path, load) in new (string, Action)[]
                 {
                     (data.HistoryPath, () => data.LoadHistory()),
                     (data.DeletedPath, () => data.LoadDeleted()),
                     (data.SettingsPath, () => data.LoadSettingsIfExists()),
                 })
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<DataUnavailableException>(load, $"{Path.GetFileName(path)} をつかまれている間は「読めない」（空や既定値にしない）");
            }
        }
        Assert.Equal(1, data.LoadHistory().Count, "離されたら読める（ファイルは壊れていない）");
        Assert.Equal(30, data.LoadSettingsIfExists()!.RetentionDays);
    }

    [Test]
    public void ReadsAgain_WhenTheLockIsReleasedShortly()
    {
        var data = new DataStore(_dir.Path);
        data.SaveHistory(new() { Record(@"C:\a.txt", Now) });
        DataStore.ReadAttempts = 5;
        DataStore.RetryDelay = TimeSpan.FromMilliseconds(100);
        var locked = new FileStream(data.HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = new Thread(() =>
        {
            Thread.Sleep(150);
            locked.Dispose();
        });
        release.Start();
        Assert.Equal(1, data.LoadHistory().Count, "ウイルス対策ソフトなどが一瞬つかんだだけなら、読み直して読める");
        release.Join();
    }

    [Test]
    public void UnreachableLocation_IsUnavailable_NotFirstRun()
    {
        // %APPDATA% がネットワーク上にあり、切断されている（保存フォルダの親も見えない）
        var data = new DataStore(Path.Combine(_dir.Path, "disconnected-share", "KeepHistory"));
        Assert.Throws<DataUnavailableException>(() => data.LoadSettingsIfExists(), "初回起動と取り違えない");
        Assert.Throws<DataUnavailableException>(() => data.LoadHistory(), "履歴を空として読まない");
    }

    [Test]
    public void MissingFile_InReachableLocation_IsFirstRun()
    {
        var notCreatedYet = new DataStore(Path.Combine(_dir.Path, "KeepHistory"));
        Assert.Null(notCreatedYet.LoadSettingsIfExists(), "保存フォルダがまだ無い（親は見える）なら初回");
        Assert.Equal(0, notCreatedYet.LoadHistory().Count);

        var empty = new DataStore(_dir.Path);
        Assert.Null(empty.LoadSettingsIfExists(), "フォルダはあるがファイルが無いなら初回");
        File.WriteAllText(empty.SettingsPath, "{ broken");
        Assert.NotNull(empty.LoadSettingsIfExists(), "壊れたファイルは初回ではない（既定値で動く）");
    }

    [Test]
    public void Settings_AreNotOverwritten_UntilTheyCanBeRead()
    {
        var data = new DataStore(_dir.Path);
        data.SaveSettings(new AppSettings { RetentionDays = 30, StayResident = true });
        var storage = new SettingsStorage(data);

        SettingsLoadResult result;
        AppSettings settings;
        using (new FileStream(data.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = storage.TryLoad(out settings);
        }
        Assert.Equal(SettingsLoadResult.Unavailable, result);
        Assert.True(storage.LoadFailed);
        Assert.Equal(AppSettings.DefaultRetentionDays, settings.RetentionDays, "読めない間は既定値で動く");

        Assert.False(storage.SaveAuto(settings), "読めない間は、画面を隠す・閉じる・終了時の自動保存で上書きしない");
        Assert.Equal(30, data.LoadSettings().RetentionDays, "保存してあった設定が残っている");

        var reloaded = storage.Reload();
        Assert.Equal(30, reloaded!.RetentionDays, "読めるようになったら読み直せる");
        storage.MarkRecovered();
        Assert.True(storage.SaveAuto(reloaded), "反映したら自動保存を再開する");
    }

    [Test]
    public void ConfirmedSettings_AreSaved_EvenWhileUnreadable()
    {
        var data = new DataStore(Path.Combine(_dir.Path, "disconnected-share", "KeepHistory"));
        var storage = new SettingsStorage(data);
        Assert.Equal(SettingsLoadResult.Unavailable, storage.TryLoad(out _));
        Directory.CreateDirectory(Path.GetDirectoryName(data.Directory)!);
        storage.SaveConfirmed(new AppSettings { RetentionDays = 90 });
        Assert.False(storage.LoadFailed, "利用者が確定した設定は保存し、自動保存も再開する");
        Assert.Equal(90, data.LoadSettings().RetentionDays);
    }

    [Test]
    public void SettingsLoad_DistinguishesFirstRun()
    {
        var storage = new SettingsStorage(new DataStore(_dir.Path));
        Assert.Equal(SettingsLoadResult.NotFound, storage.TryLoad(out var settings), "初回起動");
        Assert.False(storage.LoadFailed);
        Assert.NotNull(settings);
        storage.SaveConfirmed(new AppSettings());
        Assert.Equal(SettingsLoadResult.Loaded, storage.TryLoad(out _));
    }

    // ---- 読めるようになったら、この起動中の記録とまとめる ----

    [Test]
    public void MergeLoaded_KeepsSavedHistoryAndKeeps_AndAddsWhatWasRecordedMeanwhile()
    {
        // 起動時に読めず空で始め、走査で .lnk から登録した
        var store = new HistoryStore();
        store.Register(@"C:\new.txt", Now.AddHours(-1), Now);
        store.Register(@"C:\same.txt", Now.AddDays(-2), Now);

        store.MergeLoaded(new List<HistoryRecord>
        {
            Record(@"C:\kept-old.txt", Now.AddDays(-100), 7, kept: true),
            Record(@"C:\same.txt", Now.AddDays(-2), 4, kept: true),
        }, new List<DeletedRecord>());

        Assert.Equal(3, store.Entries.Count, "保存してあった履歴が戻り、この起動中の記録も残る");
        var kept = store.Find(@"C:\kept-old.txt")!;
        Assert.True(kept.IsKept, "キープが戻る");
        Assert.Equal(7, kept.OpenCount);
        var same = store.Find(@"C:\same.txt")!;
        Assert.True(same.IsKept);
        Assert.Equal(4, same.OpenCount, "同じ日時の記録は同じ 1 回（重ねて数えない）");
        Assert.Equal(1, store.Find(@"C:\new.txt")!.OpenCount);
    }

    [Test]
    public void MergeLoaded_AddsOpensThatHappenedMeanwhile()
    {
        var store = new HistoryStore();
        // 保存後に 1 回開かれていた（.lnk が新しい）→ さらにこの起動中に 1 回開いた
        store.Register(@"C:\a.txt", Now.AddHours(-2), Now);
        store.Register(@"C:\a.txt", Now.AddHours(-1), Now);
        // 保存したときと同じ .lnk → この起動中に 1 回開いた
        store.Register(@"C:\b.txt", Now.AddDays(-1), Now);
        store.Register(@"C:\b.txt", Now.AddHours(-1), Now);

        store.MergeLoaded(new List<HistoryRecord>
        {
            Record(@"C:\a.txt", Now.AddDays(-1), 5),
            Record(@"C:\b.txt", Now.AddDays(-1), 5),
        }, new List<DeletedRecord>());

        Assert.Equal(7, store.Find(@"C:\a.txt")!.OpenCount, "保存してあった 5 回 + この起動中に分かった 2 回");
        Assert.Equal(6, store.Find(@"C:\b.txt")!.OpenCount, "保存してあった 5 回 + 開き直した 1 回");
        Assert.Equal(Now.AddHours(-1), store.Find(@"C:\a.txt")!.LastUsed, "日時は新しいほう");
    }

    [Test]
    public void MergeLoaded_RespectsDeletions()
    {
        var store = new HistoryStore();
        // 保存してあった「消した記録」より前の .lnk を、読めない間に登録してしまった
        store.Register(@"C:\deleted-before.txt", Now.AddDays(-3), Now);
        // 消した後に開き直された
        store.Register(@"C:\reopened.txt", Now.AddHours(-1), Now);
        // この起動中に消した
        store.Register(@"C:\deleted-now.txt", Now.AddDays(-1), Now);
        store.Remove(new[] { store.Find(@"C:\deleted-now.txt")! }, Now);

        store.MergeLoaded(new List<HistoryRecord>
        {
            Record(@"C:\deleted-now.txt", Now.AddDays(-1), 9, kept: true),
        }, new List<DeletedRecord>
        {
            new DeletedRecord { Path = @"C:\deleted-before.txt", DeletedAt = DateFormat.ToStorage(Now.AddDays(-2)) },
            new DeletedRecord { Path = @"C:\reopened.txt", DeletedAt = DateFormat.ToStorage(Now.AddDays(-2)) },
        });

        Assert.Null(store.Find(@"C:\deleted-before.txt"), "消した履歴を復活させない");
        Assert.NotNull(store.Find(@"C:\reopened.txt"), "消した後に開き直したものは残す");
        Assert.False(store.Deleted.ContainsKey(@"C:\reopened.txt"));
        Assert.Null(store.Find(@"C:\deleted-now.txt"), "この起動中に消したものは戻さない");
        var removed = store.Deleted[@"C:\deleted-now.txt"];
        Assert.True(removed.IsKept && removed.OpenCount == 9, "元に戻すときは、保存してあった状態（キープ・回数）で戻る");
        Assert.True(store.Deleted.ContainsKey(@"C:\deleted-before.txt"), "消した記録を覚え続ける");
    }

    // ---- 再接続前のネットワークドライブ ----

    [Test]
    public void RememberedNetworkDrive_NotYetReconnected_IsNotMissing()
    {
        var checkedPaths = new List<string>();
        var checker = new FileExistenceChecker(p =>
        {
            checkedPaths.Add(p);
            return false;
        }, root => root[0] switch
        {
            'Z' => DriveType.NoRootDirectory, // 再接続する設定だが、まだドライブ文字が使えない
            'Y' => null,
            _ => DriveType.NoRootDirectory, // USB を抜いたなど
        }, letter => letter is 'Z' or 'Y');

        Assert.False(checker.IsMissing(@"Z:\share\a.xlsx"), "再接続前のネットワークドライブは見つからない扱いにしない（開けば再接続される）");
        Assert.False(checker.IsMissing(@"z:\share\b.xlsx"), "小文字のドライブ文字も");
        Assert.False(checker.IsMissing(@"Y:\share\c.xlsx"));
        Assert.Equal(0, checkedPaths.Count, "応答を待つ File.Exists は呼ばない");
        Assert.True(checker.IsMissing(@"E:\usb\a.txt"), "ネットワークドライブでないドライブは従来どおり確かめる");
        Assert.Equal(1, checkedPaths.Count);
    }

    [Test]
    public void RealRegistryLookup_DoesNotThrow()
    {
        var checker = new FileExistenceChecker();
        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            checker.IsNetworkPath($@"{letter}:\a.txt");
        }
    }

    // ---- スタートアップ登録の確認 ----

    [Test]
    public void StartupRepair_DoesNotTouchNetworkExe()
    {
        var existsCalls = 0;
        bool Exists(string _)
        {
            existsCalls++;
            return false;
        }
        Assert.False(StartupCommand.NeedsRepair(@"\\server\tools\KeepHistory.exe", _ => true, Exists),
            "ネットワーク上の exe は、切断中に「無い」と誤って書き換えない");
        Assert.Equal(0, existsCalls, "応答を待つ確認をしない");
        Assert.True(StartupCommand.NeedsRepair(@"C:\Old\KeepHistory.exe", _ => false, Exists), "ローカルで消えていれば登録し直す");
        Assert.False(StartupCommand.NeedsRepair(@"C:\Tools\KeepHistory.exe", _ => false, _ => true));
        Assert.False(StartupCommand.NeedsRepair(null, _ => false, Exists), "登録が無ければ何もしない");
    }

    // ---- 「最近使った項目」の監視の始め直し ----

    /// <summary>
    /// 監視のテスト用のフォルダ。Windows のローカルの一時フォルダに作る（リポジトリの temp/ は WSL 上
    /// （\\wsl.localhost\…）でネットワーク扱いになり、FileSystemWatcher が自分でエラーを上げることがあるため）。
    /// </summary>
    private sealed class LocalFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"KeepHistory.Tests-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    [Test]
    public void Watcher_StopsOnError_AndCanBeRestarted()
    {
        using var local = new LocalFolder();
        var folder = local.Path;
        Directory.CreateDirectory(folder);
        using var watcher = new RecentFolderWatcher(folder);
        var rescans = 0;
        var changed = new List<string>();
        watcher.RescanRequired += () => Interlocked.Increment(ref rescans);
        watcher.LinkChanged += path =>
        {
            lock (changed) changed.Add(path);
        };
        Assert.True(watcher.Start());
        Assert.True(watcher.IsRunning);

        watcher.HandleError(new InternalBufferOverflowException());
        Assert.True(watcher.IsRunning, "バッファのあふれでは監視は続く");
        Assert.Equal(1, rescans, "あふれたら走査し直す");

        // ネットワークの切断など（FileSystemWatcher はエラーを 1 回上げて監視をやめる）
        watcher.HandleError(new IOException("指定されたネットワーク名は利用できません。"));
        Assert.False(watcher.IsRunning, "止まったことが分かる");
        Assert.Equal(2, rescans);

        Assert.True(watcher.Restart(), "始め直せる");
        Assert.True(watcher.IsRunning);
        Assert.False(watcher.Restart(), "動いていれば何もしない");

        var lnk = Path.Combine(folder, "a.lnk");
        File.WriteAllText(lnk, "x");
        Assert.True(WaitFor(() =>
        {
            lock (changed) return changed.Contains(lnk);
        }), "始め直した後も .lnk の変化を受け取る");
    }

    [Test]
    public void Watcher_CannotStartWhileFolderIsUnreachable_ThenStartsLater()
    {
        using var local = new LocalFolder();
        var folder = local.Path;
        using var watcher = new RecentFolderWatcher(folder);
        Assert.False(watcher.Start(), "届かない間は始められない（例外にしない）");
        Assert.False(watcher.IsRunning);
        Assert.False(watcher.Restart());

        Directory.CreateDirectory(folder);
        Assert.True(watcher.Restart(), "届くようになったら始め直せる");
        Assert.True(watcher.IsRunning);
    }
}
