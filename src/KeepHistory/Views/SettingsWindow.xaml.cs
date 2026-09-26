using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;

namespace KeepHistory.Views;

/// <summary>設定画面。OK なら Result に新しい設定が入る。</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _source;
    private readonly Func<HotkeySetting, bool> _isHotkeyAvailable;
    private readonly Action? _openDeletedHistory;
    private readonly Action? _resetAll;
    private readonly Func<AppSettings, (int Excluded, int Expired)>? _previewRemoval;
    private bool _loading = true;

    /// <param name="isHotkeyAvailable">ホットキーが使えるかの確認（既定は実際に登録を試す。テストで差し替える）。</param>
    /// <param name="openDeletedHistory">「削除した履歴を元に戻す」画面を開く処理。null ならボタンを使えなくする。</param>
    /// <param name="resetAll">「すべて初期状態に戻す」を確認で OK したときの処理（設定・履歴の削除）。null ならボタンを使えなくする。</param>
    /// <param name="previewRemoval">新しい設定で消える履歴の件数（除外・期限切れ）を数える処理。OK の前の確認に使う。</param>
    public SettingsWindow(AppSettings current, Func<HotkeySetting, bool>? isHotkeyAvailable = null,
        Action? openDeletedHistory = null, Action? resetAll = null,
        Func<AppSettings, (int Excluded, int Expired)>? previewRemoval = null)
    {
        _previewRemoval = previewRemoval;
        _isHotkeyAvailable = isHotkeyAvailable ?? GlobalHotkey.IsAvailable;
        _openDeletedHistory = openDeletedHistory;
        _resetAll = resetAll;
        InitializeComponent();
        DeletedHistoryButton.IsEnabled = openDeletedHistory != null;
        ResetAllButton.IsEnabled = resetAll != null;
        Confirm = message => MessageBox.Show(this, message, "KeepHistory の設定",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
        KeyBox.ItemsSource = KeyChoices;
        RetentionBox.ItemsSource = AppSettings.RetentionChoices;
        _source = current.Clone();
        LoadForm(_source);
    }

    /// <summary>確認ダイアログ（OK なら true。テストで差し替える）。</summary>
    internal Func<string, bool> Confirm { get; set; }

    /// <summary>「すべて初期状態に戻す」を確認で OK したか。</summary>
    public bool ResetPerformed { get; private set; }

    /// <summary>「すべて初期状態に戻す」の確認文。</summary>
    public const string ResetConfirmMessage =
        "すべてを初期状態に戻します。次のものを削除し、すぐに反映します。\n\n"
        + "・設定（常駐、保持期間、除外パターン、ホットキー、一覧の列の幅・並び、ウインドウサイズ）\n"
        + "・履歴、キープ、削除した履歴\n\n"
        + "削除したものは元に戻せません。\n"
        + "履歴は、初回起動と同じように Windows の「最近使った項目」から読み直します。\n"
        + "次回の起動時には、常駐するかを確かめる画面が表示されます。\n\n"
        + "よろしいですか？";

    /// <summary>設定の値を画面の入力欄に入れる。</summary>
    private void LoadForm(AppSettings settings)
    {
        _loading = true;

        var retentionDays = AppSettings.SnapRetentionDays(settings.RetentionDays);
        RetentionBox.SelectedItem = AppSettings.RetentionChoices.First(c => c.Value == retentionDays);
        StayResidentBox.IsChecked = settings.StayResident;
        ExcludePatternsBox.Text = string.Join(Environment.NewLine, settings.ExcludePatterns);
        HotkeyEnabledBox.IsChecked = settings.Hotkey.Enabled;
        CtrlBox.IsChecked = settings.Hotkey.Control;
        AltBox.IsChecked = settings.Hotkey.Alt;
        ShiftBox.IsChecked = settings.Hotkey.Shift;
        WinBox.IsChecked = settings.Hotkey.Win;
        KeyBox.SelectedItem = KeyChoices.FirstOrDefault(k => string.Equals(k, settings.Hotkey.Key, StringComparison.OrdinalIgnoreCase)) ?? "H";
        _loading = false;
        UpdateHotkeyStatus();
    }

    /// <summary>
    /// その場で確認し、OK なら設定ファイルを削除する処理を呼んで設定画面を閉じる（入力中の変更は捨てる）。
    /// キャンセルなら何もしない。
    /// </summary>
    internal void ResetAll()
    {
        if (_resetAll == null || !Confirm(ResetConfirmMessage)) return;
        _resetAll();
        ResetPerformed = true;
        Close();
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e) => ResetAll();

    private void OnDeletedHistoryClick(object sender, RoutedEventArgs e) => _openDeletedHistory?.Invoke();

    /// <summary>ホットキーの確認結果。</summary>
    public enum HotkeyCheck
    {
        /// <summary>ホットキーを使わない。</summary>
        Disabled,
        /// <summary>修飾キーが無い（F キー以外）。</summary>
        NeedsModifier,
        /// <summary>ほかのアプリや Windows が使っていて登録できない。</summary>
        Unavailable,
        Available,
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

        if (RetentionBox.SelectedItem is not Choice<int> retention)
        {
            error = "保持期間を選んでください。";
            return false;
        }
        result.RetentionDays = retention.Value;
        result.StayResident = StayResidentBox.IsChecked == true;

        result.ExcludePatterns = ExcludePatternsBox.Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var hotkey = ReadHotkey();
        var (check, message) = CheckHotkey(hotkey);
        if (check is HotkeyCheck.NeedsModifier or HotkeyCheck.Unavailable)
        {
            error = message;
            return false;
        }
        result.Hotkey = hotkey;
        return true;
    }

    private HotkeySetting ReadHotkey() => new()
    {
        Enabled = HotkeyEnabledBox.IsChecked == true,
        Control = CtrlBox.IsChecked == true,
        Alt = AltBox.IsChecked == true,
        Shift = ShiftBox.IsChecked == true,
        Win = WinBox.IsChecked == true,
        Key = KeyBox.SelectedItem as string ?? "H",
    };

    /// <summary>ホットキーが使えるかを確かめ、画面に出す文言を返す。</summary>
    private (HotkeyCheck Check, string Message) CheckHotkey(HotkeySetting hotkey)
    {
        if (!hotkey.Enabled) return (HotkeyCheck.Disabled, string.Empty);
        var isFunctionKey = hotkey.Key.Length > 1 && hotkey.Key.StartsWith('F');
        if (!hotkey.HasModifier && !isFunctionKey)
        {
            return (HotkeyCheck.NeedsModifier, "ホットキーには Ctrl・Alt・Shift・Win のいずれかを組み合わせてください。");
        }
        if (!_isHotkeyAvailable(hotkey))
        {
            return (HotkeyCheck.Unavailable,
                $"✗ {hotkey.ToDisplayString()} はほかのアプリか Windows が使っているため使えません。別の組み合わせにしてください。");
        }
        return (HotkeyCheck.Available, $"✓ {hotkey.ToDisplayString()} は使えます。");
    }

    /// <summary>ホットキーの入力が変わるたびに、使えるかをその場で表示する。</summary>
    internal HotkeyCheck UpdateHotkeyStatus()
    {
        var (check, message) = CheckHotkey(ReadHotkey());
        HotkeyStatusText.Text = message;
        HotkeyStatusText.Visibility = check == HotkeyCheck.Disabled ? Visibility.Collapsed : Visibility.Visible;
        HotkeyStatusText.Foreground = (System.Windows.Media.Brush)FindResource(check == HotkeyCheck.Available ? "SuccessBrush" : "ErrorBrush");
        return check;
    }

    private void OnHotkeyInputChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateHotkeyStatus();
    }

    private void OnResetExcludePatternsClick(object sender, RoutedEventArgs e)
        => ExcludePatternsBox.Text = string.Join(Environment.NewLine, ExclusionFilter.DefaultPatterns);

    /// <summary>新しい設定で履歴が消えるときの確認文。0 件の理由は書かない。</summary>
    public static string BuildRemovalMessage(int excluded, int expired)
    {
        var lines = new System.Collections.Generic.List<string>();
        if (excluded > 0) lines.Add($"・除外パターンに当たる履歴: {excluded} 件");
        if (expired > 0) lines.Add($"・保持期間を過ぎた履歴: {expired} 件");
        return $"この設定にすると、次の履歴 {excluded + expired} 件を一覧から削除します。\n\n"
               + string.Join("\n", lines) + "\n\n"
               + "キープした履歴は残ります。\n"
               + "設定を元に戻すと、「最近使った項目」に残っている分は一覧に戻りますが、回数は 1 からになります。\n\n"
               + "よろしいですか？";
    }

    /// <summary>
    /// 入力を検証し、履歴が消える場合は確認してから確定する。確定したら Result に入れて true
    /// （キャンセル・入力エラーなら false で、設定画面に残る）。
    /// </summary>
    internal bool TryAccept()
    {
        if (!TryBuildResult(out var result, out var error))
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return false;
        }
        if (_previewRemoval != null)
        {
            var (excluded, expired) = _previewRemoval(result);
            if (excluded + expired > 0 && !Confirm(BuildRemovalMessage(excluded, expired))) return false;
        }
        Result = result;
        return true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (TryAccept()) DialogResult = true;
    }
}
