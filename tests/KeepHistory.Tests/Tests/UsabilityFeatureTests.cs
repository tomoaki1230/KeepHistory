using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;

namespace KeepHistory.Tests.Tests;

/// <summary>1.2.0 の使い勝手の機能（画面を使わない部分）。</summary>
public sealed class UsabilityFeatureTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0); // 水曜日

    public UsabilityFeatureTests() => UiTestHost.EnsureApplication();

    // ---- 最近の検索語 ----

    [Test]
    public void RecentSearches_NewestFirst_NoDuplicates_AtMostTen()
    {
        var list = new RecentSearchList();
        var changes = 0;
        list.Changed += (_, _) => changes++;
        Assert.False(list.Remember("   "), "空白だけは覚えない");
        Assert.False(list.Remember(null));
        list.Remember("見積");
        list.Remember("  議事録   0926  ");
        Assert.SequenceEqual(new[] { "議事録 0926", "見積" }, list.Items, "新しい順・前後の空白を除き、続く空白は 1 つにまとめる");
        list.Remember("見積");
        Assert.SequenceEqual(new[] { "見積", "議事録 0926" }, list.Items, "同じ語は先頭へ移す（重ねない）");
        list.Remember("REPORT");
        list.Remember("report");
        Assert.Equal("report", list.Items[0], "大文字小文字だけ違う語は 1 つにまとめ、最後に使った書き方にする");
        Assert.Equal(3, list.Items.Count);
        Assert.False(list.Remember("report"), "先頭と同じなら何もしない");
        for (int i = 0; i < 15; i++) list.Remember("語" + i);
        Assert.Equal(RecentSearchList.MaxCount, list.Items.Count, "最大 10 件");
        Assert.Equal("語14", list.Items[0]);
        Assert.True(changes > 0);
    }

    [Test]
    public void RecentSearches_LoadDropsBrokenValues_AndClearNotifies()
    {
        var list = new RecentSearchList();
        var changes = 0;
        list.Changed += (_, _) => changes++;
        list.Load(new string?[] { null, "", "  ", "a", "A", "b" }.Concat(Enumerable.Range(0, 20).Select(i => "x" + i)));
        Assert.Equal("a", list.Items[0]);
        Assert.Equal("b", list.Items[1], "重複・空・null は捨てる");
        Assert.Equal(RecentSearchList.MaxCount, list.Items.Count, "多すぎる分は捨てる");
        Assert.Equal(0, changes, "読み込みでは保存を依頼しない");
        list.Clear();
        Assert.Equal(0, list.Items.Count);
        Assert.Equal(1, changes, "消したら保存を依頼する");
        list.Clear();
        Assert.Equal(1, changes, "空なら何もしない");
    }

    [Test]
    public void RecentSearches_AreSavedSeparatelyFromSettings()
    {
        using var dir = new TempDirectory("searches");
        var data = new DataStore(dir.Path);
        Assert.Equal(0, data.LoadRecentSearches().Count, "無ければ空");
        data.SaveRecentSearches(new List<string> { "見積", "議事録" });
        Assert.SequenceEqual(new[] { "見積", "議事録" }, data.LoadRecentSearches());
        Assert.False(File.Exists(data.SettingsPath), "settings.json には入れない（searches.json に分ける）");
        data.DeleteRecentSearches();
        Assert.False(File.Exists(data.RecentSearchesPath));
    }

    [Test]
    public void ViewModel_RemembersTheSearch_OnlyWhenThereIsOne()
    {
        var vm = new MainViewModel(new HistoryStore(), () => Now);
        vm.RememberSearch();
        Assert.Equal(0, vm.RecentSearches.Items.Count, "検索していなければ覚えない");
        vm.SearchText = "見積 2026";
        vm.RememberSearch();
        Assert.SequenceEqual(new[] { "見積 2026" }, vm.RecentSearches.Items);
    }

    // ---- 日付でまとめる ----

    [Test]
    public void DateGroups_SplitLikeExplorer_WeekStartsOnMonday()
    {
        void Check(DateTime now, DateTime lastUsed, DateGroup expected)
            => Assert.Equal(expected, DateGroups.Of(lastUsed, now), $"{lastUsed:yyyy/MM/dd (ddd)}（今は {now:yyyy/MM/dd (ddd)}）");

        // 水曜日
        Check(Now, Now.AddHours(-9), DateGroup.Today);
        Check(Now, Now.AddDays(1), DateGroup.Today); // 未来（時計のずれ）
        Check(Now, new DateTime(2026, 10, 13, 23, 59, 0), DateGroup.Yesterday);
        Check(Now, new DateTime(2026, 10, 12, 0, 0, 0), DateGroup.ThisWeek); // 月曜
        Check(Now, new DateTime(2026, 10, 11, 23, 0, 0), DateGroup.LastWeek); // 日曜
        Check(Now, new DateTime(2026, 10, 5), DateGroup.LastWeek);
        Check(Now, new DateTime(2026, 10, 4), DateGroup.ThisMonth);
        Check(Now, new DateTime(2026, 10, 1), DateGroup.ThisMonth);
        Check(Now, new DateTime(2026, 9, 30, 23, 0, 0), DateGroup.Earlier);

        // 月曜日: 昨日（日曜）は「昨日」を優先し、今週は今日だけ
        var monday = new DateTime(2026, 10, 12, 9, 0, 0);
        Check(monday, new DateTime(2026, 10, 11), DateGroup.Yesterday);
        Check(monday, new DateTime(2026, 10, 10), DateGroup.LastWeek);

        // 月の初め: 先月の分は今週・先週に入っても「今月」にはしない
        var first = new DateTime(2026, 10, 1, 9, 0, 0); // 木曜
        Check(first, new DateTime(2026, 9, 29), DateGroup.ThisWeek);
        Check(first, new DateTime(2026, 9, 21), DateGroup.LastWeek);
        Check(first, new DateTime(2026, 9, 20), DateGroup.Earlier);

        Assert.SequenceEqual(new[] { "今日", "昨日", "今週", "先週", "今月", "それより前" },
            Enum.GetValues<DateGroup>().Select(DateGroups.LabelOf));
    }

    private static (HistoryStore Store, MainViewModel Vm) CreateWithHistory()
    {
        var store = new HistoryStore();
        store.Register(@"C:\Docs\today.txt", Now.AddHours(-1), Now);
        store.Register(@"C:\Docs\yesterday.xlsx", Now.AddDays(-1), Now);
        store.Register(@"C:\Work\old.pdf", Now.AddDays(-40), Now);
        store.Register(@"C:\Docs\Sub\deeper.txt", Now.AddDays(-2), Now);
        return (store, new MainViewModel(store, () => Now));
    }

    [Test]
    public void GroupByDate_OnlyWhileSortedByLastUsed()
    {
        var (_, vm) = CreateWithHistory();
        Assert.False(vm.IsGroupedByDate, "設定を渡すまではまとめない（画面のテストに影響させない）");
        vm.GroupByDate = true;
        Assert.True(vm.IsGroupedByDate, "前回利用日時で並べているときはまとめる");
        var names = vm.View.Groups.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList();
        Assert.SequenceEqual(new[] { "今日", "昨日", "今週", "それより前" }, names, "新しい順に並ぶ");

        using (vm.View.DeferRefresh())
        {
            vm.View.SortDescriptions.Clear();
            vm.View.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.FileName), ListSortDirection.Ascending));
        }
        Assert.False(vm.IsGroupedByDate, "ほかの列で並べ替えたらまとめない");
        using (vm.View.DeferRefresh())
        {
            vm.View.SortDescriptions.Clear();
            vm.View.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.LastUsed), ListSortDirection.Ascending));
        }
        Assert.True(vm.IsGroupedByDate, "日時で並べ直したらまたまとめる（古い順でも）");
        vm.GroupByDate = false;
        Assert.False(vm.IsGroupedByDate, "設定をオフにしたらまとめない");
        Assert.True(new AppSettings().GroupByDate, "設定の既定はまとめる");
    }

    // ---- フォルダで絞り込む ----

    [Test]
    public void FolderFilter_ShowsOnlyFilesDirectlyInThatFolder()
    {
        var (_, vm) = CreateWithHistory();
        vm.FolderFilter = @"c:\docs\";
        var paths = vm.View.Cast<HistoryEntry>().Select(e => e.Path).ToList();
        Assert.SequenceEqual(new[] { @"C:\Docs\today.txt", @"C:\Docs\yesterday.xlsx" }, paths,
            "直下のファイルだけ（サブフォルダは含まない。大文字小文字・末尾の \\ は区別しない）");
        Assert.Contains(@"c:\docs\", vm.FolderFilterText);
        vm.SearchText = "today";
        Assert.Equal(1, vm.View.Count, "検索とも組み合わせられる");
        vm.FolderFilter = null;
        Assert.Null(vm.FolderFilterText);
        Assert.Equal(1, vm.View.Count);
        vm.SearchText = string.Empty;
        Assert.Equal(4, vm.View.Count, "解除したら元に戻る");
        Assert.True(FilterCriteria.SameFolder(@"C:\", @"c:"), "ドライブ直下も比べられる");
    }

    // ---- 削除済みのローカルファイルを表示しない ----

    [Test]
    public void HideMissingFiles_HidesOnlyMissingEntries_AndFollowsChanges()
    {
        var (store, vm) = CreateWithHistory();
        var gone = store.Find(@"C:\Work\old.pdf")!;
        gone.IsMissing = true;
        vm.NotifyStoreChanged();
        Assert.Equal(4, vm.View.Count, "既定では見つからないファイルも出す");
        Assert.True(new AppSettings().HideMissingFiles, "設定の既定は出さない（オン）");

        vm.HideMissingFiles = true;
        Assert.Equal(3, vm.View.Count);
        Assert.False(vm.View.Cast<HistoryEntry>().Contains(gone));

        // 見つかるようになったら（USB を挿し直したなど）、確かめ直した後にまた出る
        gone.IsMissing = false;
        vm.NotifyStoreChanged();
        Assert.Equal(4, vm.View.Count);
        Assert.True(store.Find(gone.Path) != null, "履歴からは消していない");

        // ネットワーク上のファイルは実在確認しない（IsMissing にならない）ので、切断中でも出たまま
        var checker = new FileExistenceChecker(_ => false, _ => System.IO.DriveType.Fixed);
        Assert.False(checker.IsMissing(@"\\server\share\a.xlsx"));
    }

    // ---- ネットワーク上のファイルを表示しない ----

    [Test]
    public void HideNetworkFiles_HidesUncAndMappedNetworkDrives()
    {
        var store = new HistoryStore();
        store.Register(@"C:\Local\a.txt", Now.AddHours(-1), Now);
        store.Register(@"\\server\share\b.xlsx", Now.AddHours(-2), Now);
        store.Register(@"Z:\mapped\c.docx", Now.AddHours(-3), Now);
        var vm = new MainViewModel(store, () => Now);
        Assert.Equal(3, vm.View.Count, "既定では出す");
        Assert.False(new AppSettings().HideNetworkFiles, "設定の既定は出す");

        // Z: は割り当てたネットワークドライブ、C: はローカル（ドライブ種別の問い合わせを差し替える）
        vm.NetworkPathCheckerFactory = () => new FileExistenceChecker(_ => true,
            root => root.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? System.IO.DriveType.Network : System.IO.DriveType.Fixed).IsNetworkPath;
        vm.HideNetworkFiles = true;
        Assert.SequenceEqual(new[] { @"C:\Local\a.txt" }, vm.View.Cast<HistoryEntry>().Select(e => e.Path),
            "UNC（\\\\server\\…）と割り当てたネットワークドライブを出さない");
        Assert.Equal(3, store.Entries.Count, "履歴からは消さない");
        vm.SearchText = "a";
        Assert.Equal(1, vm.View.Count, "検索と組み合わせられる");
        vm.HideNetworkFiles = false;
        vm.SearchText = string.Empty;
        Assert.Equal(3, vm.View.Count, "外すとまた出す");
    }

    // ---- キープした行は「表示しない」の対象にしない（1.2.1） ----

    [Test]
    public void KeptEntries_AreShown_EvenWhenMissingOrOnNetwork()
    {
        var store = new HistoryStore();
        store.Register(@"C:\Local\gone.xlsx", Now.AddHours(-1), Now);
        store.Register(@"C:\Local\kept-gone.xlsx", Now.AddHours(-2), Now);
        store.Register(@"\\server\share\b.docx", Now.AddHours(-3), Now);
        store.Register(@"\\server\share\kept.docx", Now.AddHours(-4), Now);
        store.Register(@"Z:\mapped\kept.pptx", Now.AddHours(-5), Now);
        store.Find(@"C:\Local\gone.xlsx")!.IsMissing = true;
        var keptGone = store.Find(@"C:\Local\kept-gone.xlsx")!;
        keptGone.IsMissing = true;
        keptGone.IsKept = true;
        store.Find(@"\\server\share\kept.docx")!.IsKept = true;
        store.Find(@"Z:\mapped\kept.pptx")!.IsKept = true;
        var vm = new MainViewModel(store, () => Now);
        vm.NetworkPathCheckerFactory = () => new FileExistenceChecker(_ => true,
            root => root.StartsWith("Z", StringComparison.OrdinalIgnoreCase) ? System.IO.DriveType.Network : System.IO.DriveType.Fixed).IsNetworkPath;
        IEnumerable<string> Shown() => vm.View.Cast<HistoryEntry>().Select(e => e.Path).OrderBy(p => p);

        vm.HideMissingFiles = true;
        Assert.SequenceEqual(new[] { @"\\server\share\b.docx", @"\\server\share\kept.docx", @"C:\Local\kept-gone.xlsx", @"Z:\mapped\kept.pptx" }.OrderBy(p => p), Shown(),
            "見つからないファイルでも、キープしていれば出す");

        vm.HideMissingFiles = false;
        vm.HideNetworkFiles = true;
        Assert.SequenceEqual(new[] { @"\\server\share\kept.docx", @"C:\Local\gone.xlsx", @"C:\Local\kept-gone.xlsx", @"Z:\mapped\kept.pptx" }.OrderBy(p => p), Shown(),
            "ネットワーク上のファイルでも、キープしていれば出す（UNC も割り当てたドライブも）");

        vm.HideMissingFiles = true;
        Assert.SequenceEqual(new[] { @"\\server\share\kept.docx", @"C:\Local\kept-gone.xlsx", @"Z:\mapped\kept.pptx" }.OrderBy(p => p), Shown(),
            "両方オンでも、キープした行はすべて出す");

        vm.KeptOnly = true;
        Assert.Equal(3, vm.View.Count, "「キープのみ」でも、隠れずに全部出る");

        // キープを外したら、また「表示しない」の対象になる
        vm.KeptOnly = false;
        keptGone.IsKept = false;
        vm.NotifyStoreChanged();
        Assert.False(Shown().Contains(@"C:\Local\kept-gone.xlsx"));
    }

    // ---- 履歴が多いときの検索 ----

    [Test]
    public void ManyEntries_SearchWaitsUntilTypingStops_AndCanBeFlushed()
    {
        var (_, vm) = CreateWithHistory();
        vm.SearchText = "today";
        Assert.False(vm.IsSearchPending, "少ないときは待たない");
        Assert.Equal(1, vm.View.Count, "少ないときは 1 文字ごとにすぐ絞り込む（従来どおり）");
        vm.SearchText = string.Empty;

        vm.DeferSearchThreshold = 4;
        vm.SearchText = "today";
        Assert.True(vm.IsSearchPending, "多いときは、入力が止まるまで待つ");
        Assert.Equal(4, vm.View.Count, "まだ絞り込まない");
        vm.FlushPendingSearch();
        Assert.False(vm.IsSearchPending);
        Assert.Equal(1, vm.View.Count, "Enter などの前に、すぐ絞り込める");

        vm.SearchText = string.Empty;
        Assert.False(vm.IsSearchPending, "消したときは待たずに全件に戻す");
        Assert.Equal(4, vm.View.Count);

        vm.SearchText = "old";
        var until = DateTime.UtcNow.AddSeconds(3);
        while (vm.IsSearchPending && DateTime.UtcNow < until) UiTestHost.DoEvents();
        Assert.False(vm.IsSearchPending, "入力が止まったら絞り込む");
        Assert.Equal(1, vm.View.Count);
    }

    // ---- 削除の取り消し ----

    [Test]
    public void UndoDelete_RestoresInReverseOrder_WithKeepAndCount()
    {
        var (store, vm) = CreateWithHistory();
        var today = store.Find(@"C:\Docs\today.txt")!;
        today.IsKept = true;
        today.OpenCount = 7;
        var changed = 0;
        vm.HistoryChanged += (_, _) => changed++;
        vm.Delete(new[] { today });
        vm.Delete(new[] { store.Find(@"C:\Work\old.pdf")! });
        Assert.True(vm.CanUndoDelete);
        Assert.Equal(2, vm.View.Count);

        var first = vm.UndoDelete();
        Assert.Equal(@"C:\Work\old.pdf", first.Single().Path, "最後に消したものから戻す");
        var second = vm.UndoDelete();
        var restored = second.Single();
        Assert.Equal(@"C:\Docs\today.txt", restored.Path);
        Assert.True(restored.IsKept && restored.OpenCount == 7, "キープ・回数も元どおり");
        Assert.False(store.Deleted.ContainsKey(restored.Path), "消した記録も消える（次の走査で消えない）");
        Assert.Equal(4, vm.View.Count);
        Assert.Equal(4, changed, "削除 2 回と取り消し 2 回で保存を依頼する");
        Assert.False(vm.CanUndoDelete);
        Assert.Equal(0, vm.UndoDelete().Count, "戻せるものが無ければ空");
    }

    [Test]
    public void UndoDelete_SkipsWhatCanNoLongerBeRestored_AndKeepsAtMostTwenty()
    {
        var store = new HistoryStore();
        var vm = new MainViewModel(store, () => Now);
        for (int i = 0; i < 25; i++) store.Register($@"C:\f{i}.txt", Now.AddMinutes(-i), Now);
        for (int i = 0; i < 25; i++) vm.Delete(new[] { store.Find($@"C:\f{i}.txt")! });
        // 最後に消したものは、その後に開き直された（もう一覧にある）
        store.Register(@"C:\f24.txt", Now.AddMinutes(5), Now);
        Assert.Equal(@"C:\f23.txt", vm.UndoDelete().Single().Path, "戻せないものは飛ばして、その前の削除を戻す");
        var count = 0;
        while (vm.UndoDelete().Count > 0) count++;
        Assert.Equal(MainViewModel.MaxUndo - 2, count, "覚えているのは最後の 20 回まで");
    }

    // ---- ファイルの種類のアイコン ----

    [Test]
    public void FileIcons_AreTakenFromTheExtension_AndReused()
    {
        var txt = FileIcons.SmallIconFor(".txt");
        Assert.NotNull(txt, "拡張子からアイコンが取れる");
        var before = FileIcons.CreatedCount;
        Assert.Same(txt, FileIcons.SmallIconFor(".TXT"), "同じ拡張子（大文字小文字は区別しない）は使い回す");
        Assert.Equal(before, FileIcons.CreatedCount, "作り直さない");
        Assert.NotNull(FileIcons.SmallIconFor(string.Empty), "拡張子が無くても出る");
        Assert.NotNull(FileIcons.SmallIconFor(".keephistory-unknown-ext"), "知らない拡張子でも出る（共通のアイコン）");
        Assert.True(((BitmapSource)txt!).IsFrozen, "画面のスレッド以外でも描けるよう固めてある");
    }

}
