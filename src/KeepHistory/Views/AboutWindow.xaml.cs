using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using KeepHistory.Interop;

namespace KeepHistory.Views;

/// <summary>ヘルプ ＞ バージョン情報。</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppImage.Source = AppIcon.GetImageSource();
        VersionText.Text = "バージョン " + AppVersion;
        AuthorText.Text = "作成者: " + Author;
        RuntimeText.Text = RuntimeInformation.FrameworkDescription;
    }

    /// <summary>アプリのバージョン（csproj の Version。例: 1.0.0）。</summary>
    public static string AppVersion
        => (typeof(AboutWindow).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    /// <summary>作成者（csproj の Company。直書きしない）。</summary>
    public static string Author
        => typeof(AboutWindow).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;
}
