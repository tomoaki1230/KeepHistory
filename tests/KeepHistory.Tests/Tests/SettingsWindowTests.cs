using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
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
        Assert.Contains("・履歴、キープ、削除した履歴", text, "履歴も消すことを明記する");
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
}
