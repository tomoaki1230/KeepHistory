using System;
using System.Collections.Generic;
using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class SettingsCoordinatorTests
{
    private sealed class FakeHotkey : IHotkeyService
    {
        public bool IsRegistered { get; private set; }
        public List<string> Log { get; } = new();

        public bool Register(HotkeySetting setting)
        {
            IsRegistered = setting.Enabled;
            Log.Add("登録 " + setting.ToDisplayString());
            return true;
        }

        public void Unregister()
        {
            IsRegistered = false;
            Log.Add("解除");
        }
    }

    [Test]
    public void Hotkey_IsUnregisteredWhileSettingsAreOpen_AndRegisteredAfterOk()
    {
        var hotkey = new FakeHotkey();
        hotkey.Register(new HotkeySetting());
        bool? registeredDuringDialog = null;
        var coordinator = new SettingsCoordinator(hotkey, s =>
        {
            registeredDuringDialog = hotkey.IsRegistered;
            s.Hotkey.Key = "J";
            return s;
        });

        var result = coordinator.Open(new AppSettings());

        Assert.Equal(false, registeredDuringDialog, "設定画面を開いている間はホットキーを解除する");
        Assert.True(hotkey.IsRegistered, "閉じたら登録し直す");
        Assert.Equal("登録 Ctrl+Alt+J", hotkey.Log[^1], "新しい設定で登録する");
        Assert.Equal("J", Assert.NotNull(result).Hotkey.Key);
        Assert.False(coordinator.IsOpen);
    }

    [Test]
    public void Hotkey_IsRestoredAfterCancel()
    {
        var hotkey = new FakeHotkey();
        var coordinator = new SettingsCoordinator(hotkey, _ => null);
        Assert.Null(coordinator.Open(new AppSettings()));
        Assert.Equal("登録 Ctrl+Alt+H", hotkey.Log[^1]);
    }

    [Test]
    public void Hotkey_IsRestoredEvenIfDialogThrows()
    {
        var hotkey = new FakeHotkey();
        var coordinator = new SettingsCoordinator(hotkey, _ => throw new InvalidOperationException("画面エラー"));
        Assert.Throws<InvalidOperationException>(() => coordinator.Open(new AppSettings()));
        Assert.True(hotkey.IsRegistered);
        Assert.False(coordinator.IsOpen);
    }

    [Test]
    public void SecondOpenWhileOpenIsIgnored()
    {
        var hotkey = new FakeHotkey();
        SettingsCoordinator? coordinator = null;
        AppSettings? nested = new AppSettings();
        coordinator = new SettingsCoordinator(hotkey, s =>
        {
            nested = coordinator!.Open(s);
            return null;
        });
        coordinator.Open(new AppSettings());
        Assert.Null(nested);
    }

    [Test]
    public void HotkeySetting_NativeModifiers()
    {
        var setting = new HotkeySetting { Control = true, Alt = false, Shift = true, Win = false };
        Assert.Equal(HotkeySetting.ModControl | HotkeySetting.ModShift | HotkeySetting.ModNoRepeat, setting.ToNativeModifiers());
    }
}
