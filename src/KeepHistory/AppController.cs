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

    /// <summary>読めなかった設定・履歴の読み直しと、止まった監視の始め直しを試す間隔。</summary>
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(30);

    private readonly Application _app;
    private readonly DataStore _data;
    private readonly SettingsStorage _settingsStorage;
    private readonly IStartupRegistration _startup = new RunKeyStartupRegistration();
    private readonly HistoryStore _store = new();
    private readonly RecentFolderScanner _scanner;
    private readonly RecentFolderWatcher _watcher;
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _recoveryTimer;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingLinks = new(StringComparer.OrdinalIgnoreCase);
    private bool _fullRescanPending;
    private bool _scanRunning;

    // 起動時に history.json / deleted.json を一時的に読めなかった（ネットワークの切断など）。
    // 読めるようになるまで保存しない（空に近い内容で上書きすると、保存してあった履歴・キープが消えるため）
    private bool _historyLoadFailed;
    private bool _recovering;

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
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RecoveryInterval };
        _recoveryTimer.Tick += OnRecoveryTick;
    }

    /// <param name="startHidden">
    /// 画面を出さずに始める（--tray）。常駐しない設定のときは無視して画面を出す（出さないと操作できなくなるため）。
    /// </param>
    public void Start(bool startHidden)
    {
        // 「settings.json が無い（初回）」と「一時的に読めない」を区別する。読めないときは既定値で動き、読めるまで保存しない
        var settingsLoad = _settingsStorage.TryLoad(out _settings);
        var firstRun = settingsLoad == SettingsLoadResult.NotFound;
        if (settingsLoad == SettingsLoadResult.Unavailable)
        {
            ErrorLog.Write("settings.json を一時的に読み込めないため、既定の設定で起動します。読み込めるまで設定は保存しません。");
        }
        // 最初の画面（初回確認を含む）から設定の色で出す
        ThemeManager.Apply(_app, _settings.Theme);
        if (firstRun)
        {
            // 初回起動: 常駐するかを確かめて、すぐに保存する（次回からは聞かない）
            _settings.StayResident = AskStayResident(_settings.Hotkey);
            SaveSettings(confirmed: true);
        }
        ApplySettingsToStore();
        RepairStartupRegistration();
        LoadHistoryAtStartup();
        _store.Purge(DateTime.Now);

        _vm = new MainViewModel(_store, () => DateTime.Now);
        _vm.HistoryChanged += (_, _) => SaveHistory();

        _window = new MainWindow(_vm) { Icon = AppIcon.GetImageSource() };
        _window.ApplyLayout(_settings);
        _window.SettingsRequested += (_, _) => OpenSettings();
        _window.RefreshRequested += (_, _) => ForceRefresh();
        _window.ExcludeRequested += (_, e) => AddExclusion(e.Pattern, e.Description);
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
        _ = StartWatcherAsync();
        _ = RunFullScanAsync();
        _recoveryTimer.Start();

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

    /// <summary>
    /// 強制リフレッシュ（「↻ 更新」）。期限切れを消し、ファイルが見つかるかを確かめ直して一覧を作り直し、
    /// 「最近使った項目」を全体走査し直す。走査中なら終わった後にもう一度走査する（連打しても二重には走らない）。
    /// </summary>
    public void ForceRefresh()
    {
        if (_store.Purge(DateTime.Now)) SaveHistory();
        RefreshMissing();
        _vm?.NotifyStoreChanged();
        _ = RunFullScanAsync();
    }

    /// <summary>一覧の右クリック「記録しない」。確認して除外パターンを追加し、設定と履歴を保存する。</summary>
    private void AddExclusion(string pattern, string description)
    {
        var added = ExclusionAdder.TryAdd(_settings, _store, pattern, description,
            message => MessageBox.Show(_window!, message, "記録しない", MessageBoxButton.OKCancel,
                MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK);
        if (!added) return;
        SaveSettings(confirmed: true);
        SaveHistory();
        _vm?.NotifyStoreChanged();
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

    /// <summary>
    /// 通知領域の「終了」。設定画面などが開いていれば先に閉じ、その画面の処理が終わってから終了する
    /// （開いた画面の途中で終了すると、後片付けの順序が乱れるため）。
    /// </summary>
    private void RequestExit()
    {
        if (OpenDialogs.CloseAll(_app.Windows, _window))
        {
            _app.Dispatcher.BeginInvoke(new Action(Exit), DispatcherPriority.Background);
            return;
        }
        Exit();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Stop();
        _recoveryTimer.Stop();
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
            _tray.ExitRequested += (_, _) => RequestExit();
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
        if (_settingsCoordinator == null) return;
        if (_settingsCoordinator.IsOpen)
        {
            // すでに開いている（ほかのウインドウの裏に隠れていることがある）ので、いちばん手前の画面を前面に出す
            OpenDialogs.FindTopmost(_app.Windows, _window)?.Activate();
            return;
        }
        if (_settingsStorage.LoadFailed && !ConfirmEditWithoutSavedSettings()) return;
        _window?.CaptureLayout(_settings);
        _settings.StartWithWindows = IsStartupRegistered();
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

        var startupChanged = result.StartWithWindows != _settings.StartWithWindows;
        _settings = result;
        if (_window != null) _window.Placement = _settings.Placement;
        ThemeManager.Apply(_app, _settings.Theme);
        ApplySettingsToStore();
        ApplyResidentMode();
        if (startupChanged) ApplyStartupRegistration(_settings.StartWithWindows);
        // 常駐をやめたときに画面が隠れていると操作できなくなるので出す
        if (!_settings.StayResident && _window != null && !_window.IsVisible) ShowMainWindow();
        var removed = _store.RemoveExcluded();
        var purged = _store.Purge(DateTime.Now);
        SaveSettings(confirmed: true);
        if (removed > 0 || purged) SaveHistory();
        _vm?.NotifyStoreChanged();
        // 除外パターンを外した・保持期間を延ばした場合に、「最近使った項目」に残っている分を一覧に戻す
        _ = RunFullScanAsync();

        UpdateTrayToolTip();
        if (_settings.Hotkey.Enabled && _hotkey?.IsRegistered != true) NotifyHotkeyFailure(interactive: true);
    }

    /// <summary>
    /// 保存してある設定を読めない（ネットワークの切断など）まま設定画面を開くときの確認。
    /// 画面には既定の設定が出て、OK すると保存してある設定を上書きするため。
    /// </summary>
    private bool ConfirmEditWithoutSavedSettings()
    {
        const string message = "保存してある設定（settings.json）を今は読み込めないため、設定画面には既定の設定を表示します"
                               + "（ネットワークが一時的に切断されている可能性があります）。\n\n"
                               + "設定画面で OK を押すと、保存してある設定をその内容で上書きします。\n"
                               + "読み込めるようになると、自動で保存してある設定に切り替わります。\n\nこのまま設定画面を開きますか？";
        var owner = _window != null && _window.IsVisible ? _window : null;
        var result = owner != null
            ? MessageBox.Show(owner, message, "KeepHistory", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            : MessageBox.Show(message, "KeepHistory", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return result == MessageBoxResult.OK;
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
        var failures = AppDataReset.Run(_settingsStorage, _data, _store);
        // 初期状態に戻したので、読めなかった履歴を後で読み直してまとめることはしない
        _historyLoadFailed = false;
        _vm?.NotifyStoreChanged();
        _resetRequested = true;
        if (failures.Count > 0)
        {
            MessageBox.Show(
                "次のファイルを削除できませんでした（ほかのアプリが使っている可能性があります）。\n\n"
                + string.Join("\n", failures.Select(f => "・" + f)) + "\n\n"
                + "画面は初期状態で動き、次に保存するときに初期状態の内容で上書きします。詳細は error.log を確認してください。",
                "KeepHistory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        if (_window != null) _window.Placement = _settings.Placement;
        ThemeManager.Apply(_app, _settings.Theme);
        ApplyStartupRegistration(false);
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
            resetAll: ResetAllData,
            previewRemoval: s => _store.CountRemovals(new ExclusionFilter(s.ExcludePatterns), s.RetentionDays, DateTime.Now))
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

    private bool IsStartupRegistered()
    {
        try
        {
            return _startup.GetCommand() != null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("スタートアップの登録を読めませんでした。", ex);
            return false;
        }
    }

    /// <summary>Windows の起動時に起動する登録・解除。失敗したら知らせる。</summary>
    private void ApplyStartupRegistration(bool enable)
    {
        try
        {
            if (!enable)
            {
                _startup.Disable();
                return;
            }
            var exe = StartupCommand.ExecutablePath(Environment.ProcessPath, AppContext.BaseDirectory)
                      ?? throw new IOException("KeepHistory.exe の場所が分かりません。");
            _startup.Enable(StartupCommand.Build(exe));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("スタートアップの登録を変更できませんでした。", ex);
            MessageBox.Show($"Windows の起動時に起動する設定を変更できませんでした。\n\n{ex.Message}",
                "KeepHistory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 登録されている exe が無くなっていたら（exe を移動した）、今の exe で登録し直す。
    /// 別の場所の KeepHistory が登録されていて、その exe があるなら触らない。ネットワーク上の exe は確かめない（StartupCommand.NeedsRepair）。
    /// </summary>
    private void RepairStartupRegistration()
    {
        try
        {
            var registered = StartupCommand.ExtractExePath(_startup.GetCommand());
            if (!StartupCommand.NeedsRepair(registered, new FileExistenceChecker().IsNetworkPath, File.Exists)) return;
            var exe = StartupCommand.ExecutablePath(Environment.ProcessPath, AppContext.BaseDirectory);
            if (exe != null) _startup.Enable(StartupCommand.Build(exe));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("スタートアップの登録を確かめられませんでした。", ex);
        }
    }

    private void ApplySettingsToStore()
    {
        _store.RetentionDays = _settings.RetentionDays;
        _store.Exclusion = new ExclusionFilter(_settings.ExcludePatterns);
    }

    /// <summary>
    /// 起動時に履歴を読む。一時的に読めなければ空で始め、読めるまで保存しない（OnRecoveryTick で読み直してまとめる）。
    /// history.json と deleted.json は両方読めたときだけ使う（片方だけだと、後でまとめるときに重なる）。
    /// </summary>
    private void LoadHistoryAtStartup()
    {
        try
        {
            var history = _data.LoadHistory();
            var deleted = _data.LoadDeleted();
            _store.Load(history, deleted);
        }
        catch (DataUnavailableException ex)
        {
            _historyLoadFailed = true;
            ErrorLog.Write("履歴を一時的に読み込めないため、空の状態で起動します。読み込めるまで履歴は保存しません。", ex);
        }
    }

    /// <summary>監視を始める。フォルダがネットワーク上だと確かめるのに時間がかかることがあるので、バックグラウンドで。</summary>
    private async Task StartWatcherAsync()
    {
        if (!await Task.Run(_watcher.Start))
        {
            ErrorLog.Write($"最近使った項目のフォルダを監視できません。あとで始め直します: {_scanner.Folder}");
        }
    }

    /// <summary>
    /// 一定の間隔で、一時的に読めなかった設定・履歴を読み直し、止まった監視を始め直す（ネットワークの一時的な切断から戻す）。
    /// 読み直しはバックグラウンドで行い（切断中は応答を待つため）、反映は UI スレッドで行う。
    /// </summary>
    private async void OnRecoveryTick(object? sender, EventArgs e)
    {
        if (_recovering || _disposed) return;
        _recovering = true;
        try
        {
            if (_settingsStorage.LoadFailed) await RecoverSettingsAsync();
            if (_historyLoadFailed) await RecoverHistoryAsync();
            if (!_watcher.IsRunning && await Task.Run(_watcher.Restart) && !_disposed)
            {
                ErrorLog.Write("最近使った項目の監視を始め直しました。");
                // 止まっていた間の .lnk を取り込む
                _ = RunFullScanAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("一時的に読めなかったデータの読み直しに失敗しました。", ex);
        }
        finally
        {
            _recovering = false;
        }
    }

    private async Task RecoverSettingsAsync()
    {
        AppSettings? loaded;
        try
        {
            loaded = await Task.Run(_settingsStorage.Reload);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        // 待っている間に利用者が設定を確定した・設定画面を開いている、なら今回は反映しない
        if (_disposed || !_settingsStorage.LoadFailed || _settingsCoordinator?.IsOpen == true) return;
        _settingsStorage.MarkRecovered();
        ErrorLog.Write("settings.json を読み込めるようになりました。保存してあった設定に切り替えます。");
        if (loaded == null) return;

        _settings = loaded;
        _window?.ApplyLayout(_settings);
        ThemeManager.Apply(_app, _settings.Theme);
        ApplySettingsToStore();
        ApplyResidentMode();
        // 常駐しない設定に変わったときに画面が隠れていると操作できなくなるので出す
        if (!_settings.StayResident && _window != null && !_window.IsVisible) ShowMainWindow();
        RegisterHotkey();
        var removed = _store.RemoveExcluded();
        var purged = _store.Purge(DateTime.Now);
        if (removed > 0 || purged) SaveHistory();
        _vm?.NotifyStoreChanged();
    }

    private async Task RecoverHistoryAsync()
    {
        List<HistoryRecord> history;
        List<DeletedRecord> deleted;
        try
        {
            (history, deleted) = await Task.Run(() => (_data.LoadHistory(), _data.LoadDeleted()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        // 待っている間に「すべて初期状態に戻す」をした、なら読んだ内容は使わない
        if (_disposed || !_historyLoadFailed) return;
        _store.MergeLoaded(history, deleted);
        _historyLoadFailed = false;
        ErrorLog.Write("履歴を読み込めるようになりました。この起動中の記録とまとめて保存します。");
        _store.RemoveExcluded();
        _store.Purge(DateTime.Now);
        SaveHistory();
        RefreshMissing();
        _vm?.NotifyStoreChanged();
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
        // 起動時に読めなかった履歴は、読めるまで上書きしない（OnRecoveryTick で読み直してまとめてから保存する）
        if (_historyLoadFailed) return;
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
