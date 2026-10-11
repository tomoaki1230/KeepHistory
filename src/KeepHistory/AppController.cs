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

    /// <summary>読めなかった設定・履歴の読み直しと、止まった監視の始め直しを試す間隔（切断が続く間は空けていく）。</summary>
    private readonly RecoveryBackoff _recoveryBackoff = new();

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

    // 履歴の読み込み・保存（起動時に読めなければ、読めるまで保存しない。3.9）
    private readonly HistoryPersistence _history;
    private RecentSearchPersistence? _searches;

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
        _history = new HistoryPersistence(_data, _store);
        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchDebounce };
        _debounceTimer.Tick += OnDebounceTick;
        _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = _recoveryBackoff.Current };
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
            // 初回起動: Windows の起動時に起動するかを確かめ、すぐに保存する（次回からは聞かない）。
            // 起動するなら常駐もする（起動時に起動して常駐しないと、サインインのたびに画面が開いてしまうため）
            var startWithWindows = AskStartWithWindows(_settings.Hotkey);
            _settings.StayResident = startWithWindows;
            SaveSettings(confirmed: true);
            if (startWithWindows) ApplyStartupRegistration(true);
        }
        ApplySettingsToStore();
        RepairStartupRegistration();
        // 起動時に起動する登録があるのに常駐しない設定（以前の版で選べた）なら、常駐する設定に直す
        if (_settings.EnsureResidentWhenStartingWithWindows(IsStartupRegistered()))
        {
            ErrorLog.Write("Windows の起動時に起動する設定のため、常駐する設定に直しました。");
            SaveSettings();
        }
        _history.LoadAtStartup();
        _store.Purge(DateTime.Now);

        _vm = new MainViewModel(_store, () => DateTime.Now)
        {
            GroupByDate = _settings.GroupByDate,
            HideMissingFiles = _settings.HideMissingFiles,
        };
        // 表示メニューで切り替えたら、設定に入れて保存する
        _vm.PropertyChanged += OnViewOptionChanged;
        _vm.HistoryChanged += (_, _) => SaveHistory();
        _searches = new RecentSearchPersistence(_data, _vm.RecentSearches);
        _searches.Load();
        _vm.RecentSearches.Changed += (_, _) => _searches.Save();

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
        ApplyViewOptions();
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

    /// <summary>初回起動時の確認。「起動時に起動する」なら true（× や Esc で閉じたら起動しない）。</summary>
    private static bool AskStartWithWindows(HotkeySetting hotkey)
    {
        var dialog = new FirstRunWindow(hotkey) { Icon = AppIcon.GetImageSource() };
        dialog.ShowDialog();
        return dialog.StartWithWindows;
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
        var failures = AppDataReset.Run(_settingsStorage, _data, _store, _vm?.RecentSearches);
        _searches?.ForgetLoadFailure();
        // 初期状態に戻したので、読めなかった履歴を後で読み直してまとめることはしない
        _history.ForgetLoadFailure();
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
        ApplyViewOptions();
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
            previewRemoval: s => _store.CountRemovals(new ExclusionFilter(s.ExcludePatterns), s.RetentionDays, DateTime.Now),
            clearRecentSearches: () => _vm?.RecentSearches.Clear(),
            recentSearchCount: _vm?.RecentSearches.Items.Count ?? 0)
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
        // ふだん（直すものが無い）は、覚えている印を見るだけで、ファイルにもネットワークにも触れない
        if (!NeedsRecovery())
        {
            SetRecoveryInterval(reset: true);
            return;
        }
        _recovering = true;
        try
        {
            if (_settingsStorage.LoadFailed) await RecoverSettingsAsync();
            if (_history.LoadFailed) await RecoverHistoryAsync();
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
        // まだ直らなければ、次に試すまでの間隔を空けていく（30 秒 → … → 5 分）。直ったら 30 秒に戻す
        if (!_disposed) SetRecoveryInterval(reset: !NeedsRecovery());
    }

    /// <summary>読み直し・始め直しが必要か（覚えている印だけを見る。ファイルやネットワークには触れない）。</summary>
    private bool NeedsRecovery() => _settingsStorage.LoadFailed || _history.LoadFailed || !_watcher.IsRunning;

    private void SetRecoveryInterval(bool reset)
    {
        if (reset) _recoveryBackoff.Reset();
        else _recoveryBackoff.Failed();
        if (_recoveryTimer.Interval != _recoveryBackoff.Current) _recoveryTimer.Interval = _recoveryBackoff.Current;
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
        ApplyViewOptions();
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
            (history, deleted) = await Task.Run(_history.ReadForRecovery);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        // 待っている間に「すべて初期状態に戻す」をした、なら ApplyRecovered は何もしない
        if (_disposed || !_history.ApplyRecovered(history, deleted, DateTime.Now)) return;
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
        _app.Dispatcher.BeginInvoke(new Action(() =>
        {
            // 監視が新しく止まった（ネットワークの切断など）なら、始め直しを試す間隔を最初（30 秒）に戻す
            if (!_disposed && !_watcher.IsRunning) SetRecoveryInterval(reset: true);
        }));
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

    /// <summary>設定の「表示」の値（日付でまとめる・見つからないファイルを出さない）を一覧に反映する。</summary>
    private void ApplyViewOptions()
    {
        if (_vm != null) ViewOptions.Apply(_settings, _vm);
    }

    /// <summary>表示メニューで切り替えた。設定に入れて保存する（設定と同じ値なら何もしない）。</summary>
    private void OnViewOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm != null && ViewOptions.TryTake(_vm, _settings, e)) SaveSettings();
    }

    private void SaveHistory() => _history.Save();

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
