using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using KeepHistory.Models;

namespace KeepHistory.Views;

/// <summary>設定画面。OK なら Result に新しい設定が入る。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _source;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _source = current.Clone();

        KeyBox.ItemsSource = KeyChoices;
        RetentionDaysBox.Text = _source.RetentionDays.ToString(CultureInfo.InvariantCulture);
        ExcludePatternsBox.Text = string.Join(Environment.NewLine, _source.ExcludePatterns);
        HotkeyEnabledBox.IsChecked = _source.Hotkey.Enabled;
        CtrlBox.IsChecked = _source.Hotkey.Control;
        AltBox.IsChecked = _source.Hotkey.Alt;
        ShiftBox.IsChecked = _source.Hotkey.Shift;
        WinBox.IsChecked = _source.Hotkey.Win;
        KeyBox.SelectedItem = KeyChoices.FirstOrDefault(k => string.Equals(k, _source.Hotkey.Key, StringComparison.OrdinalIgnoreCase)) ?? "H";
    }

    /// <summary>ホットキーに使えるキー（System.Windows.Input.Key の名前）。</summary>
    public static IReadOnlyList<string> KeyChoices { get; } =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
            .Concat(Enumerable.Range(1, 12).Select(i => "F" + i.ToString(CultureInfo.InvariantCulture)))
            .ToList();

    public AppSettings? Result { get; private set; }

    /// <summary>入力値を検証して設定を組み立てる。</summary>
    public bool TryBuildResult(out AppSettings result, out string error)
    {
        result = _source.Clone();
        error = string.Empty;

        if (!int.TryParse(RetentionDaysBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            || days < AppSettings.MinRetentionDays || days > AppSettings.MaxRetentionDays)
        {
            error = $"保持期間は {AppSettings.MinRetentionDays}～{AppSettings.MaxRetentionDays} の整数で入力してください。";
            return false;
        }
        result.RetentionDays = days;

        result.ExcludePatterns = ExcludePatternsBox.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var hotkey = new HotkeySetting
        {
            Enabled = HotkeyEnabledBox.IsChecked == true,
            Control = CtrlBox.IsChecked == true,
            Alt = AltBox.IsChecked == true,
            Shift = ShiftBox.IsChecked == true,
            Win = WinBox.IsChecked == true,
            Key = KeyBox.SelectedItem as string ?? "H",
        };
        var isFunctionKey = hotkey.Key.Length > 1 && hotkey.Key.StartsWith('F');
        if (hotkey.Enabled && !hotkey.HasModifier && !isFunctionKey)
        {
            error = "ホットキーには Ctrl・Alt・Shift・Win のいずれかを組み合わせてください。";
            return false;
        }
        result.Hotkey = hotkey;
        return true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out var result, out var error))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Result = result;
        DialogResult = true;
    }
}
