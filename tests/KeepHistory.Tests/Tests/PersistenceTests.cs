using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;

namespace KeepHistory.Tests.Tests;

/// <summary>
/// アプリの組み立て（AppController）から切り出した、保存まわりの決まり。
/// 起動時に読めなかった履歴・検索語を上書きしない、読めたらまとめる、表示の設定を保存へ回す。
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0);
    private readonly TempDirectory _dir = new("persistence");
    private readonly int _attempts = DataStore.ReadAttempts;
    private readonly TimeSpan _delay = DataStore.RetryDelay;

    public PersistenceTests()
    {
        UiTestHost.EnsureApplication();
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

    [Test]
    public void History_UnreadableAtStartup_IsNotOverwritten_ThenMergedWhenReadable()
    {
        var data = new DataStore(_dir.Path);
        data.SaveHistory(new() { Record(@"C:\kept.xlsx", Now.AddDays(-30), 5, kept: true) });
        data.SaveDeleted(new());
        var before = File.ReadAllBytes(data.HistoryPath);

        var store = new HistoryStore();
        var history = new HistoryPersistence(data, store);
        using (new FileStream(data.HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            history.LoadAtStartup();
        }
        Assert.True(history.LoadFailed, "読めなかったことを覚える");
        Assert.Equal(0, store.Entries.Count, "空で始める");

        // 読めない間に走査で登録して保存しようとしても、上書きしない
        store.Register(@"C:\new.txt", Now.AddHours(-1), Now);
        history.Save();
        Assert.SequenceEqual(before, File.ReadAllBytes(data.HistoryPath), "読めるまで history.json を上書きしない（履歴・キープが消えない）");

        // 読めるようになったら、この起動中の記録とまとめて保存する
        var (records, deleted) = history.ReadForRecovery();
        Assert.True(history.ApplyRecovered(records, deleted, Now));
        Assert.False(history.LoadFailed, "以後は保存してよい");
        var saved = data.LoadHistory();
        Assert.SequenceEqual(new[] { @"C:\kept.xlsx", @"C:\new.txt" }, saved.Select(r => r.Path).OrderBy(p => p), "両方が保存される");
        Assert.True(saved.Single(r => r.Path == @"C:\kept.xlsx").IsKept, "キープも残る");
    }

    [Test]
    public void History_RecoveryAfterReset_IsIgnored()
    {
        var data = new DataStore(_dir.Path);
        data.SaveHistory(new() { Record(@"C:\old.txt", Now.AddDays(-1)) });
        data.SaveDeleted(new());
        var store = new HistoryStore();
        var history = new HistoryPersistence(data, store);
        using (new FileStream(data.HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            history.LoadAtStartup();
        }
        var (records, deleted) = history.ReadForRecovery();

        // 読み直している間に「すべて初期状態に戻す」をした
        history.ForgetLoadFailure();
        Assert.False(history.ApplyRecovered(records, deleted, Now), "読み直した内容は使わない");
        Assert.Equal(0, store.Entries.Count, "初期状態のまま（古い履歴を戻さない）");
    }

    [Test]
    public void History_ReadableAtStartup_LoadsAndSaves_AndSaveFailureDoesNotThrow()
    {
        var data = new DataStore(_dir.Path);
        data.SaveHistory(new() { Record(@"C:\a.txt", Now.AddDays(-1), 3) });
        var store = new HistoryStore();
        var history = new HistoryPersistence(data, store);
        history.LoadAtStartup();
        Assert.False(history.LoadFailed);
        Assert.Equal(3, store.Find(@"C:\a.txt")!.OpenCount, "読めたら読み込む");

        store.Register(@"C:\b.txt", Now, Now);
        using (new FileStream(data.HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            history.Save(); // ほかのソフトがつかんでいて保存できない
        }
        Assert.Equal(1, data.LoadHistory().Count, "保存できなくても例外にしない（記録だけ）");
        history.Save();
        Assert.Equal(2, data.LoadHistory().Count, "次の保存で書ける");
    }

    [Test]
    public void RecentSearches_AreSaved_DeletedWhenEmpty_AndNotOverwrittenWhenUnreadable()
    {
        var data = new DataStore(_dir.Path);
        var list = new RecentSearchList();
        var searches = new RecentSearchPersistence(data, list);
        searches.Load();
        list.Remember("見積");
        searches.Save();
        Assert.SequenceEqual(new[] { "見積" }, data.LoadRecentSearches(), "覚えた語を保存する");
        list.Clear();
        searches.Save();
        Assert.False(File.Exists(data.RecentSearchesPath), "0 件になったらファイルを消す（見られたくない語を残さない）");

        // 起動時に読めなかったら、この起動中は保存しない（保存してあった語を消さない）
        data.SaveRecentSearches(new() { "議事録" });
        var other = new RecentSearchList();
        var unreadable = new RecentSearchPersistence(data, other);
        using (new FileStream(data.RecentSearchesPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            unreadable.Load();
        }
        Assert.True(unreadable.LoadFailed);
        other.Remember("新しい語");
        unreadable.Save();
        Assert.SequenceEqual(new[] { "議事録" }, data.LoadRecentSearches(), "上書きしない");
        unreadable.ForgetLoadFailure();
        unreadable.Save();
        Assert.SequenceEqual(new[] { "新しい語" }, data.LoadRecentSearches(), "初期状態に戻した後は保存してよい");
    }

    [Test]
    public void ViewOptions_MenuChangesGoToSettings_AndSettingsGoToTheList()
    {
        var vm = new MainViewModel(new HistoryStore(), () => Now);
        var settings = new AppSettings { GroupByDate = true, HideMissingFiles = true, HideNetworkFiles = true };
        ViewOptions.Apply(settings, vm);
        Assert.True(vm.GroupByDate && vm.HideMissingFiles && vm.HideNetworkFiles, "設定の値を一覧に入れる");

        vm.GroupByDate = false;
        Assert.True(ViewOptions.TryTake(vm, settings, new PropertyChangedEventArgs(nameof(MainViewModel.GroupByDate))), "メニューで変えたら保存が必要");
        Assert.False(settings.GroupByDate);
        vm.HideMissingFiles = false;
        Assert.True(ViewOptions.TryTake(vm, settings, new PropertyChangedEventArgs(nameof(MainViewModel.HideMissingFiles))));
        Assert.False(settings.HideMissingFiles);
        vm.HideNetworkFiles = false;
        Assert.True(ViewOptions.TryTake(vm, settings, new PropertyChangedEventArgs(nameof(MainViewModel.HideNetworkFiles))));
        Assert.False(settings.HideNetworkFiles);

        Assert.False(ViewOptions.TryTake(vm, settings, new PropertyChangedEventArgs(nameof(MainViewModel.GroupByDate))),
            "設定と同じ値なら保存しない（設定画面から一覧へ入れたときに、保存し直さない）");
        Assert.False(ViewOptions.TryTake(vm, settings, new PropertyChangedEventArgs(nameof(MainViewModel.SearchText))),
            "表示の設定以外の変化では保存しない（検索語は保存しない決まり）");
    }
}
