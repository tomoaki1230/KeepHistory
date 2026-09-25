using System;
using KeepHistory.Models;
using KeepHistory.Tests.Framework;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class SettingsWindowTests : IDisposable
{
    private readonly SettingsWindow _window;

    public SettingsWindowTests()
    {
        UiTestHost.EnsureApplication();
        var settings = new AppSettings { RetentionDays = 90 };
        settings.ExcludePatterns = new() { "*.tmp", "~$*" };
        _window = new SettingsWindow(settings);
    }

    public void Dispose() => _window.Close();

    [Test]
    public void LoadsCurrentValues()
    {
        Assert.Equal("90", _window.RetentionDaysBox.Text);
        Assert.Equal("*.tmp" + Environment.NewLine + "~$*", _window.ExcludePatternsBox.Text);
        Assert.Equal("H", _window.KeyBox.SelectedItem);
        Assert.Equal(true, _window.CtrlBox.IsChecked);
    }

    [Test]
    public void BuildsResultFromInput()
    {
        _window.RetentionDaysBox.Text = " 30 ";
        _window.ExcludePatternsBox.Text = "*.bak\r\n\r\n  *.old  \n";
        _window.KeyBox.SelectedItem = "F9";
        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;

        Assert.True(_window.TryBuildResult(out var result, out var error), error);
        Assert.Equal(30, result.RetentionDays);
        Assert.SequenceEqual(new[] { "*.bak", "*.old" }, result.ExcludePatterns);
        Assert.Equal("F9", result.Hotkey.Key);
        Assert.False(result.Hotkey.HasModifier, "ファンクションキーは単独でも可");
    }

    [Test]
    public void RejectsInvalidInput()
    {
        _window.RetentionDaysBox.Text = "abc";
        Assert.False(_window.TryBuildResult(out _, out var error));
        Assert.Contains("保持期間", error);

        _window.RetentionDaysBox.Text = "0";
        Assert.False(_window.TryBuildResult(out _, out _));

        _window.RetentionDaysBox.Text = "365";
        _window.CtrlBox.IsChecked = false;
        _window.AltBox.IsChecked = false;
        _window.KeyBox.SelectedItem = "H";
        Assert.False(_window.TryBuildResult(out _, out error), "文字キー単独のホットキーは不可");
        Assert.Contains("ホットキー", error);
    }
}
