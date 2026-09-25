using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using KeepHistory.Interop;
using KeepHistory.Services;

namespace KeepHistory;

/// <summary>
/// アプリ本体。多重起動ガードがあるため、画面テストではこのクラスを生成しないこと
/// （テストは素の Application に Themes/Shared.xaml だけを読ませる）。
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _guard;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _guard = new SingleInstanceGuard("KeepHistory-" + Environment.UserName);
        if (!_guard.IsFirstInstance)
        {
            // 既に常駐しているプロセスに画面を出させて終わる
            _guard.SignalExistingInstance();
            Shutdown();
            return;
        }

        _controller = new AppController(this);
        _guard.ListenForShowRequests(() => Dispatcher.BeginInvoke(new Action(() => _controller?.ShowMainWindow())));

        var startHidden = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));
        _controller.Start(showWindow: !startHidden);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _controller?.SaveAll();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _guard?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("予期しないエラーが発生しました。", e.Exception);
        MessageBox.Show($"予期しないエラーが発生しました。\n\n{e.Exception.Message}\n\n詳細は error.log を確認してください。",
            "KeepHistory", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
