using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KeepHistory.Models;
using KeepHistory.Services;

namespace KeepHistory.Views;

/// <summary>設定画面から開く「削除した履歴を元に戻す」画面。元に戻すとすぐに履歴へ反映する。</summary>
public partial class DeletedHistoryWindow : Window
{
    private readonly HistoryStore _store;
    private readonly ObservableCollection<DeletedHistory> _items;

    public DeletedHistoryWindow(HistoryStore store)
    {
        _store = store;
        InitializeComponent();
        _items = new ObservableCollection<DeletedHistory>(store.Deleted.Values.OrderByDescending(d => d.DeletedAt));
        DeletedGrid.ItemsSource = _items;
        UpdateState();
    }

    /// <summary>1 件でも元に戻したか（閉じた後に履歴を保存・再表示するため）。</summary>
    public bool RestoredAny { get; private set; }

    /// <summary>選んだ履歴を元に戻す。戻した件数（記憶だけ消したものを含む）を返す。</summary>
    internal int RestoreSelected()
    {
        var selected = DeletedGrid.SelectedItems.OfType<DeletedHistory>().ToList();
        if (selected.Count == 0) return 0;
        var shown = _store.Restore(selected.Select(d => d.Path));
        foreach (var item in selected) _items.Remove(item);
        RestoredAny = true;

        var pending = selected.Count - shown;
        ResultText.Text = pending == 0
            ? $"{selected.Count} 件を元に戻しました。"
            : $"{selected.Count} 件を元に戻しました（うち {pending} 件は、最近使った項目に残っていれば次の読み込みで一覧に戻ります）。";
        UpdateState();
        return selected.Count;
    }

    private void UpdateState()
    {
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = DeletedGrid.SelectedItems.Count > 0;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreSelected();
}
