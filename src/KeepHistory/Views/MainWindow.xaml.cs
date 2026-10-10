using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.ViewModels;

namespace KeepHistory.Views;

/// <summary>
/// 履歴画面。常駐するとき（HideOnClose）は閉じる操作で隠し、常駐しないときは本当に閉じる。
/// </summary>
public partial class MainWindow : Window
{
    private const string DefaultSortColumnId = nameof(HistoryEntry.LastUsed);

    private readonly MainViewModel _vm;

    // 行のドラッグ開始の判定用（左ボタンを押した位置と行）
    private Point? _dragStart;
    private HistoryEntry? _dragEntry;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        ShowMessage = message => MessageBox.Show(this, message, "KeepHistory", MessageBoxButton.OK, MessageBoxImage.Warning);
        InitializeComponent();
        foreach (var column in HistoryGrid.Columns)
        {
            _defaultColumnIndex[column] = HistoryGrid.Columns.IndexOf(column);
            _defaultColumnWidth[column] = column.Width;
        }
        DataContext = vm;
        ResetSortIndicator();
    }

    /// <summary>
    /// true なら閉じる操作（閉じるボタン・Esc）で隠す（常駐する設定）。false なら本当に閉じる。
    /// </summary>
    public bool HideOnClose { get; set; }

    /// <summary>true のときは HideOnClose でも本当に閉じる（アプリ終了時）。</summary>
    public bool AllowClose { get; set; }

    public event EventHandler? SettingsRequested;

    /// <summary>右クリックの「記録しない」が選ばれた（除外パターンの追加を依頼）。</summary>
    public event EventHandler<ExcludeRequestEventArgs>? ExcludeRequested;

    /// <summary>「↻ 更新」が押された（強制リフレッシュ）。</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>閉じる操作で隠れた。</summary>
    public event EventHandler? HiddenByUser;

    internal MainViewModel ViewModel => _vm;

    /// <summary>画面を出す位置（設定）。</summary>
    public WindowPlacementMode Placement { get; set; } = WindowPlacementMode.MouseScreenCenter;

    /// <summary>保存されていた前回の位置（まだ一度も表示していないときに使う）。</summary>
    public Point? SavedPosition { get; set; }

    /// <summary>作業領域の取得（テストで差し替える）。</summary>
    internal Func<Rect> WorkAreaAtCursor { get; set; } = ScreenInfo.WorkAreaAtCursor;

    internal Func<IReadOnlyList<Rect>> AllWorkAreas { get; set; } = ScreenInfo.AllWorkAreas;

    /// <summary>
    /// 隠れている画面を出す前に位置を決める。「マウスのある画面の中央」なら毎回そこへ、
    /// 「前回の位置」なら前回の位置へ（モニターを外したなどで画面外なら、マウスのある画面の中央）。
    /// </summary>
    internal void PlaceBeforeShow()
    {
        var size = new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        if (Placement == WindowPlacementMode.LastPosition)
        {
            Point? last = IsLoaded ? new Point(Left, Top) : SavedPosition;
            if (last is { } p && !double.IsNaN(p.X) && !double.IsNaN(p.Y)
                && WindowPlacementCalculator.IsReachable(new Rect(p, size), AllWorkAreas()))
            {
                Left = p.X;
                Top = p.Y;
                return;
            }
        }
        var center = WindowPlacementCalculator.CenterIn(WorkAreaAtCursor(), size);
        Left = center.X;
        Top = center.Y;
    }

    /// <summary>表示して前面に出し、検索ボックスにフォーカスする。</summary>
    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            PlaceBeforeShow();
            Show();
        }
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

    /// <summary>閉じる操作。常駐するなら隠し、しないなら閉じる。</summary>
    public void CloseOrHide()
    {
        if (HideOnClose && !AllowClose)
        {
            HideToTray();
        }
        else
        {
            Close();
        }
    }

    /// <summary>
    /// 列の幅・並びとウインドウサイズを初期状態（画面定義どおり・AppSettings の既定値）に戻す。
    /// 設定画面の「すべて初期状態に戻す」を OK したときに使う。
    /// </summary>
    public void ResetLayout()
    {
        var defaults = new AppSettings();
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        Width = defaults.WindowWidth;
        Height = defaults.WindowHeight;
        var ordered = HistoryGrid.Columns.OrderBy(c => _defaultColumnIndex[c]).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].DisplayIndex = i;
            ordered[i].Width = _defaultColumnWidth[ordered[i]];
        }
    }

    // 画面定義（XAML）どおりの列の並びと幅。ResetLayout で使う
    private readonly Dictionary<DataGridColumn, int> _defaultColumnIndex = new();
    private readonly Dictionary<DataGridColumn, DataGridLength> _defaultColumnWidth = new();

    /// <summary>保存された列レイアウトとウインドウサイズを反映する。</summary>
    public void ApplyLayout(AppSettings settings)
    {
        Placement = settings.Placement;
        SavedPosition = settings.WindowLeft is { } left && settings.WindowTop is { } top ? new Point(left, top) : null;

        // 大きいモニターで保存したサイズでも、今の画面の作業領域からはみ出さない
        var work = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(settings.WindowWidth, work.Width));
        Height = Math.Max(MinHeight, Math.Min(settings.WindowHeight, work.Height));

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
        // 表示順に矛盾が無いときだけ並びを戻す。保存に無い列（後から増えた列）は、保存された列の後ろに既定の順で並べる。
        // 列を減らした後の保存（今は無い列を含む）も読めるよう、範囲は「今の列数」と「保存された列数」の大きい方で見る
        var indexLimit = Math.Max(columns.Count, settings.Columns.Count);
        if (saved.Count > 0
            && saved.Select(c => c.DisplayIndex).Distinct().Count() == saved.Count
            && saved.All(c => c.DisplayIndex >= 0 && c.DisplayIndex < indexLimit))
        {
            var ordered = saved.OrderBy(c => c.DisplayIndex).Select(c => FindColumn(c.Id)!)
                .Concat(columns.Where(c => saved.All(s => s.Id != ColumnId(c))).OrderBy(c => c.DisplayIndex))
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].DisplayIndex = i;
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
        var position = WindowState == WindowState.Normal ? new Point(Left, Top) : RestoreBounds.TopLeft;
        if (!double.IsNaN(position.X) && !double.IsInfinity(position.X) && !double.IsNaN(position.Y) && !double.IsInfinity(position.Y))
        {
            settings.WindowLeft = Math.Round(position.X);
            settings.WindowTop = Math.Round(position.Y);
        }

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
        if (HideOnClose && !AllowClose)
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

    /// <summary>ファイルを開く処理（テストで差し替える）。</summary>
    internal Func<string, Task> OpenFile { get; set; } = ShellActions.OpenAsync;

    /// <summary>ダイアログでのメッセージ表示（テストで差し替える）。</summary>
    internal Action<string> ShowMessage { get; set; }

    /// <summary>ネットワーク上のパスか（テストで差し替える）。</summary>
    internal Func<string, bool> IsNetworkPath { get; set; } = path => new FileExistenceChecker().IsNetworkPath(path);

    // 開いている途中のファイル。ネットワークが切断されていると、失敗が分かるまで数十秒かかるので、重ねて開かない
    private readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);

    private void OpenEntry(HistoryEntry? entry)
    {
        if (entry == null) return;
        ClearStatusMessage();
        if (entry.IsMissing)
        {
            // ダイアログを出すとキーボード操作が途切れるので、ステータスバーに赤字で出す
            ShowNotFound(entry.Path);
            return;
        }
        var path = entry.Path;
        var progress = OpeningMessagePrefix + path;
        if (!_opening.Add(path))
        {
            ShowProgressMessage(progress);
            return;
        }
        // ネットワーク上のファイルは開くまで時間がかかることがあるので、開いている途中だと分かるようにする
        if (IsNetworkPath(path)) ShowProgressMessage(progress);
        Task task;
        try
        {
            task = OpenFile(path);
        }
        catch (Exception ex)
        {
            task = Task.FromException(ex);
        }
        task.ContinueWith(t =>
        {
            var ex = t.Exception?.GetBaseException();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _opening.Remove(path);
                if (StatusMessageText.Text == progress) ClearStatusMessage();
                if (ex != null) ReportOpenFailure(path, ex);
            }));
        }, System.Threading.CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal const string OpeningMessagePrefix = "開いています: ";

    private void ShowNotFound(string path) => ShowStatusMessage("ファイルが見つかりません: " + path);

    private void ShowNetworkUnavailable(string path)
        => ShowStatusMessage("ネットワーク上の場所に接続できません（一時的に切断されている可能性があります）: " + path);

    /// <summary>ステータスバーに赤字でメッセージを出す。</summary>
    internal void ShowStatusMessage(string message)
    {
        StatusMessageText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        StatusMessageText.Text = message;
        StatusMessageText.Visibility = Visibility.Visible;
    }

    /// <summary>ステータスバーに、途中経過（開いています など）を通常の淡い文字で出す。</summary>
    private void ShowProgressMessage(string message)
    {
        StatusMessageText.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
        StatusMessageText.Text = message;
        StatusMessageText.Visibility = Visibility.Visible;
    }

    internal void ClearStatusMessage()
    {
        StatusMessageText.Text = string.Empty;
        StatusMessageText.Visibility = Visibility.Collapsed;
    }

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e) => ClearStatusMessage();

    /// <summary>起動時に「ファイルが無い」「パスが無い」で失敗したか。</summary>
    private static bool IsNotFound(Exception? ex)
        => ex is System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 } or FileNotFoundException or DirectoryNotFoundException;

    /// <summary>
    /// ネットワーク上の場所に届かなかったか（サーバーや共有が見つからない・切断された・応答がない）。
    /// 51 リモートのコンピューターが使えない／53 ネットワークパスが見つからない／59 予期しないネットワークエラー／
    /// 64 ネットワーク名が使えなくなった／67 ネットワーク名が見つからない／121 タイムアウト／1203・1222 ネットワークが無い／
    /// 1231・1232 ネットワークの場所に届かない
    /// </summary>
    internal static bool IsNetworkUnavailable(Exception? ex)
        => ex is System.ComponentModel.Win32Exception { NativeErrorCode: 51 or 53 or 59 or 64 or 67 or 121 or 1203 or 1222 or 1231 or 1232 };

    private void OpenFolder(HistoryEntry? entry)
    {
        if (entry == null) return;
        var task = entry.IsMissing ? ShellActions.OpenFolderAsync(entry.FolderPath) : ShellActions.RevealInExplorerAsync(entry.Path);
        RunShellAction(task, entry.Path);
    }

    private void RunShellAction(Task task, string path)
    {
        // 失敗したら画面のスレッドに戻って知らせる（Dispatcher を直接使い、実行環境の同期コンテキストに頼らない）
        task.ContinueWith(t =>
        {
            var ex = t.Exception?.GetBaseException();
            Dispatcher.BeginInvoke(new Action(() => ReportOpenFailure(path, ex)));
        }, System.Threading.CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void ReportOpenFailure(string path, Exception? ex)
    {
        ErrorLog.Write($"開けませんでした: {path}", ex);
        if (IsNotFound(ex))
        {
            // 開く直前に消された・移動されたなど。キーボード操作を止めないようステータスバーに出す
            ShowNotFound(path);
            return;
        }
        if (IsNetworkUnavailable(ex))
        {
            // ネットワークの一時的な切断など。見つからないときと同じく、キーボード操作を止めないようステータスバーに出す
            ShowNetworkUnavailable(path);
            return;
        }
        ShowMessage($"開けませんでした。\n\n{path}\n\n{ex?.Message}");
    }

    /// <summary>コピーする文字列（複数なら 1 行に 1 件）。</summary>
    internal static string BuildCopyText(IEnumerable<HistoryEntry> entries, Func<HistoryEntry, string> selector)
        => string.Join(Environment.NewLine, entries.Select(selector));

    /// <summary>クリップボードへの書き込み（テストで差し替える）。</summary>
    internal Action<string> SetClipboardText { get; set; } = Clipboard.SetText;

    private void CopyToClipboard(IReadOnlyList<HistoryEntry> entries, Func<HistoryEntry, string> selector)
    {
        if (entries.Count == 0) return;
        try
        {
            SetClipboardText(BuildCopyText(entries, selector));
        }
        catch (ExternalException ex)
        {
            ErrorLog.Write("クリップボードにコピーできませんでした。", ex);
        }
    }

    /// <summary>クリップボードへのデータの書き込み（テストで差し替える）。</summary>
    internal Action<DataObject> SetClipboardData { get; set; } = data => Clipboard.SetDataObject(data, true);

    /// <summary>
    /// ファイル自体をコピーするデータ。エクスプローラーやメールに貼り付けるとファイルがコピーされる
    /// （FileDrop と、貼り付け時に移動ではなくコピーにする Preferred DropEffect）。
    /// </summary>
    internal static DataObject CreateFileCopyData(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
        return data;
    }

    private void CopyFile(HistoryEntry? entry)
    {
        if (entry == null) return;
        ClearStatusMessage();
        if (entry.IsMissing)
        {
            ShowNotFound(entry.Path);
            return;
        }
        try
        {
            SetClipboardData(CreateFileCopyData(entry.Path));
        }
        catch (ExternalException ex)
        {
            ErrorLog.Write("クリップボードにコピーできませんでした。", ex);
        }
    }

    /// <summary>「プログラムから開く」画面の表示（テストで差し替える）。失敗したら false。</summary>
    internal Func<IntPtr, string, bool> ShowOpenWith { get; set; } = ShellActions.ShowOpenWithDialog;

    private void OpenWith(HistoryEntry? entry)
    {
        if (entry == null) return;
        ClearStatusMessage();
        if (entry.IsMissing)
        {
            ShowNotFound(entry.Path);
            return;
        }
        var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!ShowOpenWith(owner, entry.Path))
        {
            ErrorLog.Write($"「プログラムから開く」を表示できませんでした: {entry.Path}");
            ShowMessage($"「プログラムから開く」を表示できませんでした。\n\n{entry.Path}");
        }
    }

    /// <summary>右クリックメニューを開いたとき、選んでいる行に合わせて「この拡張子を記録しない」を作る。</summary>
    internal void UpdateExcludeMenu(HistoryEntry? entry)
    {
        var extension = entry?.Extension ?? string.Empty;
        ExcludeExtensionMenuItem.Header = $"この拡張子（{extension}）を記録しない";
        ExcludeExtensionMenuItem.Visibility = extension.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RequestExclude(Func<HistoryEntry, (string Pattern, string Description)> make)
    {
        if (HistoryGrid.SelectedItem is not HistoryEntry entry) return;
        var (pattern, description) = make(entry);
        ExcludeRequested?.Invoke(this, new ExcludeRequestEventArgs(pattern, description));
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e) => UpdateExcludeMenu(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnExcludeFileClick(object sender, RoutedEventArgs e)
        => RequestExclude(entry => (ExclusionPatterns.ForFile(entry.Path), $"ファイル「{entry.FileName}」"));

    private void OnExcludeFolderClick(object sender, RoutedEventArgs e)
        => RequestExclude(entry => (ExclusionPatterns.ForFolder(entry.FolderPath), $"フォルダ「{entry.FolderPath}」の中のファイル（サブフォルダを含む）"));

    private void OnExcludeExtensionClick(object sender, RoutedEventArgs e)
        => RequestExclude(entry => (ExclusionPatterns.ForExtension(entry.Extension), $"拡張子「{entry.Extension}」のファイル"));

    private void OnCopyFileClick(object sender, RoutedEventArgs e) => CopyFile(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnOpenWithClick(object sender, RoutedEventArgs e) => OpenWith(HistoryGrid.SelectedItem as HistoryEntry);

    private void CopyPaths(IReadOnlyList<HistoryEntry> entries) => CopyToClipboard(entries, e => e.Path);

    private void CopyFileNames(IReadOnlyList<HistoryEntry> entries) => CopyToClipboard(entries, e => e.FileName);

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

    /// <summary>
    /// キープを切り替える。一覧の作り直し（並び替え・絞り込みのやり直し）で行が作り直され、
    /// キーボードフォーカスが一覧から外れてしまうので、同じ行の同じセルにフォーカスを戻す
    /// （Ctrl+K を続けて押すとトグルになるように。選択そのものは一覧が保つ）。
    /// 「キープのみ」で絞り込んでいて行が一覧から消えた場合は何もしない。
    /// </summary>
    private void ToggleKeptKeepingSelection(IReadOnlyList<HistoryEntry> entries)
    {
        var selected = HistoryGrid.SelectedItem as HistoryEntry;
        var column = HistoryGrid.CurrentColumn;
        var hadFocus = HistoryGrid.IsKeyboardFocusWithin;

        _vm.ToggleKept(entries);

        if (selected == null || !HistoryGrid.Items.Contains(selected)) return;
        column ??= HistoryGrid.Columns.OrderBy(c => c.DisplayIndex).FirstOrDefault();
        if (column != null) HistoryGrid.CurrentCell = new DataGridCellInfo(selected, column);
        if (hadFocus) FocusCell(selected, column);
    }

    /// <summary>指定した行・列のセルにキーボードフォーカスを置く。</summary>
    private void FocusCell(HistoryEntry entry, DataGridColumn? column)
    {
        HistoryGrid.ScrollIntoView(entry);
        HistoryGrid.UpdateLayout();
        if (HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry) is not DataGridRow row) return;
        if (column?.GetCellContent(row)?.Parent is DataGridCell cell)
        {
            cell.Focus();
        }
        else
        {
            row.Focus();
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
        if ((e.Key == Key.F || e.Key == Key.L) && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            FocusSearchBox();
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            // 「↻ 更新」と同じ（強制リフレッシュ）
            e.Handled = true;
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Esc で画面を閉じる（常駐するなら隠す）。プルダウンやメニューが開いていれば、そちらが Esc を使う（処理済みになる）ので、
    /// ここには届かない。先に受ける Preview で処理すると、プルダウンを閉じるつもりの Esc で画面ごと閉じてしまう。
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseOrHide();
    }

    /// <summary>検索欄の「×」。検索語を消して、続けて入力できるよう検索欄にフォーカスを置く。</summary>
    private void OnClearSearchClick(object sender, RoutedEventArgs e) => ClearSearch();

    internal void ClearSearch()
    {
        SearchBox.Clear();
        SearchBox.Focus();
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
        if (HandleGridKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>一覧でのキー操作。処理したら true（テストから直接呼ぶ）。</summary>
    internal bool HandleGridKey(Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers == ModifierKeys.Control;
        if (key == Key.Enter)
        {
            OpenEntry(HistoryGrid.SelectedItem as HistoryEntry);
        }
        else if (key == Key.Delete)
        {
            DeleteSelected();
        }
        else if (key == Key.C && ctrl)
        {
            CopyPaths(SelectedEntries());
        }
        else if (key == Key.K && ctrl)
        {
            ToggleKeptKeepingSelection(SelectedEntries());
        }
        else
        {
            return false;
        }
        return true;
    }

    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 見出しのダブルクリックは無視する
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: HistoryEntry entry }) OpenEntry(entry);
    }

    /// <summary>ドラッグの開始・受け渡し（テストで差し替える）。</summary>
    internal Func<DependencyObject, object, DragDropEffects, DragDropEffects> StartDragDrop { get; set; } = DragDrop.DoDragDrop;

    /// <summary>
    /// 行を画面の外へドラッグして、ほかのアプリやエクスプローラーにファイルを渡すためのデータ。
    /// エクスプローラーと同じ FileDrop 形式にする。見つからないファイルは渡さない（null）。
    /// </summary>
    internal static DataObject? CreateDragData(HistoryEntry entry)
    {
        if (entry.IsMissing) return null;
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { entry.Path });
        return data;
    }

    /// <summary>ドラッグとみなす移動量か（システム設定のしきい値を超えたか）。</summary>
    internal static bool IsDragGesture(Point start, Point current)
        => Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
           || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>
    /// マウスを押した要素から、ドラッグできる行の履歴を探す。
    /// ★ボタン・見出し・スクロールバーの上からはドラッグしない。
    /// </summary>
    internal static HistoryEntry? FindDraggableEntry(DependencyObject? source)
    {
        for (var current = source; current != null; current = ParentOf(current))
        {
            switch (current)
            {
                case ButtonBase: // ★ボタン。列の見出し（DataGridColumnHeader）も ButtonBase の派生
                case ScrollBar:
                    return null;
                case DataGridRow row:
                    return row.Item as HistoryEntry;
            }
        }
        return null;
    }

    /// <summary>
    /// 行のドラッグを始める。元のファイルを移動されないよう、許可するのはコピーとリンクだけ。
    /// 始めたら true。
    /// </summary>
    internal bool TryStartDrag(HistoryEntry entry)
    {
        var data = CreateDragData(entry);
        if (data == null) return false;
        StartDragDrop(HistoryGrid, data, DragDropEffects.Copy | DragDropEffects.Link);
        return true;
    }

    private void OnGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragEntry = FindDraggableEntry(e.OriginalSource as DependencyObject);
        _dragStart = _dragEntry != null ? e.GetPosition(HistoryGrid) : null;
    }

    private void OnGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart == null || _dragEntry == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDrag();
            return;
        }
        if (!IsDragGesture(_dragStart.Value, e.GetPosition(HistoryGrid))) return;
        var entry = _dragEntry;
        ResetDrag();
        TryStartDrag(entry);
    }

    private void OnGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ResetDrag();

    private void ResetDrag()
    {
        _dragStart = null;
        _dragEntry = null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current != null; current = ParentOf(current))
        {
            if (current is T match) return match;
        }
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject current)
        => current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private void OnKeepButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryEntry entry)
        {
            ToggleKeptKeepingSelection(new[] { entry });
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>モーダル画面の表示（テストで差し替える）。</summary>
    internal Func<Window, bool?> ShowDialogWindow { get; set; } = window => window.ShowDialog();

    private void OnThirdPartyLicensesClick(object sender, RoutedEventArgs e)
        => ShowDialogWindow(new LicenseWindow { Owner = this });

    private void OnAboutClick(object sender, RoutedEventArgs e)
        => ShowDialogWindow(new AboutWindow { Owner = this });

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenEntry(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolder(HistoryGrid.SelectedItem as HistoryEntry);

    private void OnCopyPathClick(object sender, RoutedEventArgs e) => CopyPaths(SelectedEntries());

    private void OnCopyFileNameClick(object sender, RoutedEventArgs e) => CopyFileNames(SelectedEntries());

    private void OnToggleKeepClick(object sender, RoutedEventArgs e) => ToggleKeptKeepingSelection(SelectedEntries());

    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteSelected();
}
