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
/// </summary>
public sealed class AppController : IDisposable
{
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromMilliseconds(600);

    private readonly Application _app;
    private readonly DataStore _data;
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

    public AppController(Application app)
    {
        _app = app;
        _data = new DataStore(DataStore.DefaultDirectory);
        _scanner = new RecentFolderScanner(RecentFolderScanner.DefaultFolder, new ShellLinkResolver());
        _watcher = new RecentFolderWatcher(_scanner.Folder);
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchDebounce };
        _debounceTimer.Tick += OnDebounceTick;
    }

    public void Start(bool showWindow)
    {
        _settings = _data.LoadSettings();
        ApplySettingsToStore();
        _store.Load(_data.LoadHistory(), _data.LoadDeleted());
        _store.Purge(DateTime.Now);

        _vm = new MainViewModel(_store, () => DateTime.Now);
        _vm.HistoryChanged += (_, _) => SaveHistory();

        _window = new MainWindow(_vm) { Icon = AppIcon.GetImageSource() };
        _window.ApplyLayout(_settings);
        _window.SettingsRequested += (_, _) => OpenSettings();
        _window.HiddenByUser += (_, _) => SaveSettings();

        _tray = new TrayIcon(AppIcon.GetIcon());
        _tray.ShowRequested += (_, _) => ShowMainWindow();
        _tray.SettingsRequested += (_, _) => OpenSettings();
        _tray.ExitRequested += (_, _) => Exit();

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => OnHotkeyPressed();
        _settingsCoordinator = new SettingsCoordinator(_hotkey, ShowSettingsDialog);
        RegisterHotkey(notifyOnFailure: true);

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

        if (showWindow) ShowMainWindow();
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
        SaveAll();
        if (_window != null)
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
            _window.HideToTray();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void RegisterHotkey(bool notifyOnFailure)
    {
        if (_hotkey == null) return;
        var ok = _hotkey.Register(_settings.Hotkey);
        UpdateTrayToolTip();
        if (!ok && notifyOnFailure)
        {
            NotifyHotkeyFailure();
        }
    }

    private void NotifyHotkeyFailure()
    {
        _tray?.ShowBalloon("KeepHistory",
            $"ホットキー {_settings.Hotkey.ToDisplayString()} を登録できませんでした。ほかのアプリが使っている可能性があります。設定から変更してください。",
            warning: true);
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
        var result = _settingsCoordinator.Open(_settings);
        if (result == null)
        {
            UpdateTrayToolTip();
            return;
        }

        _settings = result;
        ApplySettingsToStore();
        var removed = _store.RemoveExcluded();
        var purged = _store.Purge(DateTime.Now);
        SaveSettings();
        if (removed > 0 || purged) SaveHistory();
        _vm?.NotifyStoreChanged();

        UpdateTrayToolTip();
        if (_settings.Hotkey.Enabled && _hotkey?.IsRegistered != true) NotifyHotkeyFailure();
    }

    private AppSettings? ShowSettingsDialog(AppSettings current)
    {
        var dialog = new SettingsWindow(current) { Icon = AppIcon.GetImageSource() };
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

    private void SaveSettings()
    {
        try
        {
            _window?.CaptureLayout(_settings);
            _data.SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("設定を保存できませんでした。", ex);
        }
    }
}
