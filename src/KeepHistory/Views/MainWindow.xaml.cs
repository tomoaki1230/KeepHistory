using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.ViewModels;

namespace KeepHistory.Views;

/// <summary>履歴画面。閉じるボタンでは終了せず、隠して通知領域に常駐する。</summary>
public partial class MainWindow : Window
{
    private const string DefaultSortColumnId = nameof(HistoryEntry.LastUsed);

    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        ResetSortIndicator();
    }

    /// <summary>true のときだけ本当に閉じる（アプリ終了時）。</summary>
    public bool AllowClose { get; set; }

    public event EventHandler? SettingsRequested;

    /// <summary>閉じる操作で隠れた。</summary>
    public event EventHandler? HiddenByUser;

    internal MainViewModel ViewModel => _vm;

    /// <summary>表示して前面に出し、検索ボックスにフォーカスする。</summary>
    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        FocusSearchBox();
    }

    public void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void HideToTray()
    {
        Hide();
        HiddenByUser?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>保存された列レイアウトとウインドウサイズを反映する。</summary>
    public void ApplyLayout(AppSettings settings)
    {
        Width = Math.Max(MinWidth, settings.WindowWidth);
        Height = Math.Max(MinHeight, settings.WindowHeight);

        var columns = HistoryGrid.Columns;
        var saved = settings.Columns
            .Where(c => FindColumn(c.Id) != null)
            .GroupBy(c => c.Id).Select(g => g.First())
            .ToList();
        foreach (var layout in saved)
        {
            var column = FindColumn(layout.Id)!;
            if (column.CanUserResize && layout.Width >= 20 && layout.Width <= 5000)
            {
                column.Width = new DataGridLength(layout.Width);
            }
        }
        // 全列がそろっていて表示順に矛盾が無いときだけ並びを戻す
        if (saved.Count == columns.Count
            && saved.Select(c => c.DisplayIndex).Distinct().Count() == columns.Count
            && saved.All(c => c.DisplayIndex >= 0 && c.DisplayIndex < columns.Count))
        {
            foreach (var layout in saved.OrderBy(c => c.DisplayIndex))
            {
                FindColumn(layout.Id)!.DisplayIndex = layout.DisplayIndex;
            }
        }
    }

    /// <summary>現在の列レイアウトとウインドウサイズを設定に書き出す。</summary>
    public void CaptureLayout(AppSettings settings)
    {
        // 一度も表示していなければ実寸が無いので、保存済みの値をそのまま残す
        if (!IsLoaded) return;
        var bounds = WindowState == WindowState.Normal ? new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height) : RestoreBounds.Size;
        if (!double.IsInfinity(bounds.Width) && bounds.Width > 0) settings.WindowWidth = Math.Round(bounds.Width);
        if (!double.IsInfinity(bounds.Height) && bounds.Height > 0) settings.WindowHeight = Math.Round(bounds.Height);

        settings.Columns = HistoryGrid.Columns.Select(c => new ColumnLayout
        {
            Id = ColumnId(c),
            Width = Math.Round(c.ActualWidth > 0 ? c.ActualWidth : c.Width.DisplayValue),
            DisplayIndex = c.DisplayIndex,
        }).ToList();
    }

    internal static string ColumnId(DataGridColumn column) => column.SortMemberPath;

    internal DataGridColumn? FindColumn(string id)
        => HistoryGrid.Columns.FirstOrDefault(c => ColumnId(c) == id);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HideToTray();
        }
        base.OnClosing(e);
    }

    private void ResetSortIndicator()
    {
        foreach (var column in HistoryGrid.Columns) column.SortDirection = null;
        var sortColumn = FindColumn(DefaultSortColumnId);
        if (sortColumn != null) sortColumn.SortDirection = ListSortDirection.Descending;
    }

    private IReadOnlyList<HistoryEntry> SelectedEntries()
        => HistoryGrid.SelectedItems.OfType<HistoryEntry>().ToList();

    private HistoryEntry? CurrentEntry()
        => HistoryGrid.SelectedItem as HistoryEntry
           ?? (HistoryGrid.Items.Count > 0 ? HistoryGrid.Items[0] as HistoryEntry : null);

    private void OpenEntry(HistoryEntry? entry)
    {
        if (entry == null) return;
        if (entry.IsMissing)
        {
            MessageBox.Show(this, $"ファイルが見つかりません。\n\n{entry.Path}", "KeepHistory",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RunShellAction(ShellActions.OpenAsync(entry.Path), entry.Path);
    }

    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry == null) return;
        var task = entry.IsMissing ? ShellActions.OpenFolderAsync(entry.FolderPath) : ShellActions.RevealInExplorerAsync(entry.Path);
        RunShellAction(task, entry.Path);
    }

    private void RunShellAction(Task task, string path)
    {
        task.ContinueWith(t =>
        {
            var ex = t.Exception?.GetBaseException();
            ErrorLog.Write($"開けませんでした: {path}", ex);
            MessageBox.Show(this, $"開けませんでした。\n\n{path}\n\n{ex?.Message}", "KeepHistory",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }, System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void CopyPaths(IReadOnlyList<HistoryEntry> entries)
    {
        if (entries.Count == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, entries.Select(e => e.Path)));
        }
        catch (COMException ex)
        {
            ErrorLog.Write("クリップボードにコピーできませんでした。", ex);
        }
    }

    private void DeleteSelected()
    {
        var entries = SelectedEntries();
        if (entries.Count == 0) return;
        int index = HistoryGrid.SelectedIndex;
        _vm.Delete(entries);
        // 消した位置の近くを選び直す
        if (HistoryGrid.Items.Count > 0)
        {
            HistoryGrid.SelectedIndex = Math.Clamp(index, 0, HistoryGrid.Items.Count - 1);
            FocusSelectedRow();
        }
    }

    private void FocusSelectedRow()
    {
        if (HistoryGrid.SelectedItem == null) return;
        HistoryGrid.ScrollIntoView(HistoryGrid.SelectedItem);
        HistoryGrid.UpdateLayout();
        if (HistoryGrid.ItemContainerGenerator.ContainerFromItem(HistoryGrid.SelectedItem) is DataGridRow row)
        {
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideToTray();
        }
        else if ((e.Key == Key.F || e.Key == Key.L) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            FocusSearchBox();
        }
    }

    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && HistoryGrid.Items.Count > 0)
        {
            e.Handled = true;
            if (HistoryGrid.SelectedIndex < 0) HistoryGrid.SelectedIndex = 0;
            FocusSelectedRow();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenEntry(CurrentEntry());
        }
    }

    private void OnGridPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenEntry(HistoryGrid.SelectedItem as HistoryEntry);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteSelected();
        }
        else if (e.Key == Key.C && ctrl)
        {
            e.Handled = true;
            CopyPaths(SelectedEntries());
        }
        else if (e.Key == Key.K && ctrl)
        {
            e.Handled = true;
            _vm.ToggleKept(SelectedEntries());
        }
    }

    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 見出しのダブルクリックは無視する
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridRow)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (source is DataGridRow row && row.Item is HistoryEntry entry) OpenEntry(entry);
    }

    private void OnKeepButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryEntry entry)
        {
            _vm.ToggleKept(new[] { entry });
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenEntry(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolder(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnCopyPathClick(object sender, RoutedEventArgs e) => CopyPaths(SelectedEntries());

    private void OnToggleKeepClick(object sender, RoutedEventArgs e) => _vm.ToggleKept(SelectedEntries());

    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteSelected();
}
