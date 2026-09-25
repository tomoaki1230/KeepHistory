using System;
using System.Windows;
using System.Windows.Threading;

namespace KeepHistory.Tests.Framework;

/// <summary>
/// 画面テストの土台。製品の App は生成しない（App.OnStartup の多重起動ガードでテストが殺されるため）。
/// 素の Application を作り、共有リソース（Themes/Shared.xaml）だけを読ませる。
/// </summary>
public static class UiTestHost
{
    public const string SharedResourceUri = "pack://application:,,,/KeepHistory;component/Themes/Shared.xaml";

    private static bool _resourcesLoaded;

    public static Application EnsureApplication()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!_resourcesLoaded)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(SharedResourceUri, UriKind.Absolute) });
            _resourcesLoaded = true;
        }
        return app;
    }

    /// <summary>ディスパッチャーに溜まった処理（レイアウト・バインディング）を流す。</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
