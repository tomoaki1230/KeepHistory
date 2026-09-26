using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class AppDataResetTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0);
    private readonly TempDirectory _dir = new("app-data-reset");

    public void Dispose() => _dir.Dispose();

    [Test]
    public void Run_DeletesSettingsHistoryKeepAndDeleted_FilesAndMemory()
    {
        var data = new DataStore(_dir.Path);
        var settings = new SettingsStorage(data);
        var store = new HistoryStore();
        store.Register(@"C:\a.txt", Now.AddHours(-2), Now);
        store.Register(@"C:\b.txt", Now.AddHours(-1), Now);
        store.Find(@"C:\a.txt")!.IsKept = true;
        store.Remove(new[] { store.Find(@"C:\b.txt")! }, Now);
        settings.SaveConfirmed(new AppSettings { StayResident = true });
        data.SaveHistory(store.ToHistoryRecords());
        data.SaveDeleted(store.ToDeletedRecords());
        File.WriteAllText(data.HistoryPath + ".tmp", "書きかけ");
        File.WriteAllText(Path.Combine(_dir.Path, "error.log"), "ログ");

        var failures = AppDataReset.Run(settings, data, store);

        Assert.Equal(0, failures.Count, "すべて消せた");
        Assert.False(File.Exists(data.SettingsPath), "settings.json を消す");
        Assert.False(File.Exists(data.HistoryPath), "history.json（履歴・キープ）を消す");
        Assert.False(File.Exists(data.DeletedPath), "deleted.json（削除した履歴）を消す");
        Assert.False(File.Exists(data.HistoryPath + ".tmp"), "書きかけの一時ファイルも消す");
        Assert.Equal(0, store.Entries.Count, "メモリ上の履歴も同時に空にする");
        Assert.Equal(0, store.Deleted.Count);
        Assert.True(settings.IsSuspended, "設定は自動保存で作り直さない（次回起動は初回扱い）");
        Assert.True(File.Exists(Path.Combine(_dir.Path, "error.log")), "error.log は対象外");

        // 読み込み直しても空
        var reloaded = new HistoryStore();
        reloaded.Load(data.LoadHistory(), data.LoadDeleted());
        Assert.Equal(0, reloaded.Entries.Count);
        Assert.Equal(0, reloaded.Deleted.Count);
    }

    [Test]
    public void Run_WhenHistoryFileIsLocked_ReportsFailure_AndStillResetsTheRest()
    {
        var data = new DataStore(_dir.Path);
        var settings = new SettingsStorage(data);
        var store = new HistoryStore();
        store.Register(@"C:\a.txt", Now, Now);
        settings.SaveConfirmed(new AppSettings());
        data.SaveHistory(store.ToHistoryRecords());

        IReadOnlyList<string> failures;
        using (new FileStream(data.HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failures = AppDataReset.Run(settings, data, store);
        }

        Assert.SequenceEqual(new[] { "履歴（history.json・deleted.json）" }, failures, "消せなかったものを知らせる");
        Assert.False(File.Exists(data.SettingsPath), "ほかのものは消す");
        Assert.Equal(0, store.Entries.Count, "メモリ上の履歴は空にする（次の保存で上書きされる）");
        Assert.True(settings.IsSuspended);
    }

    [Test]
    public void Run_WhenSettingsFileIsLocked_KeepsAutoSaveSoDefaultsAreWritten()
    {
        var data = new DataStore(_dir.Path);
        var settings = new SettingsStorage(data);
        settings.SaveConfirmed(new AppSettings { StayResident = true });

        IReadOnlyList<string> failures;
        using (new FileStream(data.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            failures = AppDataReset.Run(settings, data, new HistoryStore());
        }

        Assert.SequenceEqual(new[] { "設定（settings.json）" }, failures);
        Assert.False(settings.IsSuspended, "消せなかった設定は、既定値の自動保存で上書きできるよう止めない");
    }

    [Test]
    public void Run_WhenNoFiles_DoesNotFail()
    {
        var data = new DataStore(_dir.Path);
        AppDataReset.Run(new SettingsStorage(data), data, new HistoryStore());
        Assert.False(Directory.EnumerateFiles(_dir.Path).Any(f => f.EndsWith(".json")));
    }
}
