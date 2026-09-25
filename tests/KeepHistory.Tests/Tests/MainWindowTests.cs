using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    public MainWindowTests()
    {
        UiTestHost.EnsureApplication();
        _store.Register(@"C:\Docs\report-2026.docx", Now.AddHours(-3), Now);
        _store.Register(@"C:\Docs\budget.xlsx", Now.AddHours(-1), Now);
        _store.Register(@"C:\Old\gone.txt", Now.AddHours(-2), Now);
        _vm = new MainViewModel(_store, () => Now);
        _window = new MainWindow(_vm) { ShowActivated = true };
    }

    public void Dispose()
    {
        _window.AllowClose = true;
        _window.Close();
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

    [Test]
    public void KeepColumnHeader_StarIsCenteredAndYellow()
    {
        ShowWindow();
        var column = _window.FindColumn(nameof(HistoryEntry.IsKept))!;
        var header = Assert.NotNull(FindVisual<DataGridColumnHeader>(_window, h => h.Column == column));
        var star = Assert.NotNull(FindVisual<TextBlock>(header, t => t.Text == "★"), "見出しの★");

        var starCenter = star.TranslatePoint(new Point(star.ActualWidth / 2, 0), header).X;
        Assert.True(Math.Abs(starCenter - header.ActualWidth / 2) < 2.0,
            $"★は中央揃え（★の中心 {starCenter:0.0} / 見出し幅 {header.ActualWidth:0.0}）");
        Assert.Same(Application.Current.FindResource("StarOnBrush"), star.Foreground, "★は黄色");
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
                new ColumnLayout { Id = "IsKept", Width = 40, DisplayIndex = 4 },
                new ColumnLayout { Id = "FileName", Width = 300, DisplayIndex = 0 },
                new ColumnLayout { Id = "Extension", Width = 80, DisplayIndex = 1 },
                new ColumnLayout { Id = "FolderPath", Width = 200, DisplayIndex = 2 },
                new ColumnLayout { Id = "LastUsed", Width = 150, DisplayIndex = 3 },
            },
        };
        _window.ApplyLayout(settings);
        ShowWindow();

        var captured = new AppSettings();
        _window.CaptureLayout(captured);
        Assert.Equal(800.0, captured.WindowWidth);
        Assert.Equal(500.0, captured.WindowHeight);
        var byId = captured.Columns.ToDictionary(c => c.Id);
        Assert.Equal(4, byId["IsKept"].DisplayIndex);
        Assert.Equal(0, byId["FileName"].DisplayIndex);
        Assert.Equal(300.0, byId["FileName"].Width);
        Assert.Equal(200.0, byId["FolderPath"].Width);
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
    public void Closing_HidesInsteadOfExit()
    {
        ShowWindow();
        _window.Close();
        UiTestHost.DoEvents();
        Assert.False(_window.IsVisible, "閉じるボタンでは隠す");
        ShowWindow();
        Assert.True(_window.IsVisible, "隠した後も呼び出し直せる");
    }
}
