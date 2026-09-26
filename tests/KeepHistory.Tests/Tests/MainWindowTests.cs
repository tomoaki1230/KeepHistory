using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

/// <summary>画面テスト。製品の App は生成せず、素の Application に共有リソースだけ読ませる。</summary>
public sealed class MainWindowTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0);

    private readonly HistoryStore _store = new();
    private readonly MainViewModel _vm;
    private readonly MainWindow _window;
    private bool _closed;

    public MainWindowTests()
    {
        UiTestHost.EnsureApplication();
        _store.Register(@"C:\Docs\report-2026.docx", Now.AddHours(-3), Now);
        _store.Register(@"C:\Docs\budget.xlsx", Now.AddHours(-1), Now);
        _store.Register(@"C:\Old\gone.txt", Now.AddHours(-2), Now);
        _vm = new MainViewModel(_store, () => Now);
        _window = new MainWindow(_vm) { ShowActivated = true };
        _window.Closed += (_, _) => _closed = true;
    }

    public void Dispose()
    {
        if (!_closed)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        UiTestHost.DoEvents();
    }

    private void ShowWindow()
    {
        _window.ShowAndActivate();
        UiTestHost.DoEvents();
    }

    [Test]
    public void TestHost_DoesNotCreateProductApp()
    {
        Assert.Equal(typeof(Application), Application.Current.GetType(), "App.OnStartup の多重起動ガードを動かさない");
        Assert.NotNull(Application.Current.TryFindResource("MissingTextBrush"), "共有リソースは読めている");
    }

    [Test]
    public void SearchBox_HasInitialFocus()
    {
        ShowWindow();
        Assert.True(_window.SearchBox.IsFocused, "表示したら検索ボックスにフォーカス");
    }

    [Test]
    public void SearchBox_GetsFocusBackWhenShownAgain()
    {
        ShowWindow();
        _window.HistoryGrid.Focus();
        _window.HideToTray();
        UiTestHost.DoEvents();
        ShowWindow();
        Assert.True(_window.SearchBox.IsFocused, "呼び出し直したら検索ボックスに戻る");
    }

    [Test]
    public void MissingFile_IsShownFadedAndStaysInList()
    {
        _store.Find(@"C:\Old\gone.txt")!.IsMissing = true;
        ShowWindow();
        var grid = _window.HistoryGrid;
        Assert.Equal(3, grid.Items.Count, "見つからないファイルも一覧に残す");

        var missing = _store.Find(@"C:\Old\gone.txt")!;
        var missingRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(missing);
        var normalRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(_store.Find(@"C:\Docs\budget.xlsx")!);
        var faded = Application.Current.FindResource("MissingTextBrush");
        Assert.Same(faded, Assert.NotNull(missingRow).Foreground, "見つからないファイルは淡色");
        Assert.NotEqual(faded, Assert.NotNull(normalRow).Foreground);
    }

    private DataGridColumnHeader HeaderOf(string columnId)
    {
        var column = _window.FindColumn(columnId)!;
        return Assert.NotNull(FindVisual<DataGridColumnHeader>(_window, h => h.Column == column), columnId + " の見出し");
    }

    [Test]
    public void KeepColumnHeader_IsTextCenteredAndFullyVisible()
    {
        ShowWindow();
        var header = HeaderOf(nameof(HistoryEntry.IsKept));
        Assert.Equal("キープ", header.Column.Header, "見出しは★ではなく「キープ」");
        Assert.Null(FindVisual<TextBlock>(header, t => t.Text == "★"), "見出しに★は出さない");
        var text = Assert.NotNull(FindVisual<TextBlock>(header, t => t.Text == "キープ"));

        var left = text.TranslatePoint(new Point(0, 0), header).X;
        var right = left + text.ActualWidth;
        Assert.True(Math.Abs((left + right) / 2 - header.ActualWidth / 2) < 2.0,
            $"中央揃え（文字 {left:0.0}～{right:0.0} / 見出し幅 {header.ActualWidth:0.0}）");
        // 切り詰められた後の幅ではなく、文字本来の幅と、見出しの余白を除いた幅を比べる
        var natural = new FormattedText(text.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
        var usable = header.ActualWidth - header.Padding.Left - header.Padding.Right;
        Assert.True(usable >= natural && text.ActualWidth >= natural - 0.5,
            $"「キープ」が切れずに収まる（文字本来の幅 {natural:0.0} / 表示幅 {text.ActualWidth:0.0} / 見出しの使える幅 {usable:0.0}）");

        var other = Assert.NotNull(FindVisual<TextBlock>(HeaderOf("FileName"), t => t.Text == "ファイル名"));
        Assert.Equal(other.Foreground, text.Foreground, "文字の色はほかの見出しと同じ");
        Assert.Equal(other.FontSize, text.FontSize, "文字の大きさはほかの見出しと同じ");
    }

    [Test]
    public void KeptOnlyToggle_StarIsYellowWhenOnAndGrayOutlineWhenOff()
    {
        ShowWindow();
        var star = _window.KeptOnlyStar;
        Assert.False(_vm.KeptOnly);
        Assert.Equal("☆", star.Text, "オフは☆");
        Assert.Same(Application.Current.FindResource("StarOffBrush"), star.Foreground, "オフは灰色");

        _window.KeptOnlyToggle.IsChecked = true;
        UiTestHost.DoEvents();
        Assert.True(_vm.KeptOnly, "ボタンで絞り込みがオンになる");
        Assert.Equal("★", star.Text, "オンは★");
        Assert.Same(Application.Current.FindResource("StarOnBrush"), star.Foreground, "オンは黄色");

        _vm.KeptOnly = false;
        UiTestHost.DoEvents();
        Assert.Equal("☆", star.Text, "オフに戻すと☆");
        Assert.NotNull(FindVisual<TextBlock>(_window.KeptOnlyToggle, t => t.Text == "キープのみ"));
    }

    [Test]
    public void SelectedCell_HasNoBorder()
    {
        ShowWindow();
        var grid = _window.HistoryGrid;
        var entry = (HistoryEntry)grid.Items[0];
        grid.SelectedItem = entry;
        grid.CurrentCell = new DataGridCellInfo(entry, _window.FindColumn("FileName")!);
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(entry);
        var cell = Assert.NotNull(FindVisual<DataGridCell>(row, c => c.Column == _window.FindColumn("FileName")));
        cell.Focus();
        UiTestHost.DoEvents();

        Assert.True(cell.IsKeyboardFocusWithin || cell.IsFocused, "セルにフォーカスがある状態で確認する");
        Assert.Equal(new Thickness(0), cell.BorderThickness, "選んだセルに枠を付けない");
        Assert.Null(cell.FocusVisualStyle, "フォーカスの点線枠も付けない");
    }

    [Test]
    public void ContextMenu_HasCopyFileName()
    {
        var headers = _window.HistoryGrid.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        var pathIndex = headers.IndexOf("パスをコピー");
        Assert.Equal(pathIndex + 1, headers.IndexOf("ファイル名をコピー"), "パスをコピーの次に並ぶ");

        // 選んだ行のファイル名だけをコピーする
        ShowWindow();
        string? copied = null;
        _window.SetClipboardText = text => copied = text;
        _window.HistoryGrid.SelectedItem = _store.Find(@"C:\Docs\report-2026.docx")!;
        _window.CopyFileNameMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("report-2026.docx", copied);
    }

    [Test]
    public void Grid_AllowsOnlySingleSelection()
    {
        ShowWindow();
        var grid = _window.HistoryGrid;
        Assert.Equal(DataGridSelectionMode.Single, grid.SelectionMode, "複数行は選べない");
        grid.SelectedItem = grid.Items[0];
        grid.SelectedItem = grid.Items[1];
        Assert.Equal(1, grid.SelectedItems.Count);
        Assert.Same(grid.Items[1], grid.SelectedItem);
    }

    /// <summary>行の中の要素（マウスを押した位置の代わり）を探す。</summary>
    private T RowElement<T>(HistoryEntry entry, Func<T, bool> predicate) where T : DependencyObject
    {
        var row = (DataGridRow)_window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry);
        return Assert.NotNull(FindVisual(Assert.NotNull(row), predicate));
    }

    [Test]
    public void DragRow_PassesFileAsFileDrop_CopyOrLinkOnly()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        object? passedData = null;
        DragDropEffects? allowed = null;
        _window.StartDragDrop = (source, data, effects) =>
        {
            passedData = data;
            allowed = effects;
            return DragDropEffects.Copy;
        };

        // ファイル名のセルを押した想定
        var cellText = RowElement<TextBlock>(entry, t => t.Text == "budget.xlsx");
        var found = Assert.NotNull(MainWindow.FindDraggableEntry(cellText), "行の上からはドラッグできる");
        Assert.True(_window.TryStartDrag(found));

        var data = Assert.NotNull(passedData as IDataObject);
        Assert.SequenceEqual(new[] { @"C:\Docs\budget.xlsx" }, (string[])data.GetData(DataFormats.FileDrop), "エクスプローラーと同じ FileDrop 形式");
        Assert.Equal(DragDropEffects.Copy | DragDropEffects.Link, allowed, "元のファイルを移動させない");
    }

    [Test]
    public void DragRow_NotFromStarButtonOrHeader_AndNotForMissingFile()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        var star = RowElement<Button>(entry, _ => true);
        Assert.Null(MainWindow.FindDraggableEntry(star), "★ボタンからはドラッグしない（クリックでキープ）");
        var header = Assert.NotNull(FindVisual<DataGridColumnHeader>(_window, h => h.Column != null));
        Assert.Null(MainWindow.FindDraggableEntry(header), "見出しからはドラッグしない（列の入れ替え・並び替え）");

        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        var started = false;
        _window.StartDragDrop = (_, _, _) =>
        {
            started = true;
            return DragDropEffects.None;
        };
        Assert.False(_window.TryStartDrag(missing), "見つからないファイルは渡さない");
        Assert.False(started);
    }

    [Test]
    public void DragGesture_UsesSystemThreshold()
    {
        var start = new Point(100, 100);
        Assert.False(MainWindow.IsDragGesture(start, new Point(101, 101)), "少し動いただけではドラッグにしない（クリック扱い）");
        Assert.True(MainWindow.IsDragGesture(start, new Point(100 + SystemParameters.MinimumHorizontalDragDistance, 100)));
        Assert.True(MainWindow.IsDragGesture(start, new Point(100, 100 - SystemParameters.MinimumVerticalDragDistance)));
    }

    [Test]
    public void Stars_HaveSameSizeInRowAndKeptOnlyButton()
    {
        _vm.ToggleKept(new[] { _store.Find(@"C:\Docs\budget.xlsx")! });
        ShowWindow();
        var rowStar = Assert.NotNull(FindVisual<TextBlock>(_window.HistoryGrid, t => t.Text == "★" && FindAncestorOf<DataGridRow>(t) != null), "行の★");
        var rowOutline = Assert.NotNull(FindVisual<TextBlock>(_window.HistoryGrid, t => t.Text == "☆" && FindAncestorOf<DataGridRow>(t) != null), "行の☆");
        var buttonStar = _window.KeptOnlyStar;
        var buttonOff = buttonStar.ActualHeight;
        _window.KeptOnlyToggle.IsChecked = true;
        UiTestHost.DoEvents();

        var expected = (double)Application.Current.FindResource("StarFontSize");
        Assert.Equal(expected, rowStar.FontSize, "行の★");
        Assert.Equal(expected, rowOutline.FontSize, "行の☆");
        Assert.Equal(expected, buttonStar.FontSize, "キープのみボタンの星");
        Assert.Equal(rowStar.FontFamily, buttonStar.FontFamily);
        Assert.True(Math.Abs(rowStar.ActualHeight - buttonStar.ActualHeight) < 0.5 && Math.Abs(buttonOff - buttonStar.ActualHeight) < 0.5,
            $"表示上の大きさもそろう（行 {rowStar.ActualHeight:0.0} / ボタン オン {buttonStar.ActualHeight:0.0}・オフ {buttonOff:0.0}）");
    }

    private Window? ClickMenuAndCaptureDialog(MenuItem item)
    {
        Window? shown = null;
        _window.ShowDialogWindow = w =>
        {
            shown = w;
            return true;
        };
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return shown;
    }

    [Test]
    public void MenuBar_HasToolsAndHelp_WithAccessKeys()
    {
        ShowWindow();
        var top = _window.MainMenu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.SequenceEqual(new[] { "ツール(_T)", "ヘルプ(_H)" }, top, "メニューバーはツールとヘルプ");
        Assert.SequenceEqual(new[] { "設定(_O)..." }, _window.ToolsMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.SequenceEqual(new[] { "サードパーティーのライセンス(_L)", "バージョン情報(_A)" },
            _window.HelpMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.True(_window.MainMenu.TranslatePoint(new Point(0, 0), _window.SearchBox).Y < 0, "メニューバーは検索行より上");
    }

    [Test]
    public void SearchRow_NoLongerHasSettingsOrHelpButtons()
    {
        ShowWindow();
        var searchRow = Assert.NotNull(FindAncestorOf<DockPanel>(_window.KeptOnlyToggle));
        var buttons = searchRow.Children.OfType<Button>().Select(b => b.Content as string).ToList();
        Assert.Equal(0, buttons.Count, $"検索行に設定・ヘルプのボタンを置かない（{string.Join(", ", buttons)}）");
    }

    [Test]
    public void ToolsMenu_Settings_RaisesSettingsRequested()
    {
        ShowWindow();
        var requested = 0;
        _window.SettingsRequested += (_, _) => requested++;
        _window.SettingsMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(1, requested, "ツール ＞ 設定 で設定画面を開く");
    }

    [Test]
    public void HelpMenu_ThirdPartyLicenses_OpensLicenseWindow()
    {
        ShowWindow();
        var license = Assert.NotNull(ClickMenuAndCaptureDialog(_window.ThirdPartyLicensesMenuItem) as LicenseWindow, "ライセンス画面を開く");
        try
        {
            Assert.Same(_window, license.Owner, "履歴画面の上に出す");
            Assert.Contains("MIT License", license.NoticeText.Text);
            Assert.True(license.NoticeText.IsReadOnly, "書き換えられない");
        }
        finally
        {
            license.Close();
        }
    }

    [Test]
    public void HelpMenu_About_ShowsVersion()
    {
        ShowWindow();
        var about = Assert.NotNull(ClickMenuAndCaptureDialog(_window.AboutMenuItem) as AboutWindow, "バージョン情報を開く");
        try
        {
            Assert.Same(_window, about.Owner, "履歴画面の上に出す");
            var version = typeof(MainWindow).Assembly.GetName().Version!;
            Assert.Equal("バージョン " + version.ToString(3), about.VersionText.Text, "アプリのバージョン（csproj の Version）");
            Assert.Equal("1.0.0", AboutWindow.AppVersion);
            Assert.Equal("KeepHistory", about.ProductText.Text);
            Assert.Equal("作成者: Tomoaki Bessho", about.AuthorText.Text, "作成者を表示");
            Assert.Equal("Tomoaki Bessho", AboutWindow.Author, "csproj の作成者から読む");
            Assert.Contains(".NET 8", about.RuntimeText.Text);
            Assert.NotNull(about.AppImage.Source, "アイコンを表示");
        }
        finally
        {
            about.Close();
        }
    }

    private static T? FindAncestorOf<T>(DependencyObject? current) where T : DependencyObject
    {
        for (; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }
        return null;
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            var found = FindVisual(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>行を選び、そのセルにキーボードフォーカスを置く（一覧を操作している状態）。</summary>
    private void SelectAndFocusRow(HistoryEntry entry)
    {
        var grid = _window.HistoryGrid;
        grid.SelectedItem = entry;
        grid.CurrentCell = new DataGridCellInfo(entry, _window.FindColumn("FileName")!);
        grid.ScrollIntoView(entry);
        grid.UpdateLayout();
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(entry);
        var cell = Assert.NotNull(FindVisual<DataGridCell>(Assert.NotNull(row), c => c.Column == _window.FindColumn("FileName")));
        Keyboard.Focus(cell);
        UiTestHost.DoEvents();
        Assert.True(grid.IsKeyboardFocusWithin, "前提: 一覧にキーボードフォーカスがある");
    }

    [Test]
    public void CtrlK_KeepsSelection_SoRepeatedPressToggles()
    {
        ShowWindow();
        var grid = _window.HistoryGrid;
        var entry = _store.Find(@"C:\Old\gone.txt")!;
        SelectAndFocusRow(entry);

        Assert.True(_window.HandleGridKey(Key.K, ModifierKeys.Control));
        UiTestHost.DoEvents();
        Assert.True(entry.IsKept, "1 回目でキープ");
        Assert.Same(entry, grid.SelectedItem, "キープした後も選択を外さない");
        Assert.Equal(1, grid.SelectedItems.Count);
        Assert.True(grid.IsKeyboardFocusWithin, "キーボードフォーカスも一覧に残す");
        Assert.Same(entry, (FindAncestorOf<DataGridRow>(Keyboard.FocusedElement as DependencyObject))?.Item, "フォーカスは同じ行");

        Assert.True(_window.HandleGridKey(Key.K, ModifierKeys.Control));
        UiTestHost.DoEvents();
        Assert.False(entry.IsKept, "続けて押すと解除（トグル）");
        Assert.Same(entry, grid.SelectedItem);

        Assert.True(_window.HandleGridKey(Key.K, ModifierKeys.Control));
        UiTestHost.DoEvents();
        Assert.True(entry.IsKept, "3 回目で再びキープ");
        Assert.Same(entry, grid.SelectedItem);
    }

    [Test]
    public void CtrlK_WhenSortedByKeep_StillFollowsTheSameRow()
    {
        // キープ列で並べていると、キープすると行の位置が変わる。それでも同じ行を選んだままにする
        ShowWindow();
        var grid = _window.HistoryGrid;
        _vm.View.SortDescriptions.Clear();
        _vm.View.SortDescriptions.Add(new SortDescription(nameof(HistoryEntry.IsKept), ListSortDirection.Descending));
        var entry = (HistoryEntry)grid.Items[grid.Items.Count - 1];
        SelectAndFocusRow(entry);

        _window.HandleGridKey(Key.K, ModifierKeys.Control);
        UiTestHost.DoEvents();
        Assert.Same(entry, grid.Items[0], "前提: キープした行は先頭へ移る");
        Assert.Same(entry, grid.SelectedItem, "移った先でも選択したまま");
        Assert.True(grid.IsKeyboardFocusWithin);

        _window.HandleGridKey(Key.K, ModifierKeys.Control);
        UiTestHost.DoEvents();
        Assert.False(entry.IsKept, "続けて押すと同じ行の解除になる");
    }

    /// <summary>ダイアログとファイルを開く処理を記録用に差し替える。</summary>
    private (List<string> Dialogs, List<string> Opened) RecordDialogsAndOpens(Func<string, Task>? open = null)
    {
        var dialogs = new List<string>();
        var opened = new List<string>();
        _window.ShowMessage = dialogs.Add;
        _window.OpenFile = path =>
        {
            opened.Add(path);
            return open?.Invoke(path) ?? Task.CompletedTask;
        };
        return (dialogs, opened);
    }

    [Test]
    public void OpenMissingFile_ShowsRedStatusMessage_NotDialog()
    {
        ShowWindow();
        var (dialogs, opened) = RecordDialogsAndOpens();
        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        SelectAndFocusRow(missing);

        Assert.True(_window.HandleGridKey(Key.Enter, ModifierKeys.None));
        UiTestHost.DoEvents();

        Assert.Equal(0, dialogs.Count, "ダイアログを出さない");
        Assert.Equal(0, opened.Count, "見つからないファイルは開きに行かない");
        Assert.Equal(Visibility.Visible, _window.StatusMessageText.Visibility, "ステータスバーに出す");
        Assert.Equal(@"ファイルが見つかりません: C:\Old\gone.txt", _window.StatusMessageText.Text);
        Assert.Same(Application.Current.FindResource("ErrorBrush"), _window.StatusMessageText.Foreground, "赤字");
        Assert.True(_window.HistoryGrid.IsKeyboardFocusWithin, "キーボード操作を続けられる（フォーカスは一覧のまま）");
    }

    [Test]
    public void StatusMessage_ClearsWhenSelectingAnotherRowOrOpeningAgain()
    {
        ShowWindow();
        var (_, opened) = RecordDialogsAndOpens();
        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        SelectAndFocusRow(missing);
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        Assert.Equal(Visibility.Visible, _window.StatusMessageText.Visibility);

        var other = _store.Find(@"C:\Docs\budget.xlsx")!;
        _window.HistoryGrid.SelectedItem = other;
        Assert.Equal(Visibility.Collapsed, _window.StatusMessageText.Visibility, "別の行を選んだら消す");

        _window.ShowStatusMessage("前のメッセージ");
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Collapsed, _window.StatusMessageText.Visibility, "次に開いたら消す");
        Assert.SequenceEqual(new[] { other.Path }, opened, "あるファイルは開く");
    }

    [Test]
    public void OpenFailsBecauseFileVanished_ShowsStatusMessage_OtherErrorsShowDialog()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);

        // 開く直前に消された（Windows が「ファイルが見つかりません」= 2 を返した）
        var (dialogs, _) = RecordDialogsAndOpens(_ => Task.FromException(new System.ComponentModel.Win32Exception(2)));
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(0, dialogs.Count, "見つからないときはダイアログを出さない");
        Assert.Equal(@"ファイルが見つかりません: C:\Docs\budget.xlsx", _window.StatusMessageText.Text);

        // 関連付けが無いなど、見つからない以外の失敗は従来どおりダイアログ
        (dialogs, _) = RecordDialogsAndOpens(_ => Task.FromException(new System.ComponentModel.Win32Exception(1155)));
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(1, dialogs.Count, "見つからない以外はダイアログで知らせる");
        Assert.Contains("開けませんでした", dialogs[0]);
    }

    [Test]
    public void DefaultOrder_IsLastUsedDescending()
    {
        ShowWindow();
        var paths = _window.HistoryGrid.Items.Cast<HistoryEntry>().Select(e => e.Path).ToList();
        Assert.SequenceEqual(new[] { @"C:\Docs\budget.xlsx", @"C:\Old\gone.txt", @"C:\Docs\report-2026.docx" }, paths);
        Assert.Equal(ListSortDirection.Descending, _window.FindColumn(nameof(HistoryEntry.LastUsed))!.SortDirection);
    }

    [Test]
    public void Search_And_Filters_NarrowTheGrid()
    {
        ShowWindow();
        _window.SearchBox.Text = "docs 2026";
        UiTestHost.DoEvents();
        Assert.Equal(1, _window.HistoryGrid.Items.Count, "スペース区切りの AND");
        Assert.Contains("表示 1 件 / 全 3 件", _window.StatusText.Text);

        _window.SearchBox.Text = "";
        _vm.SelectedExtension = _vm.ExtensionChoices.First(c => c.Value == ".xlsx");
        UiTestHost.DoEvents();
        Assert.Equal(1, _window.HistoryGrid.Items.Count);

        _vm.SelectedExtension = MainViewModel.AllExtensions;
        _vm.KeptOnly = true;
        Assert.Equal(0, _window.HistoryGrid.Items.Count);
        _vm.ToggleKept(new[] { _store.Find(@"C:\Old\gone.txt")! });
        Assert.Equal(1, _window.HistoryGrid.Items.Count, "キープのみ");
    }

    [Test]
    public void ExtensionChoices_AreBuiltFromHistory()
    {
        var labels = _vm.ExtensionChoices.Select(c => c.Label).ToList();
        Assert.SequenceEqual(new[] { "すべての種類", ".docx", ".txt", ".xlsx" }, labels);
    }

    [Test]
    public void Delete_RemovesFromListAndRemembers()
    {
        ShowWindow();
        var target = _store.Find(@"C:\Docs\budget.xlsx")!;
        _vm.Delete(new[] { target });
        Assert.Equal(2, _window.HistoryGrid.Items.Count);
        Assert.True(_store.Deleted.ContainsKey(target.Path));
        Assert.False(_store.Register(target.Path, target.LastUsed, Now), "消した履歴は同じ .lnk で復活しない");
    }

    [Test]
    public void ColumnLayout_And_WindowSize_RoundTrip()
    {
        var settings = new AppSettings
        {
            WindowWidth = 800,
            WindowHeight = 500,
            Columns =
            {
                new ColumnLayout { Id = "IsKept", Width = 40, DisplayIndex = 3 },
                new ColumnLayout { Id = "FileName", Width = 300, DisplayIndex = 0 },
                new ColumnLayout { Id = "FolderPath", Width = 200, DisplayIndex = 1 },
                new ColumnLayout { Id = "LastUsed", Width = 150, DisplayIndex = 2 },
                new ColumnLayout { Id = "OpenCount", Width = 60, DisplayIndex = 4 },
            },
        };
        _window.ApplyLayout(settings);
        ShowWindow();

        var captured = new AppSettings();
        _window.CaptureLayout(captured);
        Assert.Equal(800.0, captured.WindowWidth);
        Assert.Equal(500.0, captured.WindowHeight);
        var byId = captured.Columns.ToDictionary(c => c.Id);
        Assert.Equal(3, byId["IsKept"].DisplayIndex);
        Assert.Equal(0, byId["FileName"].DisplayIndex);
        Assert.Equal(300.0, byId["FileName"].Width);
        Assert.Equal(200.0, byId["FolderPath"].Width);
    }

    [Test]
    public void OpenCountColumn_ShowsCountAndSorts()
    {
        var entry = _store.Find(@"C:\Docs\report-2026.docx")!;
        _store.Register(entry.Path, Now.AddMinutes(-30), Now);
        _store.Register(entry.Path, Now.AddMinutes(-20), Now);
        _vm.NotifyStoreChanged();
        ShowWindow();

        var column = Assert.NotNull(_window.FindColumn("OpenCount"), "回数の列");
        Assert.Equal("回数", column.Header);
        var row = (DataGridRow)_window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry);
        var cell = Assert.NotNull(FindVisual<TextBlock>(Assert.NotNull(row), t => t.Text == "3"), "3 回と表示");
        Assert.Equal(HorizontalAlignment.Right, cell.HorizontalAlignment, "数値は右寄せ");

        _vm.View.SortDescriptions.Clear();
        _vm.View.SortDescriptions.Add(new SortDescription(column.SortMemberPath, ListSortDirection.Descending));
        Assert.Same(entry, _window.HistoryGrid.Items[0], "回数で並び替えられる");
    }

    [Test]
    public void ColumnLayout_SavedBeforeNewColumn_KeepsOrderAndAppendsNewColumn()
    {
        // 回数の列が無かった頃の保存（5 列）を読んでも、並びを捨てずに新しい列を末尾に足す
        var settings = new AppSettings
        {
            Columns =
            {
                new ColumnLayout { Id = "IsKept", Width = 40, DisplayIndex = 4 },
                new ColumnLayout { Id = "FileName", Width = 300, DisplayIndex = 0 },
                new ColumnLayout { Id = "Extension", Width = 80, DisplayIndex = 1 },
                new ColumnLayout { Id = "FolderPath", Width = 200, DisplayIndex = 2 },
                new ColumnLayout { Id = "LastUsed", Width = 150, DisplayIndex = 3 },
            },
        };
        _window.ApplyLayout(settings);
        var order = _window.HistoryGrid.Columns.OrderBy(c => c.DisplayIndex).Select(MainWindow.ColumnId);
        Assert.SequenceEqual(new[] { "FileName", "FolderPath", "LastUsed", "IsKept", "OpenCount" }, order);
    }

    [Test]
    public void ExtensionColumn_IsRemoved_ButExtensionFilterRemains()
    {
        ShowWindow();
        Assert.Null(_window.FindColumn("Extension"), "種類の列は無い");
        Assert.False(_window.HistoryGrid.Columns.Any(c => (c.Header as string) == "種類"));
        Assert.SequenceEqual(new[] { "IsKept", "FileName", "FolderPath", "LastUsed", "OpenCount" },
            _window.HistoryGrid.Columns.OrderBy(c => c.DisplayIndex).Select(MainWindow.ColumnId), "列の既定の並び");

        // 拡張子での絞り込みは残す
        Assert.Equal(Visibility.Visible, _window.ExtensionBox.Visibility);
        _window.ExtensionBox.SelectedItem = _vm.ExtensionChoices.First(c => c.Value == ".docx");
        UiTestHost.DoEvents();
        Assert.Equal(1, _window.HistoryGrid.Items.Count, "拡張子で絞り込める");
        Assert.Equal(".docx", ((HistoryEntry)_window.HistoryGrid.Items[0]).Extension);
    }

    [Test]
    public void ColumnLayout_SavedWithRemovedColumn_KeepsOrderOfRemainingColumns()
    {
        // 種類の列があった頃の保存（6 列。回数が末尾 = 5 番目）を読んでも、残りの列の並びを捨てない
        var settings = new AppSettings
        {
            Columns =
            {
                new ColumnLayout { Id = "FileName", Width = 300, DisplayIndex = 0 },
                new ColumnLayout { Id = "LastUsed", Width = 150, DisplayIndex = 1 },
                new ColumnLayout { Id = "Extension", Width = 80, DisplayIndex = 2 },
                new ColumnLayout { Id = "FolderPath", Width = 200, DisplayIndex = 3 },
                new ColumnLayout { Id = "IsKept", Width = 40, DisplayIndex = 4 },
                new ColumnLayout { Id = "OpenCount", Width = 60, DisplayIndex = 5 },
            },
        };
        _window.ApplyLayout(settings);
        var order = _window.HistoryGrid.Columns.OrderBy(c => c.DisplayIndex).Select(MainWindow.ColumnId);
        Assert.SequenceEqual(new[] { "FileName", "LastUsed", "FolderPath", "IsKept", "OpenCount" }, order);
    }

    [Test]
    public void ResetLayout_RestoresDefaultColumnsAndWindowSize()
    {
        var settings = new AppSettings
        {
            WindowWidth = 700,
            WindowHeight = 400,
            Columns =
            {
                new ColumnLayout { Id = "OpenCount", Width = 90, DisplayIndex = 0 },
                new ColumnLayout { Id = "FileName", Width = 400, DisplayIndex = 1 },
                new ColumnLayout { Id = "FolderPath", Width = 120, DisplayIndex = 2 },
                new ColumnLayout { Id = "LastUsed", Width = 200, DisplayIndex = 3 },
                new ColumnLayout { Id = "IsKept", Width = 40, DisplayIndex = 4 },
            },
        };
        _window.ApplyLayout(settings);
        ShowWindow();

        _window.ResetLayout();
        var captured = new AppSettings { Columns = { } };
        _window.CaptureLayout(captured);

        var defaults = new AppSettings();
        Assert.Equal(defaults.WindowWidth, captured.WindowWidth, "ウインドウサイズを既定に戻す（直後の保存でも新しい値）");
        Assert.Equal(defaults.WindowHeight, captured.WindowHeight);
        Assert.SequenceEqual(new[] { "IsKept", "FileName", "FolderPath", "LastUsed", "OpenCount" },
            captured.Columns.OrderBy(c => c.DisplayIndex).Select(c => c.Id), "列の並びを画面定義どおりに戻す");
        var byId = captured.Columns.ToDictionary(c => c.Id);
        Assert.Equal(260.0, byId["FileName"].Width, "列の幅も戻す");
        Assert.Equal(60.0, byId["OpenCount"].Width);
    }

    [Test]
    public void ColumnLayout_WindowLargerThanScreen_IsClampedToWorkArea()
    {
        // 大きいモニターで保存したサイズを小さい画面で読んでも、画面からはみ出さない
        _window.ApplyLayout(new AppSettings { WindowWidth = 20000, WindowHeight = 15000 });
        var work = SystemParameters.WorkArea;
        Assert.True(_window.Width <= work.Width, $"幅 {_window.Width} は作業領域 {work.Width} 以下");
        Assert.True(_window.Height <= work.Height, $"高さ {_window.Height} は作業領域 {work.Height} 以下");

        _window.ApplyLayout(new AppSettings { WindowWidth = 800, WindowHeight = 500 });
        Assert.Equal(800.0, _window.Width, "収まる大きさはそのまま");
        Assert.Equal(500.0, _window.Height);
    }

    /// <summary>実際のキー入力と同じ経路（Preview → 通常のイベント）で、フォーカスのある要素にキーを送る。</summary>
    private static void PressKey(Key key)
    {
        var target = (Visual)Keyboard.FocusedElement;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        InputManager.Current.ProcessInput(args);
        UiTestHost.DoEvents();
    }

    [Test]
    public void Esc_WithDropDownOpen_ClosesOnlyTheDropDown()
    {
        ShowWindow();
        _window.PeriodBox.Focus();
        _window.PeriodBox.IsDropDownOpen = true;
        UiTestHost.DoEvents();

        PressKey(Key.Escape);

        Assert.False(_closed, "プルダウンを閉じる Esc で画面を閉じない（常駐しない設定ではアプリが終了してしまう）");
        Assert.True(_window.IsVisible);
        Assert.False(_window.PeriodBox.IsDropDownOpen, "プルダウンは閉じる");
    }

    [Test]
    public void Esc_WithMenuOpen_ClosesOnlyTheMenu()
    {
        ShowWindow();
        _window.ToolsMenu.Focus();
        _window.ToolsMenu.IsSubmenuOpen = true;
        UiTestHost.DoEvents();

        PressKey(Key.Escape);

        Assert.False(_closed, "メニューを閉じる Esc で画面を閉じない");
        Assert.True(_window.IsVisible);
    }

    [Test]
    public void Esc_FromSearchBoxOrGrid_StillClosesWindow()
    {
        ShowWindow();
        _window.SearchBox.Focus();
        _window.SearchBox.Text = "abc";
        PressKey(Key.Escape);
        Assert.True(_closed, "検索ボックスからの Esc は従来どおり閉じる");

        var other = new MainWindow(_vm) { ShowActivated = true };
        var otherClosed = false;
        other.Closed += (_, _) => otherClosed = true;
        other.ShowAndActivate();
        UiTestHost.DoEvents();
        other.HistoryGrid.SelectedIndex = 0;
        other.HistoryGrid.Focus();
        Keyboard.Focus(other.HistoryGrid);
        UiTestHost.DoEvents();
        PressKey(Key.Escape);
        if (!otherClosed) { other.AllowClose = true; other.Close(); }
        Assert.True(otherClosed, "一覧からの Esc も閉じる");
    }

    [Test]
    public void ColumnLayout_BrokenSettingsAreIgnored()
    {
        var settings = new AppSettings
        {
            Columns =
            {
                new ColumnLayout { Id = "Unknown", Width = 100, DisplayIndex = 0 },
                new ColumnLayout { Id = "FileName", Width = -5, DisplayIndex = 99 },
            },
        };
        _window.ApplyLayout(settings);
        ShowWindow();
        Assert.Equal(1, _window.FindColumn("FileName")!.DisplayIndex, "壊れた並びは反映しない");
        Assert.Equal(260.0, _window.FindColumn("FileName")!.Width.Value);
    }

    [Test]
    public void CaptureLayout_BeforeShown_KeepsSavedValues()
    {
        var settings = new AppSettings { WindowWidth = 1234 };
        settings.Columns.Add(new ColumnLayout { Id = "FileName", Width = 333, DisplayIndex = 1 });
        _window.CaptureLayout(settings);
        Assert.Equal(1234.0, settings.WindowWidth);
        Assert.Equal(333.0, settings.Columns[0].Width);
    }

    [Test]
    public void Closing_WhenResident_HidesInsteadOfExit()
    {
        _window.HideOnClose = true;
        ShowWindow();
        _window.Close();
        UiTestHost.DoEvents();
        Assert.False(_window.IsVisible, "常駐するときは閉じるボタンで隠す");
        Assert.False(_closed, "閉じない");
        ShowWindow();
        Assert.True(_window.IsVisible, "隠した後も呼び出し直せる");
    }

    [Test]
    public void Closing_WhenNotResident_ReallyCloses()
    {
        Assert.False(_window.HideOnClose, "既定は常駐しない");
        ShowWindow();
        _window.Close();
        UiTestHost.DoEvents();
        Assert.True(_closed, "常駐しないときは閉じる（アプリ側で終了する）");
    }

    [Test]
    public void CloseOrHide_FollowsResidentSetting()
    {
        _window.HideOnClose = true;
        ShowWindow();
        _window.CloseOrHide();
        UiTestHost.DoEvents();
        Assert.False(_window.IsVisible);
        Assert.False(_closed, "常駐するときの Esc は隠すだけ");

        _window.HideOnClose = false;
        ShowWindow();
        _window.CloseOrHide();
        UiTestHost.DoEvents();
        Assert.True(_closed, "常駐しないときの Esc は閉じる");
    }
}
