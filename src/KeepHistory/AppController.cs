using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory;

/// <summary>
/// アプリ全体の組み立てと、走査・監視・保存・常駐の流れを受け持つ。
/// 常駐する設定なら通知領域にアイコンを出し、画面を閉じても隠すだけにする。常駐しない設定なら画面を閉じたら終了する。
/// </summary>
public sealed class AppController : IDisposable
{
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromMilliseconds(600);

    private readonly Application _app;
    private readonly DataStore _data;
    private readonly SettingsStorage _settingsStorage;
    private readonly HistoryStore _store = new();
    private readonly RecentFolderScanner _scanner;
    private readonly RecentFolderWatcher _watcher;
    private readonly DispatcherTimer _debounceTimer;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingLinks = new(StringComparer.OrdinalIgnoreCase);
    private bool _fullRescanPending;
    private bool _scanRunning;

    private AppSettings _settings = new();
    private MainViewModel? _vm;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private GlobalHotkey? _hotkey;
    private SettingsCoordinator? _settingsCoordinator;
    private bool _disposed;
    private bool _exiting;
    private bool _resetRequested;

    public AppController(Application app)
    {
        _app = app;
        _data = new DataStore(DataStore.DefaultDirectory);
        _settingsStorage = new SettingsStorage(_data);
        _scanner = new RecentFolderScanner(RecentFolderScanner.DefaultFolder, new ShellLinkResolver());
        _watcher = new RecentFolderWatcher(_scanner.Folder);
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchDebounce };
        _debounceTimer.Tick += OnDebounceTick;
    }

    /// <param name="startHidden">
    /// 画面を出さずに始める（--tray）。常駐しない設定のときは無視して画面を出す（出さないと操作できなくなるため）。
    /// </param>
    public void Start(bool startHidden)
    {
        var firstRun = !_settingsStorage.Exists;
        _settings = _settingsStorage.Load();
        if (firstRun)
        {
            // 初回起動: 常駐するかを確かめて、すぐに保存する（次回からは聞かない）
            _settings.StayResident = AskStayResident(_settings.Hotkey);
            SaveSettings(confirmed: true);
        }
        ApplySettingsToStore();
        _store.Load(_data.LoadHistory(), _data.LoadDeleted());
        _store.Purge(DateTime.Now);

        _vm = new MainViewModel(_store, () => DateTime.Now);
        _vm.HistoryChanged += (_, _) => SaveHistory();

        _window = new MainWindow(_vm) { Icon = AppIcon.GetImageSource() };
        _window.ApplyLayout(_settings);
        _window.SettingsRequested += (_, _) => OpenSettings();
        _window.HiddenByUser += (_, _) => SaveSettings();
        _window.Closing += (_, e) =>
        {
            // 閉じた後は実寸が取れないので、閉じる直前に列レイアウトとサイズを保存する
            if (!e.Cancel) SaveSettings();
        };
        _window.Closed += (_, _) =>
        {
            // 常駐しない設定で画面を閉じたら終了する
            if (!_exiting) Exit();
        };
        ApplyResidentMode();

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => OnHotkeyPressed();
        _settingsCoordinator = new SettingsCoordinator(_hotkey, ShowSettingsDialog);
        RegisterHotkey();

        RefreshMissing();
        _vm.NotifyStoreChanged();

        // 起動時に一括走査し、以降は FileSystemWatcher で監視する
        _watcher.LinkChanged += OnLinkChanged;
        _watcher.RescanRequired += OnRescanRequired;
        if (!_watcher.Start())
        {
            ErrorLog.Write($"最近使った項目のフォルダが見つかりません: {_scanner.Folder}");
        }
        _ = RunFullScanAsync();

        if (!startHidden || !_settings.StayResident) ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (_window == null || _vm == null) return;
        if (_store.Purge(DateTime.Now)) SaveHistory();
        RefreshMissing();
        _vm.NotifyStoreChanged();
        _window.ShowAndActivate();
    }

    public void SaveAll()
    {
        SaveSettings();
        SaveHistory();
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        SaveAll();
        if (_window != null && _window.IsLoaded)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Stop();
        _watcher.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
    }

    private void OnHotkeyPressed()
    {
        if (_settingsCoordinator?.IsOpen == true || _window == null) return;
        if (_window.IsVisible && _window.IsActive)
        {
            // 常駐するときは隠す。常駐しないときに隠すと操作できなくなるので何もしない
            if (_settings.StayResident) _window.HideToTray();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void RegisterHotkey()
    {
        if (_hotkey == null) return;
        var ok = _hotkey.Register(_settings.Hotkey);
        UpdateTrayToolTip();
        if (!ok) NotifyHotkeyFailure(interactive: false);
    }

    /// <param name="interactive">設定を変えた直後か（常駐しないときはメッセージで知らせる）。</param>
    private void NotifyHotkeyFailure(bool interactive)
    {
        var message = $"ホットキー {_settings.Hotkey.ToDisplayString()} を登録できませんでした。ほかのアプリが使っている可能性があります。設定から変更してください。";
        ErrorLog.Write(message);
        if (_tray != null)
        {
            _tray.ShowBalloon("KeepHistory", message, warning: true);
        }
        else if (interactive)
        {
            MessageBox.Show(message, "KeepHistory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>常駐する／しないの設定を反映する（通知領域のアイコンを出し入れする）。</summary>
    private void ApplyResidentMode()
    {
        if (_window != null) _window.HideOnClose = _settings.StayResident;
        if (_settings.StayResident && _tray == null)
        {
            _tray = new TrayIcon(AppIcon.GetIcon());
            _tray.ShowRequested += (_, _) => ShowMainWindow();
            _tray.SettingsRequested += (_, _) => OpenSettings();
            _tray.ExitRequested += (_, _) => Exit();
            UpdateTrayToolTip();
        }
        else if (!_settings.StayResident && _tray != null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private void UpdateTrayToolTip()
    {
        var text = _settings.Hotkey.Enabled && _hotkey?.IsRegistered == true
            ? $"KeepHistory ({_settings.Hotkey.ToDisplayString()})"
            : "KeepHistory";
        _tray?.SetToolTip(text);
    }

    private void OpenSettings()
    {
        if (_settingsCoordinator == null || _settingsCoordinator.IsOpen) return;
        _window?.CaptureLayout(_settings);
        _resetRequested = false;
        var result = _settingsCoordinator.Open(_settings);
        if (_resetRequested)
        {
            // 設定画面で「すべて初期状態に戻す」を OK した（settings.json は削除済み）
            ApplyDefaultSettings();
            return;
        }
        if (result == null)
        {
            UpdateTrayToolTip();
            return;
        }

        _settings = result;
        ApplySettingsToStore();
        ApplyResidentMode();
        // 常駐をやめたときに画面が隠れていると操作できなくなるので出す
        if (!_settings.StayResident && _window != null && !_window.IsVisible) ShowMainWindow();
        var removed = _store.RemoveExcluded();
        var purged = _store.Purge(DateTime.Now);
        SaveSettings(confirmed: true);
        if (removed > 0 || purged) SaveHistory();
        _vm?.NotifyStoreChanged();

        UpdateTrayToolTip();
        if (_settings.Hotkey.Enabled && _hotkey?.IsRegistered != true) NotifyHotkeyFailure(interactive: true);
    }

    /// <summary>初回起動時の確認。「常駐する」なら true（× で閉じたら常駐しない）。</summary>
    private static bool AskStayResident(HotkeySetting hotkey)
    {
        var dialog = new FirstRunWindow(hotkey) { Icon = AppIcon.GetImageSource() };
        dialog.ShowDialog();
        return dialog.StayResident;
    }

    /// <summary>設定画面から開く「削除した履歴を元に戻す」。戻したら保存して一覧に反映し、.lnk を読み直す。</summary>
    private void OpenDeletedHistory(Window owner)
    {
        var dialog = new DeletedHistoryWindow(_store) { Owner = owner, Icon = AppIcon.GetImageSource() };
        dialog.ShowDialog();
        if (!dialog.RestoredAny) return;
        SaveHistory();
        RefreshMissing();
        _vm?.NotifyStoreChanged();
        // 削除前の状態が分からない（旧版で消した）ものは、.lnk が残っていれば走査で一覧に戻る
        _ = RunFullScanAsync();
    }

    /// <summary>
    /// 設定画面の「すべて初期状態に戻す」を確認で OK した直後に呼ばれる。その場で設定・履歴・キープ・削除した履歴を
    /// すべて消す（ファイルとメモリ上の履歴）。画面を閉じた後に ApplyDefaultSettings で既定値を反映する。
    /// </summary>
    private void ResetAllData()
    {
        try
        {
            AppDataReset.Run(_settingsStorage, _data, _store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("設定・履歴のファイルを削除できませんでした。", ex);
        }
        _vm?.NotifyStoreChanged();
        _resetRequested = true;
    }

    /// <summary>
    /// 既定の設定を、動いているアプリにその場で反映する（常駐・保持期間・除外パターン・ホットキー・列レイアウト・ウインドウサイズ）。
    /// 履歴は ResetAllData で空にしてあるので、初回起動と同じく「最近使った項目」を読み直して作る。
    /// settings.json は作り直さない（次回起動は初回扱い）。
    /// </summary>
    private void ApplyDefaultSettings()
    {
        _settings = new AppSettings();
        _window?.ResetLayout();
        ApplySettingsToStore();
        ApplyResidentMode();
        if (_window != null && !_window.IsVisible) ShowMainWindow();
        _vm?.NotifyStoreChanged();
        // 設定画面を閉じたときは元のホットキーで登録し直されているので、既定のホットキーで登録し直す
        RegisterHotkey();
        _ = RunFullScanAsync();
    }

    private AppSettings? ShowSettingsDialog(AppSettings current)
    {
        SettingsWindow? dialog = null;
        dialog = new SettingsWindow(current,
            openDeletedHistory: () => OpenDeletedHistory(dialog!),
            resetAll: ResetAllData)
        { Icon = AppIcon.GetImageSource() };
        if (_window != null && _window.IsVisible)
        {
            dialog.Owner = _window;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void ApplySettingsToStore()
    {
        _store.RetentionDays = _settings.RetentionDays;
        _store.Exclusion = new ExclusionFilter(_settings.ExcludePatterns);
    }

    private void RefreshMissing()
    {
        // ネットワーク上のファイルは FileExistenceChecker が実在確認を飛ばす
        _store.RefreshMissing(new FileExistenceChecker());
    }

    private async Task RunFullScanAsync()
    {
        if (_scanRunning)
        {
            _fullRescanPending = true;
            return;
        }
        _scanRunning = true;
        try
        {
            var items = await Task.Run(() => _scanner.ScanAll());
            ApplyItems(items);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("最近使った項目の走査に失敗しました。", ex);
        }
        finally
        {
            _scanRunning = false;
        }
        if (_fullRescanPending)
        {
            _fullRescanPending = false;
            _ = RunFullScanAsync();
        }
    }

    // FileSystemWatcher のスレッドから呼ばれる
    private void OnLinkChanged(string lnkPath)
    {
        lock (_pendingLock) _pendingLinks.Add(lnkPath);
        _app.Dispatcher.BeginInvoke(new Action(RestartDebounce));
    }

    // FileSystemWatcher のスレッドから呼ばれる
    private void OnRescanRequired()
    {
        _app.Dispatcher.BeginInvoke(new Action(() => _ = RunFullScanAsync()));
    }

    private void RestartDebounce()
    {
        // .lnk は短時間に何度も書き換わるので、落ち着いてからまとめて読む
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        List<string> links;
        lock (_pendingLock)
        {
            links = _pendingLinks.ToList();
            _pendingLinks.Clear();
        }
        if (links.Count == 0) return;
        try
        {
            var items = await Task.Run(() => links.Select(_scanner.Read).Where(i => i != null).Select(i => i!).ToList());
            ApplyItems(items);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("最近使った項目の読み込みに失敗しました。", ex);
        }
    }

    // UI スレッドで呼ぶこと
    private void ApplyItems(IReadOnlyList<RecentItem> items)
    {
        if (_disposed) return;
        var now = DateTime.Now;
        var changed = false;
        foreach (var item in items)
        {
            changed |= _store.Register(item.TargetPath, item.LastUsed, now);
        }
        changed |= _store.Purge(now);
        if (changed)
        {
            SaveHistory();
            RefreshMissing();
            _vm?.NotifyStoreChanged();
        }
    }

    private void SaveHistory()
    {
        try
        {
            _data.SaveHistory(_store.ToHistoryRecords());
            _data.SaveDeleted(_store.ToDeletedRecords());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("履歴を保存できませんでした。", ex);
        }
    }

    /// <param name="confirmed">利用者が設定を確定したとき true（初期状態に戻した後の自動保存の停止を解く）。</param>
    private void SaveSettings(bool confirmed = false)
    {
        try
        {
            _window?.CaptureLayout(_settings);
            if (confirmed)
            {
                _settingsStorage.SaveConfirmed(_settings);
            }
            else
            {
                _settingsStorage.SaveAuto(_settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("設定を保存できませんでした。", ex);
        }
    }
}
