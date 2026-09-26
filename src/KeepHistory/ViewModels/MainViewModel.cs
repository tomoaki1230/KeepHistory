using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using KeepHistory.Models;
using KeepHistory.Services;

namespace KeepHistory.ViewModels;

/// <summary>
/// 履歴画面の状態。検索語・絞り込み・並び順はメモリ上だけで持ち、保存しない。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public static readonly Choice<string?> AllExtensions = new("すべての種類", null);

    private readonly HistoryStore _store;
    private readonly Func<DateTime> _clock;
    private FilterCriteria _criteria = new();
    private string _searchText = string.Empty;
    private Choice<string?> _selectedExtension = AllExtensions;
    private Choice<PeriodOption> _selectedPeriod;
    private bool _keptOnly;

    public MainViewModel(HistoryStore store, Func<DateTime> clock)
    {
        _store = store;
        _clock = clock;
        _selectedPeriod = PeriodChoices[0];

        View = new ListCollectionView(store.Entries);
        View.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.LastUsed), ListSortDirection.Descending));
        View.Filter = item => item is HistoryEntry entry && _criteria.Matches(entry, _clock());

        ExtensionChoices = new ObservableCollection<Choice<string?>> { AllExtensions };
        RebuildExtensionChoices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>履歴に変更があった（保存が必要）。</summary>
    public event EventHandler? HistoryChanged;

    public ListCollectionView View { get; }

    public ObservableCollection<Choice<string?>> ExtensionChoices { get; }

    public IReadOnlyList<Choice<PeriodOption>> PeriodChoices => PeriodOptions.Choices;

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
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

    public string StatusText => $"表示 {View.Count} 件 / 全 {_store.Entries.Count} 件";

    public const string NoHistoryMessage = "履歴はまだありません。\nファイルを開くと、ここに表示されます。";
    public const string NoMatchMessage = "条件に一致する履歴はありません。\n検索語や絞り込み（種類・期間・キープのみ）を変えてみてください。";

    /// <summary>一覧が空のときの案内。空でなければ null。</summary>
    public string? EmptyMessage => View.Count > 0 ? null : _store.Entries.Count == 0 ? NoHistoryMessage : NoMatchMessage;

    /// <summary>一覧で強調する検索語（正規化済み）。</summary>
    public IReadOnlyList<string> HighlightTerms => _criteria.Query.NormalizedTerms;

    /// <summary>絞り込みをやり直す。</summary>
    public void ApplyFilter()
    {
        _criteria = new FilterCriteria
        {
            Query = SearchQuery.Parse(_searchText),
            Extension = _selectedExtension.Value,
            Period = _selectedPeriod.Value,
            KeptOnly = _keptOnly,
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
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        NotifyStoreChanged();
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
