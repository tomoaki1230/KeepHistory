using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using KeepHistory.Models;
using KeepHistory.Services;

namespace KeepHistory.ViewModels;

/// <summary>
/// 履歴画面の状態。検索語・絞り込み・並び順はメモリ上だけで持ち、保存しない
/// （最近の検索語は別に RecentSearches で持ち、searches.json に保存する）。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public static readonly Choice<string?> AllExtensions = new("すべての種類", null);

    /// <summary>削除の取り消し（Ctrl+Z）で覚えておく回数。</summary>
    public const int MaxUndo = 20;

    private readonly HistoryStore _store;
    private readonly Func<DateTime> _clock;
    private readonly DispatcherTimer _searchTimer;
    private readonly List<IReadOnlyList<string>> _undo = new();
    private FilterCriteria _criteria = new();
    private string _searchText = string.Empty;
    private Choice<string?> _selectedExtension = AllExtensions;
    private Choice<PeriodOption> _selectedPeriod;
    private bool _keptOnly;
    private string? _folderFilter;
    private bool _groupByDate;
    private bool _hideMissingFiles;
    private bool _hideNetworkFiles;
    private bool _searchPending;

    public MainViewModel(HistoryStore store, Func<DateTime> clock)
    {
        _store = store;
        _clock = clock;
        _selectedPeriod = PeriodChoices[0];

        View = new ListCollectionView(store.Entries);
        View.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.LastUsed), ListSortDirection.Descending));
        View.Filter = item => item is HistoryEntry entry && _criteria.Matches(entry, _clock());
        // 列見出しで並べ替えたら、日付でまとめるかを決め直す（日時で並べているときだけまとめる）
        ((INotifyCollectionChanged)View.SortDescriptions).CollectionChanged += (_, _) => UpdateGrouping();

        ExtensionChoices = new ObservableCollection<Choice<string?>> { AllExtensions };
        RebuildExtensionChoices();

        // 履歴が多いときは、入力が止まってから検索する（1 文字ごとの絞り込み・描き直しが重いため。8.6）
        _searchTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = SearchDelay };
        _searchTimer.Tick += (_, _) => FlushPendingSearch();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>履歴に変更があった（保存が必要）。</summary>
    public event EventHandler? HistoryChanged;

    public ListCollectionView View { get; }

    public ObservableCollection<Choice<string?>> ExtensionChoices { get; }

    public IReadOnlyList<Choice<PeriodOption>> PeriodChoices => PeriodOptions.Choices;

    /// <summary>最近の検索語。</summary>
    public RecentSearchList RecentSearches { get; } = new();

    /// <summary>この件数以上の履歴があるときは、入力が止まってから検索する（テストで差し替える）。</summary>
    internal int DeferSearchThreshold { get; set; } = 1000;

    /// <summary>入力が止まったとみなすまでの時間。</summary>
    internal static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>入力が止まるのを待っている検索があるか。</summary>
    internal bool IsSearchPending => _searchPending;

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            if (_store.Entries.Count >= DeferSearchThreshold && value.Length > 0)
            {
                // 打ち終わるまで待つ（消したときはすぐ全件に戻す）
                _searchPending = true;
                _searchTimer.Stop();
                _searchTimer.Start();
                return;
            }
            ApplyFilter();
        }
    }

    /// <summary>
    /// 待っている検索があれば、すぐ絞り込む。Enter・↓ などで一覧を使う直前に呼ぶ
    /// （待っている間に Enter を押して、古い絞り込みのファイルを開かないように）。
    /// </summary>
    public void FlushPendingSearch()
    {
        _searchTimer.Stop();
        if (!_searchPending) return;
        ApplyFilter();
    }

    public Choice<string?> SelectedExtension
    {
        get => _selectedExtension;
        set
        {
            // 選択肢の入れ替え中に null が来ることがあるので無視する
            if (value == null || ReferenceEquals(_selectedExtension, value)) return;
            _selectedExtension = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public Choice<PeriodOption> SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (value == null || ReferenceEquals(_selectedPeriod, value)) return;
            _selectedPeriod = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool KeptOnly
    {
        get => _keptOnly;
        set
        {
            if (_keptOnly == value) return;
            _keptOnly = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>このフォルダの直下のファイルだけを表示する（右クリックの「このフォルダのファイルを表示」）。null なら絞り込まない。</summary>
    public string? FolderFilter
    {
        get => _folderFilter;
        set
        {
            if (string.IsNullOrEmpty(value)) value = null;
            if (_folderFilter == value) return;
            _folderFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FolderFilterText));
            ApplyFilter();
        }
    }

    /// <summary>フォルダで絞り込んでいるときの表示文。</summary>
    public string? FolderFilterText => _folderFilter == null ? null : $"フォルダ「{_folderFilter}」のファイルだけを表示しています";

    /// <summary>前回利用日時で並べているとき、一覧を「今日」「昨日」などでまとめるか（設定）。</summary>
    public bool GroupByDate
    {
        get => _groupByDate;
        set
        {
            if (_groupByDate == value) return;
            _groupByDate = value;
            OnPropertyChanged();
            UpdateGrouping();
        }
    }

    /// <summary>削除・移動などで見つからないローカルのファイルを一覧に出さないか（設定。表示メニュー）。</summary>
    public bool HideMissingFiles
    {
        get => _hideMissingFiles;
        set
        {
            if (_hideMissingFiles == value) return;
            _hideMissingFiles = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>ネットワーク上のファイルを一覧に出さないか（設定。表示メニュー）。</summary>
    public bool HideNetworkFiles
    {
        get => _hideNetworkFiles;
        set
        {
            if (_hideNetworkFiles == value) return;
            _hideNetworkFiles = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>
    /// ネットワーク上のパスかを判定する仕組みを作る（絞り込みのたびに 1 つ作り、ドライブ種別はその中で使い回す。テストで差し替える）。
    /// </summary>
    internal Func<Func<string, bool>> NetworkPathCheckerFactory { get; set; } = () => new FileExistenceChecker().IsNetworkPath;

    /// <summary>今、日付でまとめて表示しているか。</summary>
    public bool IsGroupedByDate => View.GroupDescriptions.Count > 0;

    public string StatusText => $"表示 {View.Count} 件 / 全 {_store.Entries.Count} 件";

    public const string NoHistoryMessage = "履歴はまだありません。\nファイルを開くと、ここに表示されます。";
    public const string NoMatchMessage = "条件に一致する履歴はありません。\n検索語や絞り込み（種類・期間・キープのみ・フォルダ）を変えてみてください。";

    /// <summary>一覧が空のときの案内。空でなければ null。</summary>
    public string? EmptyMessage => View.Count > 0 ? null : _store.Entries.Count == 0 ? NoHistoryMessage : NoMatchMessage;

    /// <summary>一覧で強調する検索語（正規化済み）。</summary>
    public IReadOnlyList<string> HighlightTerms => _criteria.Query.NormalizedTerms;

    /// <summary>絞り込みをやり直す。</summary>
    public void ApplyFilter()
    {
        _searchTimer.Stop();
        _searchPending = false;
        _criteria = new FilterCriteria
        {
            Query = SearchQuery.Parse(_searchText),
            Extension = _selectedExtension.Value,
            Period = _selectedPeriod.Value,
            KeptOnly = _keptOnly,
            Folder = _folderFilter,
            HideMissing = _hideMissingFiles,
            IsNetworkPath = _hideNetworkFiles ? NetworkPathCheckerFactory() : null,
        };
        View.Refresh();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HighlightTerms));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>履歴の中身が変わった後に呼ぶ（拡張子の選択肢と表示を作り直す）。</summary>
    public void NotifyStoreChanged()
    {
        RebuildExtensionChoices();
        ApplyFilter();
    }

    /// <summary>いまの検索語を「最近の検索語」に覚える（検索してファイルを開いたときなど）。</summary>
    public void RememberSearch() => RecentSearches.Remember(_searchText);

    public void SetKept(IReadOnlyList<HistoryEntry> items, bool kept)
    {
        if (items.Count == 0) return;
        foreach (var item in items) item.IsKept = kept;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        ApplyFilter();
    }

    /// <summary>1 件でも未キープがあれば全部キープ、全部キープ済みなら全部解除。</summary>
    public void ToggleKept(IReadOnlyList<HistoryEntry> items)
        => SetKept(items, items.Any(i => !i.IsKept));

    public void Delete(IReadOnlyList<HistoryEntry> items)
    {
        if (items.Count == 0) return;
        _store.Remove(items, _clock());
        _undo.Add(items.Select(i => i.Path).ToList());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        OnPropertyChanged(nameof(CanUndoDelete));
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        NotifyStoreChanged();
    }

    /// <summary>取り消せる削除があるか。</summary>
    public bool CanUndoDelete => _undo.Count > 0;

    /// <summary>
    /// 直前の削除を取り消す（Ctrl+Z）。削除前の状態（日時・回数・キープ）のまま一覧に戻し、戻した履歴を返す。
    /// 削除した後に開き直された・初期状態に戻したなどで戻せないものは飛ばす。戻せる削除が無ければ空。
    /// </summary>
    public IReadOnlyList<HistoryEntry> UndoDelete()
    {
        while (_undo.Count > 0)
        {
            var paths = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            if (_store.Restore(paths) == 0) continue;
            OnPropertyChanged(nameof(CanUndoDelete));
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            NotifyStoreChanged();
            return paths.Select(_store.Find).Where(e => e != null).Select(e => e!).ToList();
        }
        OnPropertyChanged(nameof(CanUndoDelete));
        return Array.Empty<HistoryEntry>();
    }

    /// <summary>日付でまとめるか決め直す。設定がオンで、いちばん先の並べ替えが前回利用日時のときだけまとめる。</summary>
    private void UpdateGrouping()
    {
        var byDate = _groupByDate
                     && View.SortDescriptions.Count > 0
                     && View.SortDescriptions[0].PropertyName == nameof(HistoryEntry.LastUsed);
        if (byDate == IsGroupedByDate) return;
        if (byDate)
        {
            View.GroupDescriptions.Add(new PropertyGroupDescription(null, new DateGroupConverter(_clock)));
        }
        else
        {
            View.GroupDescriptions.Clear();
        }
        OnPropertyChanged(nameof(IsGroupedByDate));
    }

    /// <summary>履歴を「今日」「昨日」などの見出しに振り分ける。</summary>
    private sealed class DateGroupConverter : IValueConverter
    {
        private readonly Func<DateTime> _clock;

        public DateGroupConverter(Func<DateTime> clock) => _clock = clock;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is HistoryEntry entry ? DateGroups.LabelOf(DateGroups.Of(entry.LastUsed, _clock())) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private void RebuildExtensionChoices()
    {
        var wanted = _store.Entries.Select(e => e.Extension).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();

        // 選択中の項目は、該当が無くなっても残す（選択が勝手に外れないように）
        for (int i = ExtensionChoices.Count - 1; i >= 1; i--)
        {
            var choice = ExtensionChoices[i];
            if (!ReferenceEquals(choice, _selectedExtension) && !wanted.Contains(choice.Value!, StringComparer.OrdinalIgnoreCase))
            {
                ExtensionChoices.RemoveAt(i);
            }
        }
        foreach (var ext in wanted)
        {
            if (ExtensionChoices.Skip(1).Any(c => string.Equals(c.Value, ext, StringComparison.OrdinalIgnoreCase))) continue;
            var newChoice = new Choice<string?>(ext.Length == 0 ? "(拡張子なし)" : ext, ext);
            int index = 1;
            while (index < ExtensionChoices.Count
                   && string.Compare(ExtensionChoices[index].Value, ext, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }
            ExtensionChoices.Insert(index, newChoice);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
