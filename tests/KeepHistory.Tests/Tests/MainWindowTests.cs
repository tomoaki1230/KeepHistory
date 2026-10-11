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
        // 実際のキーボードの状態（テスト中に押された Shift など）に左右されないよう、修飾キーは「何も押していない」にする
        _window = new MainWindow(_vm) { ShowActivated = true, CurrentModifiers = () => ModifierKeys.None };
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
        Assert.SequenceEqual(new[] { "表示(_V)", "ツール(_T)", "ヘルプ(_H)" }, top, "メニューバーは表示・ツール・ヘルプ（Windows のアプリと同じ順）");
        Assert.SequenceEqual(new[] { "一覧を日付でまとめる(_G)", "削除済みのローカルファイルを表示しない(_M)", "ネットワーク上のファイルを表示しない(_N)" },
            _window.ViewMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.SequenceEqual(new[] { "設定(_O)..." }, _window.ToolsMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.SequenceEqual(new[] { "サードパーティーのライセンス(_L)", "バージョン情報(_A)" },
            _window.HelpMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        Assert.True(_window.MainMenu.TranslatePoint(new Point(0, 0), _window.SearchBox).Y < 0, "メニューバーは検索行より上");
        var searchRow = Assert.NotNull(FindAncestorOf<DockPanel>(_window.KeptOnlyToggle));
        Assert.SequenceEqual(new[] { _window.RefreshButton }, searchRow.Children.OfType<Button>(),
            "検索行のボタンは更新だけ（設定・ヘルプはメニューバー）");
    }

    /// <summary>文字の実際に描かれる部分（インク）の高さ。フォントの行の高さではなく見た目の大きさを比べるため。</summary>
    private static double InkHeight(TextBlock text)
        => new FormattedText(text.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip).Extent;

    [Test]
    public void RefreshIcon_LooksAsLargeAsKeptOnlyStar_AndIsAligned()
    {
        ShowWindow();
        var icon = _window.RefreshIcon;
        var star = _window.KeptOnlyStar;
        Assert.Equal("\uE72C", icon.Text, "Windows 標準のアイコンフォントの「更新」（文字の ↻ は小さく下にずれるので使わない）");
        Assert.NotNull(FindVisual<TextBlock>(_window.RefreshButton, t => t.Text == "更新"), "「更新」の文字");

        var iconInk = InkHeight(icon);
        var starInk = InkHeight(star);
        Assert.True(Math.Abs(iconInk - starInk) <= starInk * 0.2,
            $"アイコンと「キープのみ」の星の見た目の大きさをそろえる（アイコン {iconInk:0.0} / 星 {starInk:0.0}）");

        var iconCenter = icon.TranslatePoint(new Point(0, icon.ActualHeight / 2), _window).Y;
        var starCenter = star.TranslatePoint(new Point(0, star.ActualHeight / 2), _window).Y;
        Assert.True(Math.Abs(iconCenter - starCenter) <= 2.0, $"上下の位置をそろえる（アイコン {iconCenter:0.0} / 星 {starCenter:0.0}）");
    }

    [Test]
    public void RefreshButton_IsRightOfKeptOnly_AndRequestsRefresh()
    {
        ShowWindow();
        var toggle = _window.KeptOnlyToggle;
        var refresh = _window.RefreshButton;
        var toggleRight = toggle.TranslatePoint(new Point(toggle.ActualWidth, 0), _window).X;
        var refreshLeft = refresh.TranslatePoint(new Point(0, 0), _window).X;
        Assert.True(refreshLeft >= toggleRight, $"「キープのみ」の右に置く（キープのみの右端 {toggleRight:0} / 更新の左端 {refreshLeft:0}）");
        var searchRow = Assert.NotNull(FindAncestorOf<DockPanel>(toggle));
        Assert.True(searchRow.Children.OfType<FrameworkElement>().All(e =>
                ReferenceEquals(e, refresh) || e.TranslatePoint(new Point(0, 0), _window).X < refreshLeft),
            "検索行のいちばん右");
        Assert.Contains("読み直", refresh.ToolTip as string, "何をするかをツールチップで示す");

        var requested = 0;
        _window.RefreshRequested += (_, _) => requested++;
        refresh.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(1, requested, "押すと更新を依頼する");
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
            Assert.Equal("1.2.0", AboutWindow.AppVersion);
            Assert.Equal("KeepHistory", about.ProductText.Text);
            Assert.Equal("作成者: Tomoaki Bessho", about.AuthorText.Text, "作成者を表示");
            Assert.Equal("Tomoaki Bessho", AboutWindow.Author, "csproj の作成者から読む");
            Assert.Contains(".NET 10", about.RuntimeText.Text, "動いている .NET の版を出す");
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
    public void OpenNetworkFile_ShowsProgress_ThenClearsWhenOpened()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);
        _window.IsNetworkPath = _ => true;
        var pending = new TaskCompletionSource();
        var (dialogs, opened) = RecordDialogsAndOpens(_ => pending.Task);

        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Visible, _window.StatusMessageText.Visibility, "開いている途中だと分かるようにする");
        Assert.Equal(MainWindow.OpeningMessagePrefix + entry.Path, _window.StatusMessageText.Text);
        Assert.Same(Application.Current.FindResource("SubTextBrush"), _window.StatusMessageText.Foreground, "途中経過は赤字にしない");

        pending.SetResult();
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Collapsed, _window.StatusMessageText.Visibility, "開けたら消す");
        Assert.Equal(0, dialogs.Count);
        Assert.Equal(1, opened.Count);
    }

    [Test]
    public void OpenLocalFile_DoesNotShowProgress()
    {
        ShowWindow();
        SelectAndFocusRow(_store.Find(@"C:\Docs\budget.xlsx")!);
        _window.IsNetworkPath = _ => false;
        var pending = new TaskCompletionSource();
        RecordDialogsAndOpens(_ => pending.Task);
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Collapsed, _window.StatusMessageText.Visibility, "すぐ開くローカルのファイルでは出さない（ちらつく）");
        pending.SetResult();
        UiTestHost.DoEvents();
    }

    [Test]
    public void OpenNetworkFile_WhileDisconnected_ShowsRedStatusMessage_NotDialog()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);
        _window.IsNetworkPath = _ => true;
        foreach (var code in new[] { 53, 67, 64, 1231 })
        {
            var (dialogs, _) = RecordDialogsAndOpens(_ => Task.FromException(new System.ComponentModel.Win32Exception(code)));
            _window.HandleGridKey(Key.Enter, ModifierKeys.None);
            UiTestHost.DoEvents();
            Assert.Equal(0, dialogs.Count, $"ネットワークに届かないとき（{code}）はダイアログを出さない");
            Assert.Contains("ネットワーク上の場所に接続できません", _window.StatusMessageText.Text, $"エラー {code}");
            Assert.Contains(entry.Path, _window.StatusMessageText.Text);
            Assert.Same(Application.Current.FindResource("ErrorBrush"), _window.StatusMessageText.Foreground, "赤字");
            Assert.False(entry.IsMissing, "一時的な切断なので、見つからない扱いにはしない");
        }
        Assert.True(_window.HistoryGrid.IsKeyboardFocusWithin, "キーボード操作を続けられる");
    }

    [Test]
    public void OpeningSameFileTwice_WhileWaiting_OpensOnlyOnce()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);
        _window.IsNetworkPath = _ => true;
        var pending = new TaskCompletionSource();
        var (_, opened) = RecordDialogsAndOpens(_ => pending.Task);

        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(1, opened.Count, "応答を待っている間に Enter を押しても、重ねて開かない");
        Assert.Equal(MainWindow.OpeningMessagePrefix + entry.Path, _window.StatusMessageText.Text, "開いている途中だと知らせる");

        pending.SetException(new System.ComponentModel.Win32Exception(67));
        UiTestHost.DoEvents();
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(2, opened.Count, "終わったら、もう一度開ける");
    }

    [Test]
    public void ClearSearchButton_IsShownOnlyWhileSearching()
    {
        ShowWindow();
        Assert.Equal(Visibility.Collapsed, _window.ClearSearchButton.Visibility, "検索語が無ければ出さない");
        _vm.SearchText = "budget";
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Visible, _window.ClearSearchButton.Visibility, "検索語があれば出す");
        _window.SearchBox.Text = string.Empty;
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Collapsed, _window.ClearSearchButton.Visibility, "キーボードで消しても隠れる");
    }

    [Test]
    public void ClearSearchButton_ClearsTheSearch_AndKeepsTypingInTheSearchBox()
    {
        ShowWindow();
        _window.SearchBox.Text = "budget";
        UiTestHost.DoEvents();
        Assert.Equal(1, _window.HistoryGrid.Items.Count, "前提: 絞り込まれている");
        // 一覧を操作した後に「×」を押した（フォーカスは一覧にある）
        SelectAndFocusRow(_store.Find(@"C:\Docs\budget.xlsx")!);
        Assert.False(_window.SearchBox.IsKeyboardFocused, "前提: フォーカスは一覧");

        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(_window.ClearSearchButton);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        UiTestHost.DoEvents();

        Assert.Equal(string.Empty, _vm.SearchText, "検索語を消す");
        Assert.Equal(3, _window.HistoryGrid.Items.Count, "一覧が元に戻る");
        Assert.True(_window.SearchBox.IsKeyboardFocused, "続けて入力できるよう、検索欄にフォーカスを置く");
        Assert.Equal(Visibility.Collapsed, _window.ClearSearchButton.Visibility);
    }

    [Test]
    public void SearchBoxButtons_DoNotTakeFocus_AndHaveNamesToReadAloud()
    {
        ShowWindow();
        _vm.RecentSearches.Remember("budget");
        _vm.SearchText = "budget";
        UiTestHost.DoEvents();
        foreach (var (button, name) in new[] { (_window.ClearSearchButton, "検索語を消す"), (_window.RecentSearchButton, "最近の検索語") })
        {
            Assert.False(button.Focusable, $"{name}: 押してもフォーカスを奪わない");
            Assert.False(button.IsTabStop, $"{name}: Tab で止まらない（検索欄 → ↓ で一覧、の流れを崩さない）");
            Assert.Equal(name, System.Windows.Automation.AutomationProperties.GetName(button), "読み上げ名");
        }
        Assert.Equal("検索語を消す", _window.ClearSearchButton.ToolTip as string);
    }

    // ---- 1.2.0: Ctrl+Enter でフォルダを開く ----

    private List<string> RecordReveals()
    {
        var revealed = new List<string>();
        _window.RevealInExplorer = path =>
        {
            revealed.Add(path);
            return Task.CompletedTask;
        };
        _window.OpenFolderOnly = path =>
        {
            revealed.Add("folder:" + path);
            return Task.CompletedTask;
        };
        return revealed;
    }

    [Test]
    public void CtrlEnter_OpensTheFolder_FromTheListAndTheSearchBox()
    {
        ShowWindow();
        var (_, opened) = RecordDialogsAndOpens();
        var revealed = RecordReveals();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);

        Assert.True(_window.HandleGridKey(Key.Enter, ModifierKeys.Control));
        UiTestHost.DoEvents();
        Assert.SequenceEqual(new[] { entry.Path }, revealed, "一覧で Ctrl+Enter: エクスプローラーでファイルを選んだ状態で開く");
        Assert.Equal(0, opened.Count, "ファイルは開かない");

        _vm.SearchText = "report";
        UiTestHost.DoEvents();
        Assert.True(_window.HandleSearchBoxKey(Key.Enter, ModifierKeys.Control));
        UiTestHost.DoEvents();
        Assert.Equal(@"C:\Docs\report-2026.docx", revealed[1], "検索欄で Ctrl+Enter: いちばん上の行のフォルダ");
        Assert.Equal(0, opened.Count);

        _window.HandleSearchBoxKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(1, opened.Count, "Enter だけなら従来どおりファイルを開く");
    }

    [Test]
    public void OpenFolderMenu_ShowsTheShortcut()
    {
        var items = _window.HistoryGrid.ContextMenu!.Items.OfType<MenuItem>().ToList();
        Assert.Equal("Ctrl+Enter", items.Single(i => (string)i.Header == "フォルダを開く").InputGestureText);
        Assert.Contains("Ctrl+Enter", _window.KeyHintText.Text, "画面下のキーの案内にも出す");
        Assert.Contains("Ctrl+Z", _window.KeyHintText.Text);
        Assert.Contains("Delete: 一覧から外す", _window.KeyHintText.Text, "Delete は「削除」ではなく「一覧から外す」と案内する");
        var remove = items.Single(i => (string)i.Header == "一覧から外す");
        Assert.Equal("Delete", remove.InputGestureText);
        Assert.Contains("パソコンのファイルは削除しません", remove.ToolTip as string, "ファイルは消えないと伝える");
        Assert.False(items.Any(i => (i.Header as string)?.Contains("削除") == true), "右クリックのメニューに「削除」という言葉を使わない");
    }

    // ---- 1.2.0: 削除の取り消し（Ctrl+Z） ----

    [Test]
    public void CtrlZ_RestoresTheDeletedRow_AndSelectsIt()
    {
        ShowWindow();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        entry.IsKept = true;
        SelectAndFocusRow(entry);
        Assert.True(_window.HandleGridKey(Key.Delete, ModifierKeys.None));
        UiTestHost.DoEvents();
        Assert.Null(_store.Find(entry.Path), "前提: 削除した");
        Assert.Contains("一覧から外しました: budget.xlsx", _window.StatusMessageText.Text, "「削除」ではなく「一覧から外す」と言う");
        Assert.Contains("ファイルはそのままです", _window.StatusMessageText.Text, "パソコンのファイルは消えていないと伝える");
        Assert.Contains("Ctrl+Z で戻せます", _window.StatusMessageText.Text, "取り消せることを知らせる");
        Assert.Same(Application.Current.FindResource("SubTextBrush"), _window.StatusMessageText.Foreground, "知らせは赤字にしない");

        Assert.True(_window.HandleGridKey(Key.Z, ModifierKeys.Control));
        UiTestHost.DoEvents();
        var restored = _store.Find(entry.Path);
        Assert.NotNull(restored, "Ctrl+Z で一覧に戻る");
        Assert.True(restored!.IsKept, "キープも元どおり");
        Assert.Same(restored, _window.HistoryGrid.SelectedItem, "戻した行を選ぶ");
        Assert.True(_window.HistoryGrid.IsKeyboardFocusWithin, "続けてキーボードで操作できる");
        Assert.Equal(MainWindow.RestoredMessagePrefix + "budget.xlsx", _window.StatusMessageText.Text);

        _window.HandleGridKey(Key.Z, ModifierKeys.Control);
        UiTestHost.DoEvents();
        Assert.Equal(MainWindow.NothingToUndoMessage, _window.StatusMessageText.Text, "戻せるものが無ければそう知らせる");
    }

    [Test]
    public void CtrlZ_InTheSearchBox_IsLeftToTheTextBox()
    {
        ShowWindow();
        SelectAndFocusRow(_store.Find(@"C:\Docs\budget.xlsx")!);
        _window.HandleGridKey(Key.Delete, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.False(_window.HandleUndoKey(Key.Z, ModifierKeys.Control, _window.SearchBox), "検索欄では文字の「元に戻す」を優先する");
        Assert.Null(_store.Find(@"C:\Docs\budget.xlsx"));
        Assert.True(_window.HandleUndoKey(Key.Z, ModifierKeys.Control, _window.RefreshButton), "ボタンなどにフォーカスがあれば取り消す");
        Assert.NotNull(_store.Find(@"C:\Docs\budget.xlsx"));
        Assert.False(_window.HandleUndoKey(Key.Z, ModifierKeys.None, _window.RefreshButton), "Z だけでは何もしない");
    }

    // ---- 1.2.0: このフォルダのファイルを表示 ----

    [Test]
    public void ShowFolderFiles_FiltersByFolder_AndCanBeCleared()
    {
        ShowWindow();
        Assert.Equal(Visibility.Collapsed, _window.FolderFilterBar.Visibility, "ふだんは帯を出さない");
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);
        Assert.NotNull(_window.HistoryGrid.ContextMenu!.Items.OfType<MenuItem>().SingleOrDefault(i => (string)i.Header == "このフォルダのファイルを表示"),
            "右クリックのメニューにある");

        _window.ShowFolderFiles(entry);
        UiTestHost.DoEvents();
        Assert.SequenceEqual(new[] { @"C:\Docs\budget.xlsx", @"C:\Docs\report-2026.docx" },
            _window.HistoryGrid.Items.Cast<HistoryEntry>().Select(e => e.Path), "同じフォルダのファイルだけ");
        Assert.Equal(Visibility.Visible, _window.FolderFilterBar.Visibility, "絞り込んでいることを帯で知らせる");
        Assert.Contains(@"C:\Docs", _window.FolderFilterText.Text);
        Assert.Same(entry, _window.HistoryGrid.SelectedItem, "選んでいた行は選んだまま");
        Assert.Equal("フォルダの絞り込みを解除", System.Windows.Automation.AutomationProperties.GetName(_window.ClearFolderFilterButton));

        _window.ClearFolderFilter();
        UiTestHost.DoEvents();
        Assert.Equal(3, _window.HistoryGrid.Items.Count, "解除したらすべて出す");
        Assert.Equal(Visibility.Collapsed, _window.FolderFilterBar.Visibility);
        Assert.Same(entry, _window.HistoryGrid.SelectedItem, "解除しても選んでいた行のまま");
    }

    // ---- 1.2.0: 最近の検索語 ----

    [Test]
    public void OpeningAFileWhileSearching_RemembersTheSearch()
    {
        ShowWindow();
        RecordDialogsAndOpens();
        RecordReveals();
        Assert.Equal(Visibility.Visible, _window.RecentSearchButton.Visibility, "▾ は常に出す（出たり消えたりしない）");
        Assert.False(_window.RecentSearchButton.IsEnabled, "覚えている語が無ければ押せない");
        _vm.SearchText = "budget";
        UiTestHost.DoEvents();
        _window.HandleSearchBoxKey(Key.Enter, ModifierKeys.None);
        _vm.SearchText = "report";
        UiTestHost.DoEvents();
        _window.HandleSearchBoxKey(Key.Enter, ModifierKeys.Control);
        UiTestHost.DoEvents();
        Assert.SequenceEqual(new[] { "report", "budget" }, _vm.RecentSearches.Items, "ファイル・フォルダを開いたときの検索語を新しい順に覚える");
        Assert.True(_window.RecentSearchButton.IsEnabled, "覚えたら押せる");
    }

    [Test]
    public void RecentSearches_CanBePickedWithAltDown()
    {
        ShowWindow();
        Assert.False(_window.HandleSearchBoxKey(Key.Down, ModifierKeys.Alt), "覚えている語が無ければ何もしない");
        _vm.RecentSearches.Remember("budget");
        _vm.RecentSearches.Remember("report");
        UiTestHost.DoEvents();

        Assert.True(_window.HandleSearchBoxKey(Key.Down, ModifierKeys.Alt));
        UiTestHost.DoEvents();
        Assert.True(_window.RecentSearchPopup.IsOpen, "Alt+↓ で一覧を出す");
        Assert.Equal("report", _window.RecentSearchList.SelectedItem as string, "いちばん新しい語を選んだ状態");

        // Esc は一覧だけを閉じる（画面は閉じない）
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window.RecentSearchList)!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        _window.RecentSearchList.RaiseEvent(esc);
        UiTestHost.DoEvents();
        Assert.True(esc.Handled);
        Assert.False(_window.RecentSearchPopup.IsOpen);
        Assert.True(_window.IsVisible, "画面は閉じない");

        // 一覧の下の「履歴を消す」にフォーカスがあるときの Esc も、一覧だけを閉じる
        _window.OpenRecentSearches();
        UiTestHost.DoEvents();
        var escOnClear = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window.ClearRecentSearchesInListButton)!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        _window.ClearRecentSearchesInListButton.RaiseEvent(escOnClear);
        UiTestHost.DoEvents();
        Assert.True(escOnClear.Handled && !_window.RecentSearchPopup.IsOpen && _window.IsVisible, "「履歴を消す」の上の Esc も一覧だけ閉じる");
        Assert.Equal(2, _vm.RecentSearches.Items.Count, "Esc では消さない");

        // ↓ で 2 番目の語に移って Enter（キー操作で選ぶ）
        _window.OpenRecentSearches();
        UiTestHost.DoEvents();
        _window.RecentSearchList.SelectedIndex = 1;
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window.RecentSearchList)!, 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        _window.RecentSearchList.RaiseEvent(enter);
        UiTestHost.DoEvents();
        Assert.False(_window.RecentSearchPopup.IsOpen);
        Assert.Equal("budget", _vm.SearchText, "選んだ語で検索する");
        Assert.Equal(1, _window.HistoryGrid.Items.Count);
        Assert.True(_window.SearchBox.IsKeyboardFocused, "続けて入力できるよう検索欄に戻る");
        Assert.Equal(_window.SearchBox.Text.Length, _window.SearchBox.CaretIndex, "文字の最後から入力できる");
    }

    [Test]
    public void SearchBoxButtons_SitInsideTheBox_AndDoNotHideTheText()
    {
        ShowWindow();
        _vm.RecentSearches.Remember("budget");
        _vm.SearchText = "budget";
        UiTestHost.DoEvents();
        var box = _window.SearchBox;
        Rect BoundsOf(FrameworkElement e) => e.TransformToVisual(box).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var clear = BoundsOf(_window.ClearSearchButton);
        var dropDown = BoundsOf(_window.RecentSearchButton);
        Assert.True(clear.Left >= 0 && clear.Top >= 0 && clear.Bottom <= box.ActualHeight, $"「×」は検索欄の中に収まる（{clear}）");
        Assert.True(dropDown.Left - clear.Right >= 0 && dropDown.Left - clear.Right < 8, "「×」は「▾」のすぐ左");
        Assert.True(box.ActualWidth - dropDown.Right <= 2, "「▾」は検索欄の右端（プルダウンと同じ位置）");
        Assert.True(dropDown.Top <= 2 && box.ActualHeight - dropDown.Bottom <= 2, "「▾」は検索欄の高さいっぱい（区切り線で入力部分と分ける）");
        Assert.True(box.Padding.Right >= box.ActualWidth - clear.Left, $"文字の右の余白（{box.Padding.Right}）が「×」と「▾」の幅以上なので、長い検索語が隠れない");
    }

    [Test]
    public void DropDown_IsAlwaysThere_AndExplainsWhenThereIsNothingToShow()
    {
        ShowWindow();
        var button = _window.RecentSearchButton;
        Assert.Equal(Visibility.Visible, button.Visibility, "検索語の有無にかかわらず出す");
        Assert.False(button.IsEnabled);
        Assert.Equal("最近の検索語はまだありません", button.ToolTip as string, "押せない理由をツールチップで伝える");
        Assert.True(ToolTipService.GetShowOnDisabled(button), "押せないときもツールチップを出す");

        _vm.RecentSearches.Remember("budget");
        UiTestHost.DoEvents();
        Assert.True(button.IsEnabled);
        Assert.Equal("最近の検索語（Alt+↓）", button.ToolTip as string, "キーボードでの出し方も伝える");
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
        UiTestHost.DoEvents();
        Assert.True(_window.RecentSearchPopup.IsOpen, "押すと一覧を出す");
        _window.RecentSearchPopup.IsOpen = false;
    }

    [Test]
    public void ClearHistoryInTheList_ForgetsAllSearches_AndReturnsToTheSearchBox()
    {
        ShowWindow();
        _vm.RecentSearches.Remember("budget");
        _vm.RecentSearches.Remember("report");
        var saveRequests = 0;
        _vm.RecentSearches.Changed += (_, _) => saveRequests++;
        _window.OpenRecentSearches();
        UiTestHost.DoEvents();
        Assert.Equal("履歴を消す", _window.ClearRecentSearchesInListButton.Content as string, "一覧のいちばん下にある");
        Assert.Equal("検索語の履歴を消す", System.Windows.Automation.AutomationProperties.GetName(_window.ClearRecentSearchesInListButton));

        _window.ClearRecentSearchesFromList();
        UiTestHost.DoEvents();
        Assert.Equal(0, _vm.RecentSearches.Items.Count, "その場ですべて消す");
        Assert.Equal(1, saveRequests, "消したことを保存する");
        Assert.False(_window.RecentSearchPopup.IsOpen, "一覧を閉じる");
        Assert.True(_window.SearchBox.IsKeyboardFocused, "検索欄に戻る");
        Assert.False(_window.RecentSearchButton.IsEnabled, "消した後は ▾ を押せない");
    }

    [Test]
    public void EnterRightAfterTyping_UsesTheNewSearch_EvenWhenSearchIsDeferred()
    {
        ShowWindow();
        _vm.DeferSearchThreshold = 1;
        var (_, opened) = RecordDialogsAndOpens();
        _window.SearchBox.Text = "report";
        Assert.True(_vm.IsSearchPending, "前提: 入力が止まるのを待っている");
        Assert.True(_window.HandleSearchBoxKey(Key.Enter, ModifierKeys.None));
        UiTestHost.DoEvents();
        Assert.SequenceEqual(new[] { @"C:\Docs\report-2026.docx" }, opened, "待たずに絞り込んでから開く（古い一覧のいちばん上を開かない）");
    }

    // ---- 1.2.0: ファイルの種類のアイコン ----

    [Test]
    public void FileNameColumn_ShowsTheFileTypeIcon_FadedWhenMissing()
    {
        ShowWindow();
        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        UiTestHost.DoEvents();
        foreach (var entry in _store.Entries)
        {
            var row = (DataGridRow)_window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry);
            var icon = Assert.NotNull(FindVisual<Image>(row, i => i.Name == "FileIcon"), $"{entry.FileName} にアイコンがある");
            Assert.NotNull(icon.Source, "拡張子からアイコンを出す");
            Assert.True(icon.Width == 16 && icon.Height == 16);
            Assert.Equal(entry.IsMissing ? 0.45 : 1.0, icon.Opacity, $"{entry.FileName}: 見つからないファイルはアイコンも淡く");
        }
    }

    // ---- 1.2.0: 日付でまとめる ----

    [Test]
    public void GroupByDate_ShowsHeadings_AndKeyboardStillWorks()
    {
        _store.Register(@"C:\Docs\last-month.txt", Now.AddDays(-45), Now);
        _vm.NotifyStoreChanged();
        _vm.GroupByDate = true;
        ShowWindow();
        var headers = new List<string>();
        void Collect(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is GroupItem { DataContext: System.Windows.Data.CollectionViewGroup group }) headers.Add($"{group.Name}:{group.ItemCount}");
                Collect(child);
            }
        }
        Collect(_window.HistoryGrid);
        Assert.SequenceEqual(new[] { "今日:3", "それより前:1" }, headers, "見出しと件数が出る");

        // 検索欄から ↓ で最初の行へ、Enter で開ける（見出しは選ばれない）
        var (_, opened) = RecordDialogsAndOpens();
        Assert.Null(_window.HistoryGrid.SelectedItem, "まとめても、勝手に行を選ばない（検索欄の Enter がいちばん上を開くように）");
        _window.FocusSearchBox();
        _window.HandleSearchBoxKey(Key.Down, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(@"C:\Docs\budget.xlsx", (_window.HistoryGrid.SelectedItem as HistoryEntry)?.Path);
        Assert.True(_window.HistoryGrid.IsKeyboardFocusWithin);
        _window.HandleGridKey(Key.Enter, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal(1, opened.Count);

        _vm.GroupByDate = false;
        UiTestHost.DoEvents();
        headers.Clear();
        Collect(_window.HistoryGrid);
        Assert.Equal(0, headers.Count, "オフなら見出しを出さない");
    }

    [Test]
    public void Title_ShowsTheSelectedFileName()
    {
        ShowWindow();
        Assert.Equal("KeepHistory", _window.Title, "選んでいないときはアプリ名だけ（「最近使ったファイル」は出さない）");
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        SelectAndFocusRow(entry);
        Assert.Equal("KeepHistory - budget.xlsx", _window.Title, "行を選んだらファイル名を出す");
        _window.HandleGridKey(Key.Down, ModifierKeys.None);
        _window.HistoryGrid.SelectedItem = _store.Find(@"C:\Old\gone.txt");
        Assert.Equal("KeepHistory - gone.txt", _window.Title, "選び直したら変わる");

        _vm.SearchText = "report";
        UiTestHost.DoEvents();
        Assert.Null(_window.HistoryGrid.SelectedItem, "前提: 絞り込みで選んでいた行が消えた");
        Assert.Equal("KeepHistory", _window.Title, "選択が外れたらアプリ名だけに戻る");

        _vm.SearchText = string.Empty;
        UiTestHost.DoEvents();
        SelectAndFocusRow(entry);
        _window.HandleGridKey(Key.Delete, ModifierKeys.None);
        UiTestHost.DoEvents();
        Assert.Equal($"KeepHistory - {((HistoryEntry)_window.HistoryGrid.SelectedItem!).FileName}", _window.Title,
            "削除した後は、選び直した行の名前になる");
    }

    [Test]
    public void ViewMenu_TogglesGroupingAndHidingMissingFiles()
    {
        ShowWindow();
        var group = _window.GroupByDateMenuItem;
        var hide = _window.HideMissingFilesMenuItem;
        Assert.True(group.IsCheckable && hide.IsCheckable, "チェックで切り替える項目");
        Assert.False(group.IsChecked, "一覧の状態に合わせて表示する（このテストの一覧はまとめていない）");

        group.IsChecked = true;
        UiTestHost.DoEvents();
        Assert.True(_vm.GroupByDate && _vm.IsGroupedByDate, "チェックするとその場で日付でまとめる");
        _vm.GroupByDate = false;
        UiTestHost.DoEvents();
        Assert.False(group.IsChecked, "設定画面などで変わったらメニューのチェックも変わる");

        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        _vm.NotifyStoreChanged();
        UiTestHost.DoEvents();
        Assert.Equal(3, _window.HistoryGrid.Items.Count, "前提: ふだんは見つからないファイルも淡く出す");
        hide.IsChecked = true;
        UiTestHost.DoEvents();
        Assert.True(_vm.HideMissingFiles);
        Assert.False(_window.HistoryGrid.Items.Contains(missing), "チェックすると見つからないファイルを出さない");
        Assert.Equal(2, _window.HistoryGrid.Items.Count);
        Assert.NotNull(_store.Find(missing.Path), "履歴からは消さない（表示しないだけ）");
        Assert.Contains("表示 2 件 / 全 3 件", _window.StatusText.Text, "件数の表示も合わせる");
        hide.IsChecked = false;
        UiTestHost.DoEvents();
        Assert.Equal(3, _window.HistoryGrid.Items.Count, "外すとまた出す");

        // ネットワーク上のファイル（ここでは C:\Docs をネットワーク扱いにする）
        _vm.NetworkPathCheckerFactory = () => path => path.StartsWith(@"C:\Docs", StringComparison.OrdinalIgnoreCase);
        var network = _window.HideNetworkFilesMenuItem;
        Assert.True(network.IsCheckable && !network.IsChecked, "既定は出す");
        network.IsChecked = true;
        UiTestHost.DoEvents();
        Assert.True(_vm.HideNetworkFiles);
        Assert.SequenceEqual(new[] { @"C:\Old\gone.txt" }, _window.HistoryGrid.Items.Cast<HistoryEntry>().Select(e => e.Path),
            "チェックするとネットワーク上のファイルを出さない");
        Assert.Equal(3, _store.Entries.Count, "履歴からは消さない（表示しないだけ）");
        network.IsChecked = false;
        UiTestHost.DoEvents();
        Assert.Equal(3, _window.HistoryGrid.Items.Count, "外すとまた出す");
    }

    [Test]
    public void ViewMenu_CheckMarkUsesThemeColor_SoItIsVisibleInDarkMode()
    {
        ShowWindow();
        _window.ViewMenu.IsSubmenuOpen = true;
        UiTestHost.DoEvents();
        var item = _window.GroupByDateMenuItem;
        item.ApplyTemplate();
        var mark = Assert.NotNull(item.Template.FindName("CheckMark", item) as TextBlock, "チェックで切り替える項目は専用の見た目（標準の印は色が固定でダークで見えない）");
        item.IsChecked = true;
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Visible, mark.Visibility, "チェックしたら印を出す");
        Assert.Same(Application.Current.FindResource("AccentBrush"), mark.Foreground, "印はテーマの色（ライト・ダークで切り替わる）");
        item.IsChecked = false;
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Hidden, mark.Visibility, "外したら印を消す（場所は空けたまま）");
        _window.ViewMenu.IsSubmenuOpen = false;
    }

    [Test]
    public void ExtensionDropDown_HasNoEmptySpaceBelowTheLastItem()
    {
        // 種類がたくさんあって、プルダウンをいちばん下までスクロールした
        for (int i = 0; i < 25; i++) _store.Register($@"C:\Many\f{i}.ext{i:00}", Now.AddMinutes(-i), Now);
        _vm.NotifyStoreChanged();
        ShowWindow();
        var box = _window.ExtensionBox;
        box.IsDropDownOpen = true;
        UiTestHost.DoEvents();
        var popup = (System.Windows.Controls.Primitives.Popup)box.Template.FindName("PART_Popup", box);
        var viewer = Assert.NotNull(FindVisual<ScrollViewer>(popup.Child, _ => true));
        Assert.True(viewer.ScrollableHeight > 0, "前提: 項目が多くてスクロールする");
        viewer.ScrollToEnd();
        UiTestHost.DoEvents();
        var last = (FrameworkElement)box.ItemContainerGenerator.ContainerFromIndex(box.Items.Count - 1);
        var bottom = last.TransformToAncestor(viewer).Transform(new Point(0, last.ActualHeight)).Y;
        Assert.True(Math.Abs(viewer.ActualHeight - bottom) < 1, $"最後の項目の下に空きが無い（空き {viewer.ActualHeight - bottom:F1} px）");
        box.IsDropDownOpen = false;
    }

    [Test]
    public void Refresh_ClearsTheSelection()
    {
        ShowWindow();
        var requests = 0;
        _window.RefreshRequested += (_, _) => requests++;
        foreach (var useKey in new[] { false, true })
        {
            SelectAndFocusRow(_store.Find(@"C:\Docs\budget.xlsx")!);
            Assert.NotNull(_window.HistoryGrid.SelectedItem, "前提: 行を選んでいる");
            if (useKey)
            {
                var f5 = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window)!, 0, Key.F5)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                };
                _window.HistoryGrid.RaiseEvent(f5);
            }
            else
            {
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(_window.RefreshButton);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            UiTestHost.DoEvents();
            var how = useKey ? "F5" : "「↻ 更新」ボタン";
            Assert.Null(_window.HistoryGrid.SelectedItem, $"{how}で選択を外す（未選択の状態にする）");
            Assert.False(_window.HistoryGrid.CurrentCell.IsValid, $"{how}で現在のセルも外す");
            Assert.Equal("KeepHistory", _window.Title, $"{how}の後はタイトルもアプリ名だけ");
        }
        Assert.Equal(2, requests, "更新は依頼する");
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

    /// <summary>行の中で、強調（太字）になっている文字列。</summary>
    private List<string> HighlightedTexts(HistoryEntry entry)
    {
        var row = (DataGridRow)_window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry);
        var result = new List<string>();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock tb)
            {
                foreach (var run in tb.Inlines.OfType<System.Windows.Documents.Run>())
                {
                    if (run.FontWeight == FontWeights.Bold) result.Add(run.Text);
                }
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(Assert.NotNull(row));
        return result;
    }

    [Test]
    public void Search_HighlightsMatchedPartsInFileNameAndFolder()
    {
        ShowWindow();
        _window.SearchBox.Text = "ＤＯＣＳ　ｂｕｄ";
        UiTestHost.DoEvents();
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        Assert.SequenceEqual(new[] { "bud", "Docs" }, HighlightedTexts(entry).OrderBy(t => t), "ファイル名とフォルダの一致部分を強調（全角の検索語でも）");
        var cell = Assert.NotNull(FindVisual<TextBlock>(
            (DataGridRow)_window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(entry), t => t.Text == "budget.xlsx"), "ファイル名の全文は変わらない");
        var highlighted = cell.Inlines.OfType<System.Windows.Documents.Run>().First(r => r.FontWeight == FontWeights.Bold);
        Assert.Same(Application.Current.FindResource("HighlightBrush"), highlighted.Background, "強調の背景色");
        Assert.Same(Application.Current.FindResource("HighlightTextBrush"), highlighted.Foreground, "強調の文字色（選んだ行の白い文字の上でも読める）");

        _window.SearchBox.Text = "";
        UiTestHost.DoEvents();
        Assert.Equal(0, HighlightedTexts(entry).Count, "検索語を消したら強調も消える");
    }

    [Test]
    public void F5_RequestsRefresh_FromSearchBoxAndGrid()
    {
        ShowWindow();
        var requested = 0;
        _window.RefreshRequested += (_, _) => requested++;
        _window.SearchBox.Focus();
        PressKey(Key.F5);
        Assert.Equal(1, requested, "検索ボックスで F5");
        SelectAndFocusRow(_store.Find(@"C:\Docs\budget.xlsx")!);
        PressKey(Key.F5);
        Assert.Equal(2, requested, "一覧で F5");
        _window.CurrentModifiers = () => ModifierKeys.Shift;
        PressKey(Key.F5);
        Assert.Equal(2, requested, "Shift+F5 では更新しない（修飾キーは CurrentModifiers から読む）");
    }

    [Test]
    public void EmptyMessage_ExplainsWhyNothingIsShown()
    {
        ShowWindow();
        Assert.Equal(Visibility.Collapsed, _window.EmptyMessageText.Visibility, "履歴があれば出さない");

        _window.SearchBox.Text = "存在しない語";
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Visible, _window.EmptyMessageText.Visibility, "絞り込みで 0 件なら出す");
        Assert.Contains("条件に一致する履歴はありません", _window.EmptyMessageText.Text);

        _window.SearchBox.Text = "";
        _vm.Delete(_store.Entries.ToList());
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Visible, _window.EmptyMessageText.Visibility);
        Assert.Contains("履歴はまだありません", _window.EmptyMessageText.Text, "履歴そのものが無いときは別の案内");

        _store.Register(@"C:\new.txt", Now, Now);
        _vm.NotifyStoreChanged();
        UiTestHost.DoEvents();
        Assert.Equal(Visibility.Collapsed, _window.EmptyMessageText.Visibility, "履歴が入ったら消える");
    }

    [Test]
    public void CopyFile_PutsTheFileOnClipboardForPasting()
    {
        ShowWindow();
        DataObject? copied = null;
        _window.SetClipboardData = data => copied = data;
        _window.HistoryGrid.SelectedItem = _store.Find(@"C:\Docs\budget.xlsx")!;
        _window.CopyFileMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var data = Assert.NotNull(copied, "クリップボードに入れる");
        Assert.SequenceEqual(new[] { @"C:\Docs\budget.xlsx" }, data.GetFileDropList().Cast<string>(), "エクスプローラーに貼り付けられる形（FileDrop）");
        // .NET 10 では、取り出した値が null の可能性があると示されるので、null でないことも確かめる
        var effect = Assert.NotNull(data.GetData("Preferred DropEffect") as System.IO.MemoryStream, "貼り付け時の動き（コピー）を入れてある");
        Assert.Equal((int)DragDropEffects.Copy, BitConverter.ToInt32(effect.ToArray(), 0), "貼り付けは移動ではなくコピー");
    }

    [Test]
    public void CopyFile_And_OpenWith_MissingFile_ShowsStatusInstead()
    {
        ShowWindow();
        var missing = _store.Find(@"C:\Old\gone.txt")!;
        missing.IsMissing = true;
        var copied = 0;
        var opened = 0;
        _window.SetClipboardData = _ => copied++;
        _window.ShowOpenWith = (_, _) =>
        {
            opened++;
            return true;
        };
        _window.HistoryGrid.SelectedItem = missing;

        _window.CopyFileMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        _window.OpenWithMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(0, copied, "見つからないファイルはコピーしない");
        Assert.Equal(0, opened, "見つからないファイルは開かない");
        Assert.Contains("ファイルが見つかりません", _window.StatusMessageText.Text);
    }

    [Test]
    public void OpenWith_ShowsWindowsDialogForTheFile_AndReportsFailure()
    {
        ShowWindow();
        IntPtr owner = IntPtr.Zero;
        string? path = null;
        var dialogs = new List<string>();
        _window.ShowMessage = dialogs.Add;
        _window.ShowOpenWith = (o, p) =>
        {
            owner = o;
            path = p;
            return true;
        };
        _window.HistoryGrid.SelectedItem = _store.Find(@"C:\Docs\budget.xlsx")!;
        _window.OpenWithMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(@"C:\Docs\budget.xlsx", path, "選んだファイルで「プログラムから開く」を出す");
        Assert.NotEqual(IntPtr.Zero, owner, "履歴画面の上に出す");
        Assert.Equal(0, dialogs.Count);

        _window.ShowOpenWith = (_, _) => false;
        _window.OpenWithMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(1, dialogs.Count, "出せなかったら知らせる");
    }

    [Test]
    public void ExcludeMenu_RequestsPatternsForFileFolderAndExtension()
    {
        ShowWindow();
        var requests = new List<ExcludeRequestEventArgs>();
        _window.ExcludeRequested += (_, e) => requests.Add(e);
        var entry = _store.Find(@"C:\Docs\budget.xlsx")!;
        _window.HistoryGrid.SelectedItem = entry;
        _window.UpdateExcludeMenu(entry);
        Assert.Equal("この拡張子（.xlsx）を記録しない", _window.ExcludeExtensionMenuItem.Header, "選んだ行の拡張子を示す");

        _window.ExcludeFileMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        _window.ExcludeFolderMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        _window.ExcludeExtensionMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.SequenceEqual(new[] { @"C:\Docs\budget.xlsx", @"C:\Docs\*", "*.xlsx" }, requests.Select(r => r.Pattern));
        Assert.Contains("budget.xlsx", requests[0].Description);

        _window.UpdateExcludeMenu(new HistoryEntry(@"C:\Tools\Makefile", Now));
        Assert.Equal(Visibility.Collapsed, _window.ExcludeExtensionMenuItem.Visibility, "拡張子が無ければ出さない");
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

    private static readonly Rect TestWorkArea = new(100, 50, 1600, 900);

    private void UseTestScreens()
    {
        _window.WorkAreaAtCursor = () => TestWorkArea;
        _window.AllWorkAreas = () => new[] { TestWorkArea };
    }

    [Test]
    public void Placement_MouseScreenCenter_CentersOnCursorScreenEveryTime()
    {
        UseTestScreens();
        _window.ApplyLayout(new AppSettings { WindowWidth = 800, WindowHeight = 500, Placement = WindowPlacementMode.MouseScreenCenter });
        ShowWindow();
        Assert.Equal(100 + 400.0, _window.Left, "マウスのある画面の中央");
        Assert.Equal(50 + 200.0, _window.Top);

        _window.Left = 300;
        _window.HideToTray();
        ShowWindow();
        Assert.Equal(100 + 400.0, _window.Left, "呼び出すたびに中央へ");
    }

    [Test]
    public void Placement_LastPosition_UsesSavedPosition_OrCentersIfOffScreen()
    {
        UseTestScreens();
        _window.ApplyLayout(new AppSettings
        {
            WindowWidth = 800, WindowHeight = 500, Placement = WindowPlacementMode.LastPosition, WindowLeft = 150, WindowTop = 80,
        });
        ShowWindow();
        Assert.Equal(150.0, _window.Left, "前回の位置");
        Assert.Equal(80.0, _window.Top);

        _window.Left = 700;
        _window.HideToTray();
        ShowWindow();
        Assert.Equal(700.0, _window.Left, "隠す前の位置に出す");

        var captured = new AppSettings();
        _window.CaptureLayout(captured);
        Assert.Equal(700.0, captured.WindowLeft, "位置を保存する");

        var other = new MainWindow(_vm) { WorkAreaAtCursor = () => TestWorkArea, AllWorkAreas = () => new[] { TestWorkArea } };
        other.ApplyLayout(new AppSettings
        {
            WindowWidth = 800, WindowHeight = 500, Placement = WindowPlacementMode.LastPosition, WindowLeft = 5000, WindowTop = 80,
        });
        other.PlaceBeforeShow();
        Assert.Equal(100 + 400.0, other.Left, "前回の位置が画面外（外したモニター）なら中央");
        other.Close();
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
