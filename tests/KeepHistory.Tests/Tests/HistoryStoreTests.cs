using System;
using System.Linq;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class HistoryStoreTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0);

    private static HistoryStore NewStore() => new()
    {
        RetentionDays = 365,
        Exclusion = new ExclusionFilter(ExclusionFilter.DefaultPatterns),
    };

    [Test]
    public void Register_AddsNewAndUpdatesOnlyWhenNewer()
    {
        var store = NewStore();
        Assert.True(store.Register(@"C:\a.txt", Now.AddHours(-2), Now));
        Assert.False(store.Register(@"C:\A.TXT", Now.AddHours(-3), Now), "パスは大文字小文字を区別せず 1 件");
        Assert.True(store.Register(@"C:\a.txt", Now.AddHours(-1), Now));
        Assert.Equal(1, store.Entries.Count);
        Assert.Equal(Now.AddHours(-1), store.Entries[0].LastUsed);
    }

    [Test]
    public void Register_SkipsExcludedAndExpired()
    {
        var store = NewStore();
        Assert.False(store.Register(@"C:\~$Book.xlsx", Now, Now));
        Assert.False(store.Register(@"C:\old.txt", Now.AddDays(-400), Now));
        Assert.Equal(0, store.Entries.Count);
    }

    [Test]
    public void Deleted_IsNotRevivedByTheSameLink()
    {
        // deleted.json が無いと、消しても次の走査で .lnk から復活してしまう
        var store = NewStore();
        var lnkTime = Now.AddHours(-1).AddTicks(1234567);
        store.Register(@"C:\a.txt", lnkTime, Now);
        store.Remove(store.Entries.ToList(), Now);
        Assert.Equal(0, store.Entries.Count);

        Assert.False(store.Register(@"C:\a.txt", lnkTime, Now.AddMinutes(1)), "同じ .lnk の再走査で復活しない");
        Assert.Equal(0, store.Entries.Count);
        Assert.True(store.Deleted.ContainsKey(@"C:\A.txt"));
    }

    [Test]
    public void Deleted_IsRevivedWhenOpenedAgainLater()
    {
        var store = NewStore();
        store.Register(@"C:\a.txt", Now.AddHours(-1), Now);
        store.Remove(store.Entries.ToList(), Now);

        Assert.True(store.Register(@"C:\a.txt", Now.AddMinutes(5), Now.AddMinutes(6)), "消した後に開き直されたら記録する");
        Assert.Equal(1, store.Entries.Count);
        Assert.False(store.Deleted.ContainsKey(@"C:\a.txt"));
    }

    [Test]
    public void Deleted_SurvivesSaveAndLoad()
    {
        var store = NewStore();
        var lnkTime = Now.AddHours(-1).AddTicks(1234567);
        store.Register(@"C:\a.txt", lnkTime, Now);
        store.Remove(store.Entries.ToList(), lnkTime); // 消した時刻 = .lnk の時刻（秒未満あり）

        var reloaded = NewStore();
        reloaded.Load(store.ToHistoryRecords(), store.ToDeletedRecords());
        Assert.False(reloaded.Register(@"C:\a.txt", lnkTime, Now), "保存・読み込み後も復活しない（秒未満を失わない）");
    }

    [Test]
    public void Purge_RemovesExpiredButKeepsKept()
    {
        var store = NewStore();
        store.Register(@"C:\old.txt", Now.AddDays(-300), Now);
        store.Register(@"C:\kept.txt", Now.AddDays(-300), Now);
        store.Register(@"C:\new.txt", Now.AddDays(-1), Now);
        store.Find(@"C:\kept.txt")!.IsKept = true;

        Assert.True(store.Purge(Now.AddDays(100)));
        var paths = store.Entries.Select(e => e.Path).OrderBy(p => p).ToList();
        Assert.SequenceEqual(new[] { @"C:\kept.txt", @"C:\new.txt" }, paths);
        Assert.False(store.Purge(Now.AddDays(100)), "2 回目は変化なし");
    }

    [Test]
    public void Purge_ForgetsStaleDeletedRecords()
    {
        var store = NewStore();
        store.Register(@"C:\a.txt", Now.AddDays(-10), Now);
        store.Remove(store.Entries.ToList(), Now.AddDays(-10));
        Assert.True(store.Purge(Now.AddDays(400)));
        Assert.Equal(0, store.Deleted.Count);
        Assert.False(store.Register(@"C:\a.txt", Now.AddDays(-10), Now.AddDays(400)), "記憶を捨てても期限切れなので復活しない");
    }

    [Test]
    public void RemoveExcluded_KeepsKeptEntries()
    {
        var store = NewStore();
        store.Register(@"C:\a.log", Now, Now);
        store.Register(@"C:\b.log", Now, Now);
        store.Register(@"C:\c.txt", Now, Now);
        store.Find(@"C:\b.log")!.IsKept = true;
        store.Exclusion = new ExclusionFilter(new[] { "*.log" });

        Assert.Equal(1, store.RemoveExcluded());
        Assert.SequenceEqual(new[] { @"C:\b.log", @"C:\c.txt" }, store.Entries.Select(e => e.Path).OrderBy(p => p));
    }

    [Test]
    public void RefreshMissing_MarksMissingButKeepsInList()
    {
        var store = NewStore();
        store.Register(@"C:\exists.txt", Now, Now);
        store.Register(@"C:\gone.txt", Now, Now);
        var checker = new FileExistenceChecker(p => p.EndsWith("exists.txt"), _ => System.IO.DriveType.Fixed);

        Assert.Equal(1, store.RefreshMissing(checker));
        Assert.True(store.Find(@"C:\gone.txt")!.IsMissing);
        Assert.False(store.Find(@"C:\exists.txt")!.IsMissing);
        Assert.Equal(2, store.Entries.Count, "見つからないファイルも一覧から消さない");
    }

    [Test]
    public void Load_MergesDuplicatesAndSkipsBrokenRecords()
    {
        var store = NewStore();
        store.Load(new[]
        {
            new Models.HistoryRecord { Path = @"C:\a.txt", LastUsed = "2026-09-01T10:00:00", IsKept = true },
            new Models.HistoryRecord { Path = @"c:\A.TXT", LastUsed = "2026-09-02T10:00:00" },
            new Models.HistoryRecord { Path = @"C:\bad.txt", LastUsed = "令和8年9月1日" },
            new Models.HistoryRecord { Path = "", LastUsed = "2026-09-02T10:00:00" },
        }, null);
        Assert.Equal(1, store.Entries.Count);
        Assert.True(store.Entries[0].IsKept);
        Assert.Equal(new DateTime(2026, 9, 2, 10, 0, 0), store.Entries[0].LastUsed);
    }
}
