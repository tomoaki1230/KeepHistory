using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KeepHistory.Models;
using Microsoft.Win32;

namespace KeepHistory.Interop;

/// <summary>
/// 画面の色（ライト／ダーク）の切り替え。Application のリソースにある色のファイル（Themes/Colors.*.xaml）を入れ替え、
/// 各ウインドウのタイトルバーもそろえる。「Windows に合わせる」なら、Windows の設定（アプリのモード）が変わったら追従する。
/// </summary>
public static class ThemeManager
{
    public const string LightUri = "pack://application:,,,/KeepHistory;component/Themes/Colors.Light.xaml";
    public const string DarkUri = "pack://application:,,,/KeepHistory;component/Themes/Colors.Dark.xaml";

    private static Application? _app;
    private static AppTheme _setting = AppTheme.System;

    /// <summary>いまダークか。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Windows の設定（アプリのモード）がライトか（テストで差し替える）。</summary>
    internal static Func<bool> SystemUsesLightTheme { get; set; } = ReadSystemUsesLightTheme;

    public static bool ResolveIsDark(AppTheme setting, bool systemUsesLight)
        => setting == AppTheme.Dark || (setting == AppTheme.System && !systemUsesLight);

    /// <summary>設定に合わせて色を切り替える。</summary>
    public static void Apply(Application app, AppTheme setting)
    {
        EnsureInitialized(app);
        _setting = setting;
        IsDark = ResolveIsDark(setting, SystemUsesLightTheme());

        var uri = new Uri(IsDark ? DarkUri : LightUri, UriKind.Absolute);
        var dictionaries = app.Resources.MergedDictionaries;
        int index = -1;
        for (int i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        var colors = new ResourceDictionary { Source = uri };
        if (index < 0) dictionaries.Insert(0, colors);
        else if (!string.Equals(dictionaries[index].Source?.OriginalString, uri.OriginalString, StringComparison.OrdinalIgnoreCase)) dictionaries[index] = colors;

        foreach (Window window in app.Windows) ApplyTitleBar(window);
    }

    private static void EnsureInitialized(Application app)
    {
        if (_app != null) return;
        _app = app;
        // これから開くウインドウのタイトルバーも、表示されたときにそろえる
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => ApplyTitleBar((Window)sender)));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_setting == AppTheme.System) Apply(app, _setting);
            }));
        };
    }

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = IsDark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE（Windows 10 20H1 以降は 20、それより前は 19）
        if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
        }
    }

    private static bool ReadSystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
