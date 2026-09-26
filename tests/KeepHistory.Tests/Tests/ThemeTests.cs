using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly Func<bool> _originalSystem = ThemeManager.SystemUsesLightTheme;

    public ThemeTests() => UiTestHost.EnsureApplication();

    public void Dispose()
    {
        ThemeManager.SystemUsesLightTheme = _originalSystem;
        ThemeManager.SystemUsesLightTheme = () => true;
        ThemeManager.Apply(Application.Current, AppTheme.Light);
        ThemeManager.SystemUsesLightTheme = _originalSystem;
    }

    private static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    [Test]
    public void ResolveIsDark_FollowsSettingAndWindows()
    {
        Assert.True(ThemeManager.ResolveIsDark(AppTheme.Dark, systemUsesLight: true));
        Assert.False(ThemeManager.ResolveIsDark(AppTheme.Light, systemUsesLight: false));
        Assert.True(ThemeManager.ResolveIsDark(AppTheme.System, systemUsesLight: false), "Windows がダークならダーク");
        Assert.False(ThemeManager.ResolveIsDark(AppTheme.System, systemUsesLight: true));
    }

    [Test]
    public void Apply_SwitchesColors_AndOpenWindowsFollow()
    {
        var window = new MainWindow(new MainViewModel(new HistoryStore(), () => DateTime.Now)) { AllowClose = true };
        try
        {
            window.Show();
            var lightBackground = ColorOf("WindowBackgroundBrush");

            ThemeManager.SystemUsesLightTheme = () => true;
            ThemeManager.Apply(Application.Current, AppTheme.Dark);
            Assert.True(ThemeManager.IsDark);
            var dark = ColorOf("WindowBackgroundBrush");
            Assert.NotEqual(lightBackground, dark, "色が入れ替わる");
            Assert.True(dark.R < 0x40 && dark.G < 0x40 && dark.B < 0x40, "ダークは暗い背景");
            Assert.Equal(dark, ((SolidColorBrush)window.Background).Color, "開いている画面も追従する");
            Assert.True(((SolidColorBrush)window.Foreground).Color.R > 0xC0, "文字は明るい色");

            ThemeManager.Apply(Application.Current, AppTheme.Light);
            Assert.False(ThemeManager.IsDark);
            Assert.Equal(lightBackground, ((SolidColorBrush)window.Background).Color, "ライトに戻せる");

            ThemeManager.SystemUsesLightTheme = () => false;
            ThemeManager.Apply(Application.Current, AppTheme.System);
            Assert.True(ThemeManager.IsDark, "「Windows に合わせる」で Windows がダークならダーク");
            Assert.Equal(1, Application.Current.Resources.MergedDictionaries.Count(d =>
                d.Source?.OriginalString.Contains("Colors.") == true), "色のファイルは 1 つだけ（入れ替えで増えない）");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void LightAndDark_DefineTheSameColors()
    {
        var light = new ResourceDictionary { Source = new Uri(ThemeManager.LightUri) };
        var dark = new ResourceDictionary { Source = new Uri(ThemeManager.DarkUri) };
        var lightKeys = light.Keys.OfType<string>().OrderBy(k => k).ToList();
        var darkKeys = dark.Keys.OfType<string>().OrderBy(k => k).ToList();
        Assert.SequenceEqual(lightKeys, darkKeys, "片方にしか無い色があると、切り替えたときにその部分だけ色が付かない");
        Assert.True(lightKeys.Contains("HighlightTextBrush"));
    }
}
