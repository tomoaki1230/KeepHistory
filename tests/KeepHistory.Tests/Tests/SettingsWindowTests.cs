using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class SettingsWindowTests : IDisposable
{
    private readonly SettingsWindow _window;
    private readonly List<HotkeySetting> _checked = new();

    /// <summary>このキーの組み合わせは「ほかのアプリが使用中」として扱う。</summary>
    private string _unavailableKey = "J";

    public SettingsWindowTests()
    {
        UiTestHost.EnsureApplication();
        var settings = new AppSettings { RetentionDays = 90 };
        settings.ExcludePatterns = new() { "*.tmp", "~$*" };
        _window = new SettingsWindow(settings, h =>
        {
            _checked.Add(h.Clone());
            return h.Key != _unavailableKey;
        });
    }

    public void Dispose() => _window.Close();

    private sealed class DisposableWindow : IDisposable
    {
        public DisposableWindow(SettingsWindow value) => Value = value;

        public SettingsWindow Value { get; }

        public void Dispose() => Value.Close();
    }

    [Test]
    public void LoadsCurrentValues()
    {
        Assert.Equal("3か月", (_window.RetentionBox.SelectedItem as Choice<int>)?.Label);
        Assert.Equal(false, _window.StayResidentBox.IsChecked, "常駐は既定でしない");
        Assert.Equal("*.tmp" + Environment.NewLine + "~$*", _window.ExcludePatternsBox.Text);
        Assert.Equal("H", _window.KeyBox.SelectedItem);
        Assert.Equal(true, _window.CtrlBox.IsChecked);
    }

    [Test]
    public void BuildsResultFromInput()
    {
        _window.RetentionBox.SelectedItem = AppSettings.RetentionChoices.First(c => c.Label == "6か月");
        _window.StayResidentBox.IsChecked = true;
        _window.ExcludePatternsBox.Text = "*.bak\r\n\r\n  *.old  \n";
        _window.KeyBox.SelectedItem = "F9";
        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;

        Assert.True(_window.TryBuildResult(out var result, out var error), error);
        Assert.Equal(180, result.RetentionDays);
        Assert.True(result.StayResident);
        Assert.SequenceEqual(new[] { "*.bak", "*.old" }, result.ExcludePatterns);
        Assert.Equal("F9", result.Hotkey.Key);
        Assert.False(result.Hotkey.HasModifier, "ファンクションキーは単独でも可");
    }

    [Test]
    public void RetentionDropdown_OffersOnlyFixedChoices()
    {
        var labels = _window.RetentionBox.Items.Cast<Choice<int>>().Select(c => c.Label);
        Assert.SequenceEqual(new[] { "1か月", "3か月", "6か月", "1年" }, labels);
        Assert.False(_window.RetentionBox.IsEditable, "自由入力はさせない");
    }

    [Test]
    public void RetentionDropdown_OldValueIsShownAsNextLongerChoice()
    {
        using var window = new DisposableWindow(new SettingsWindow(new AppSettings { RetentionDays = 100 }));
        Assert.Equal("6か月", (window.Value.RetentionBox.SelectedItem as Choice<int>)?.Label);
    }

    [Test]
    public void Hotkey_UnavailableIsShownImmediately()
    {
        Assert.Contains("Ctrl+Alt+H は使えます", _window.HotkeyStatusText.Text, "開いた時点で今の組み合わせを確かめる");
        Assert.Same(_window.FindResource("SuccessBrush"), _window.HotkeyStatusText.Foreground);

        _window.KeyBox.SelectedItem = "J";
        Assert.Equal(Visibility.Visible, _window.HotkeyStatusText.Visibility);
        Assert.Contains("Ctrl+Alt+J はほかのアプリか Windows が使っているため使えません", _window.HotkeyStatusText.Text, "選んだその場で分かる");
        Assert.Same(_window.FindResource("ErrorBrush"), _window.HotkeyStatusText.Foreground, "使えないときは赤");
        Assert.False(_window.TryBuildResult(out _, out var error), "使えないホットキーでは保存しない");
        Assert.Contains("使えません", error);

        _window.KeyBox.SelectedItem = "K";
        Assert.Contains("Ctrl+Alt+K は使えます", _window.HotkeyStatusText.Text);
        Assert.True(_window.TryBuildResult(out _, out error), error);
    }

    [Test]
    public void Hotkey_ModifierChangesAreCheckedWithCurrentCombination()
    {
        _checked.Clear();
        _window.ShiftBox.IsChecked = true;
        var last = _checked[^1];
        Assert.True(last.Control && last.Alt && last.Shift && !last.Win, "修飾キーを変えたら、その組み合わせで確かめる");
        Assert.Contains("Ctrl+Alt+Shift+H", _window.HotkeyStatusText.Text);

        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;
        _window.ShiftBox.IsChecked = false;
        Assert.Contains("いずれかを組み合わせてください", _window.HotkeyStatusText.Text, "修飾キーが無いこともその場で分かる");
        Assert.Same(_window.FindResource("ErrorBrush"), _window.HotkeyStatusText.Foreground);
    }

    [Test]
    public void Hotkey_DisabledHidesStatusAndAllowsSaving()
    {
        _window.KeyBox.SelectedItem = "J";
        _window.HotkeyEnabledBox.IsChecked = false;
        Assert.Equal(Visibility.Collapsed, _window.HotkeyStatusText.Visibility, "ホットキーを使わないなら表示しない");
        Assert.True(_window.TryBuildResult(out var result, out var error), error);
        Assert.False(result.Hotkey.Enabled);
    }

    [Test]
    public void ResidentSection_ExplainsCountIsInaccurateWhenOff()
    {
        Assert.Contains("オフの場合", _window.ResidentCountNote.Text);
        Assert.Contains("回数", _window.ResidentCountNote.Text);
        Assert.Contains("正確に取得できません", _window.ResidentCountNote.Text);
    }

    [Test]
    public void ExcludePatterns_ResetToDefaults()
    {
        _window.ExcludePatternsBox.Text = "*.bak";
        _window.ResetExcludePatternsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(string.Join(Environment.NewLine, ExclusionFilter.DefaultPatterns), _window.ExcludePatternsBox.Text);
        Assert.True(_window.TryBuildResult(out var result, out var error), error);
        Assert.SequenceEqual(ExclusionFilter.DefaultPatterns, result.ExcludePatterns, "既定の除外パターンで保存される");
    }

    [Test]
    public void ResetAll_AsksFirst_CancelDoesNothing()
    {
        var resets = 0;
        string? asked = null;
        var window = new SettingsWindow(new AppSettings { StayResident = true }, _ => true, resetAll: () => resets++);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Confirm = message =>
            {
                asked = message;
                return false;
            };
            window.ResetAllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.NotNull(asked, "その場で確認する");
            Assert.Equal(0, resets, "キャンセルなら設定ファイルを消さない");
            Assert.False(window.ResetPerformed);
            Assert.False(closed, "設定画面はそのまま");
            Assert.Equal(true, window.StayResidentBox.IsChecked, "入力欄も変えない");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ResetAll_Ok_DeletesSettingsAndClosesImmediately()
    {
        var resets = 0;
        var window = new SettingsWindow(new AppSettings(), _ => true, resetAll: () => resets++);
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Confirm = _ => true;
        window.ResetAllButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Equal(1, resets, "OK なら設定ファイルを消す処理を呼ぶ");
        Assert.True(window.ResetPerformed);
        Assert.True(closed, "設定画面を閉じる");
        Assert.Null(window.Result, "入力中の変更は捨てる（OK 扱いにしない）");
    }

    [Test]
    public void ResetAll_ConfirmMessage_ExplainsWhatIsResetAndKept()
    {
        var text = SettingsWindow.ResetConfirmMessage;
        Assert.Contains("常駐、保持期間、除外パターン、ホットキー", text);
        Assert.Contains("列の幅・並び、ウインドウサイズ", text);
        Assert.Contains("・履歴、キープ、外した履歴", text, "履歴も消すことを明記する");
        Assert.Contains("元に戻せません", text);
        Assert.Contains("「最近使った項目」から読み直します", text, "空のままにはならないことを知らせる");
        Assert.False(text.Contains("そのまま残ります"), "「履歴は残る」とは書かない");
        Assert.Contains("次回の起動時には、常駐するかを確かめる画面", text);
        Assert.False(_window.ResetAllButton.IsEnabled, "消す処理が無ければ押せない");
    }

    [Test]
    public void DeletedHistoryButton_OpensRestoreScreen()
    {
        Assert.False(_window.DeletedHistoryButton.IsEnabled, "開く処理が無ければ押せない");
        var opened = 0;
        var window = new SettingsWindow(new AppSettings(), _ => true, () => opened++);
        try
        {
            Assert.True(window.DeletedHistoryButton.IsEnabled);
            window.DeletedHistoryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, opened, "設定画面から削除した履歴の画面を開く");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Ok_WhenHistoryWouldBeRemoved_AsksFirst()
    {
        AppSettings? previewed = null;
        var window = new SettingsWindow(new AppSettings(), _ => true,
            previewRemoval: s =>
            {
                previewed = s;
                return (3, 2);
            });
        try
        {
            string? asked = null;
            window.Confirm = message =>
            {
                asked = message;
                return false;
            };
            window.ExcludePatternsBox.Text = "*";
            Assert.False(window.TryAccept(), "キャンセルなら確定しない（設定画面に残る）");
            Assert.Null(window.Result);
            Assert.SequenceEqual(new[] { "*" }, Assert.NotNull(previewed).ExcludePatterns, "入力中の設定で数える");
            Assert.Contains("5 件", Assert.NotNull(asked), "消える件数を示す");
            Assert.Contains("除外パターンに当たる履歴: 3 件", asked);
            Assert.Contains("保持期間を過ぎた履歴: 2 件", asked);
            Assert.Contains("キープした履歴は残ります", asked);

            window.Confirm = _ => true;
            Assert.True(window.TryAccept(), "OK なら確定する");
            Assert.NotNull(window.Result);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Ok_WhenNothingWouldBeRemoved_DoesNotAsk()
    {
        var asked = 0;
        var window = new SettingsWindow(new AppSettings(), _ => true, previewRemoval: _ => (0, 0));
        try
        {
            window.Confirm = _ =>
            {
                asked++;
                return false;
            };
            Assert.True(window.TryAccept());
            Assert.Equal(0, asked, "消える履歴が無ければ確認しない");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void RemovalMessage_OnlyListsNonZeroReasons()
    {
        var text = SettingsWindow.BuildRemovalMessage(4, 0);
        Assert.Contains("4 件", text);
        Assert.False(text.Contains("保持期間を過ぎた履歴"), "0 件の理由は書かない");
    }

    [Test]
    public void StartupPlacementAndTheme_LoadAndBuild()
    {
        var window = new SettingsWindow(new AppSettings
        {
            StartWithWindows = true, Placement = WindowPlacementMode.LastPosition, Theme = AppTheme.Dark,
        }, _ => true);
        try
        {
            Assert.Equal(true, window.StartWithWindowsBox.IsChecked);
            Assert.Equal("前回の位置", (window.PlacementBox.SelectedItem as Choice<WindowPlacementMode>)?.Label);
            Assert.Equal("ダーク", (window.ThemeBox.SelectedItem as Choice<AppTheme>)?.Label);

            window.StartWithWindowsBox.IsChecked = false;
            window.PlacementBox.SelectedIndex = 0;
            window.ThemeBox.SelectedIndex = 1;
            Assert.True(window.TryBuildResult(out var result, out var error), error);
            Assert.False(result.StartWithWindows);
            Assert.Equal(WindowPlacementMode.MouseScreenCenter, result.Placement);
            Assert.Equal(AppTheme.Light, result.Theme);
        }
        finally
        {
            window.Close();
        }
        Assert.Equal("マウスのある画面の中央", ((Choice<WindowPlacementMode>)new SettingsWindow(new AppSettings(), _ => true).PlacementBox.SelectedItem).Label, "既定");
    }

    private static TabItem? TabOf(DependencyObject element)
    {
        for (var current = element; current != null; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is TabItem tab) return tab;
        }
        return null;
    }

    [Test]
    public void Tabs_GroupSettings()
    {
        Assert.SequenceEqual(new[] { "全般", "履歴", "ホットキー" },
            _window.SettingsTabs.Items.OfType<TabItem>().Select(t => t.Header as string), "設定を 3 つのタブに分ける");
        foreach (var element in new FrameworkElement[] { _window.StayResidentBox, _window.StartWithWindowsBox, _window.PlacementBox, _window.ThemeBox })
        {
            Assert.Same(_window.GeneralTab, TabOf(element), element.Name + " は「全般」");
        }
        foreach (var element in new FrameworkElement[] { _window.RetentionBox, _window.ExcludePatternsBox, _window.ResetExcludePatternsButton, _window.DeletedHistoryButton })
        {
            Assert.Same(_window.HistoryTab, TabOf(element), element.Name + " は「履歴」");
        }
        foreach (var element in new FrameworkElement[] { _window.HotkeyEnabledBox, _window.CtrlBox, _window.KeyBox, _window.HotkeyStatusText })
        {
            Assert.Same(_window.HotkeyTab, TabOf(element), element.Name + " は「ホットキー」");
        }
        foreach (var element in new FrameworkElement[] { _window.OkButton, _window.ResetAllButton, _window.ErrorText })
        {
            Assert.Null(TabOf(element), element.Name + " はタブの外（どのタブでも見える）");
        }
        Assert.Same(_window.GeneralTab, _window.SettingsTabs.SelectedItem, "開いたときは「全般」");
    }

    [Test]
    public void Tabs_EveryTabFitsAndHeightStaysTheSame()
    {
        _window.Show();
        UiTestHost.DoEvents();
        var height = _window.ActualHeight;
        foreach (var tab in _window.SettingsTabs.Items.OfType<TabItem>())
        {
            _window.SettingsTabs.SelectedItem = tab;
            UiTestHost.DoEvents();
            var scroll = Assert.NotNull(tab.Content as ScrollViewer, "中身はスクロールできる入れ物に入れる（万一はみ出しても隠れない）");
            Assert.Equal(0.0, scroll.ScrollableHeight, $"「{tab.Header}」の中身がスクロールせずに収まる");
            Assert.Equal(height, _window.ActualHeight, $"「{tab.Header}」に切り替えても画面の高さは変わらない");
        }
    }

    [Test]
    public void Tabs_SelectedHeaderIsBold_ButTheContentsAreNot()
    {
        _window.Show();
        UiTestHost.DoEvents();
        foreach (var tab in _window.SettingsTabs.Items.OfType<TabItem>())
        {
            _window.SettingsTabs.SelectedItem = tab;
            UiTestHost.DoEvents();
            var header = Assert.NotNull(FindText(tab, (string)tab.Header), "タブの見出し");
            Assert.Equal(FontWeights.SemiBold, header.FontWeight, $"選んだタブ「{tab.Header}」の見出しは太字");
        }
        _window.SettingsTabs.SelectedItem = _window.GeneralTab;
        UiTestHost.DoEvents();
        Assert.Equal(FontWeights.Normal, _window.StayResidentBox.FontWeight, "タブの中身は太字にしない（見出しの太字が受け継がれない）");
        Assert.Equal(FontWeights.Normal, _window.PlacementBox.FontWeight);
        Assert.Equal(FontWeights.Normal, Assert.NotNull(FindText(_window.HistoryTab, "履歴")).FontWeight, "選んでいないタブの見出しは普通の太さ");
    }

    private static TextBlock? FindText(DependencyObject root, string text)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock t && t.Text == text) return t;
            var found = FindText(child, text);
            if (found != null) return found;
        }
        return null;
    }

    [Test]
    public void Error_SwitchesToTheTabWithTheProblem()
    {
        _window.SettingsTabs.SelectedItem = _window.GeneralTab;
        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;
        Assert.False(_window.TryAccept());
        Assert.Same(_window.HotkeyTab, _window.SettingsTabs.SelectedItem, "ホットキーの誤りなら「ホットキー」タブへ");
        Assert.Equal(Visibility.Visible, _window.ErrorText.Visibility, "エラーはタブの外に出す");

        _window.CtrlBox.IsChecked = true;
        _window.SettingsTabs.SelectedItem = _window.GeneralTab;
        _window.RetentionBox.SelectedItem = null;
        Assert.False(_window.TryAccept());
        Assert.Same(_window.HistoryTab, _window.SettingsTabs.SelectedItem, "保持期間の誤りなら「履歴」タブへ");
    }

    [Test]
    public void RejectsInvalidInput()
    {
        _window.RetentionBox.SelectedItem = null;
        Assert.False(_window.TryBuildResult(out _, out var error));
        Assert.Contains("保持期間", error);

        _window.RetentionBox.SelectedIndex = 0;
        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;
        _window.KeyBox.SelectedItem = "H";
        Assert.False(_window.TryBuildResult(out _, out error), "文字キー単独のホットキーは不可");
        Assert.Contains("ホットキー", error);
    }

    [Test]
    public void GroupByDate_LoadsAndBuilds()
    {
        using var on = new DisposableWindow(new SettingsWindow(new AppSettings(), _ => true));
        Assert.True(on.Value.GroupByDateBox.IsChecked == true, "既定はまとめる");
        on.Value.GroupByDateBox.IsChecked = false;
        Assert.True(on.Value.TryBuildResult(out var result, out _));
        Assert.False(result.GroupByDate, "外したら保存する設定もまとめない");
        Assert.True(on.Value.GeneralTab.Content is System.Windows.DependencyObject general
                    && System.Windows.LogicalTreeHelper.FindLogicalNode(general, "GroupByDateBox") != null, "「全般」タブにある");

        using var off = new DisposableWindow(new SettingsWindow(new AppSettings { GroupByDate = false }, _ => true));
        Assert.False(off.Value.GroupByDateBox.IsChecked == true, "保存した設定を読み込む");
    }

    [Test]
    public void ClearRecentSearches_ClearsImmediately_IndependentOfOk()
    {
        var cleared = 0;
        using var window = new DisposableWindow(new SettingsWindow(new AppSettings(), _ => true,
            clearRecentSearches: () => cleared++, recentSearchCount: 3));
        var w = window.Value;
        Assert.True(w.ClearRecentSearchesButton.IsEnabled);
        Assert.Contains("3 件", w.RecentSearchCountText.Text, "覚えている件数を出す");
        Assert.True(w.HistoryTab.Content is System.Windows.DependencyObject history
                    && System.Windows.LogicalTreeHelper.FindLogicalNode(history, "ClearRecentSearchesButton") != null, "「履歴」タブにある");

        w.ClearRecentSearches();
        Assert.Equal(1, cleared, "押したらすぐ消す（OK を待たない）");
        Assert.False(w.ClearRecentSearchesButton.IsEnabled, "消した後は押せない");
        Assert.Contains("消しました", w.RecentSearchCountText.Text);

        using var none = new DisposableWindow(new SettingsWindow(new AppSettings(), _ => true,
            clearRecentSearches: () => cleared++, recentSearchCount: 0));
        Assert.False(none.Value.ClearRecentSearchesButton.IsEnabled, "覚えている語が無ければ押せない");
        Assert.Contains("ありません", none.Value.RecentSearchCountText.Text);
        Assert.Contains("検索語の履歴", SettingsWindow.ResetConfirmMessage, "「すべて初期状態に戻す」の確認文にも載せる");
    }

    [Test]
    public void StartWithWindows_AlsoKeepsTheAppResident()
    {
        using var window = new DisposableWindow(new SettingsWindow(new AppSettings { StayResident = false, StartWithWindows = false }, _ => true));
        var w = window.Value;
        Assert.True(w.StayResidentBox.IsEnabled, "前提: 起動時に起動しないなら、常駐は選べる");

        w.StartWithWindowsBox.IsChecked = true;
        Assert.True(w.StayResidentBox.IsChecked == true, "起動時に起動するなら、常駐もオンにする");
        Assert.False(w.StayResidentBox.IsEnabled, "起動時に起動する間は、常駐を外せない（サインインのたびに画面が開かないように）");
        Assert.Equal("Windows の起動時に起動する場合は常駐します", w.StayResidentBox.ToolTip as string, "外せない理由を伝える");
        Assert.True(w.TryBuildResult(out var result, out _));
        Assert.True(result.StartWithWindows && result.StayResident);
        w.StayResidentBox.IsChecked = false; // 画面では外せないが、外れていても
        Assert.True(w.TryBuildResult(out result, out _));
        Assert.True(result.StayResident, "起動時に起動するなら、保存する設定も常駐にする");
        w.StayResidentBox.IsChecked = true; // 画面どおりの状態に戻す

        w.StartWithWindowsBox.IsChecked = false;
        Assert.True(w.StayResidentBox.IsEnabled, "起動時に起動しないなら、また選べる");
        Assert.True(w.StayResidentBox.IsChecked == true, "常駐はオンのまま（勝手に外さない）");
        w.StayResidentBox.IsChecked = false;
        Assert.True(w.TryBuildResult(out result, out _));
        Assert.False(result.StartWithWindows || result.StayResident, "どちらもオフにできる");

        // 以前の設定で「起動時に起動する・常駐しない」だった場合も、開いたら常駐をオンにする
        using var old = new DisposableWindow(new SettingsWindow(new AppSettings { StayResident = false, StartWithWindows = true }, _ => true));
        Assert.True(old.Value.StayResidentBox.IsChecked == true && !old.Value.StayResidentBox.IsEnabled);
    }

    [Test]
    public void OldSettings_StartWithWindowsWithoutResident_AreFixedAtStartup()
    {
        var old = new AppSettings { StayResident = false };
        Assert.True(old.EnsureResidentWhenStartingWithWindows(startupRegistered: true), "以前の版の「起動時に起動・常駐しない」は直す（保存が必要）");
        Assert.True(old.StayResident);
        Assert.False(old.EnsureResidentWhenStartingWithWindows(startupRegistered: true), "直した後は何もしない");

        var notRegistered = new AppSettings { StayResident = false };
        Assert.False(notRegistered.EnsureResidentWhenStartingWithWindows(startupRegistered: false), "起動時に起動しないなら、常駐しない設定のまま");
        Assert.False(notRegistered.StayResident);
    }
}
