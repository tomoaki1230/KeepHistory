using KeepHistory.Interop;
using KeepHistory.Models;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

/// <summary>実際に Windows へ登録を試して、使えるかを判定できることを確かめる。</summary>
public sealed class HotkeyAvailabilityTests
{
    public HotkeyAvailabilityTests() => UiTestHost.EnsureApplication();

    [Test]
    public void IsAvailable_IsFalseWhileAnotherRegistrationHoldsTheKey()
    {
        // ふだん使われにくい組み合わせで試す
        var setting = new HotkeySetting { Control = true, Alt = true, Shift = true, Key = "F11" };
        Assert.True(GlobalHotkey.IsAvailable(setting), "前提: この組み合わせは空いている");
        Assert.True(GlobalHotkey.IsAvailable(setting), "確かめた後に登録を残さない（2 回目も使える）");

        using (var other = new GlobalHotkey())
        {
            Assert.True(other.Register(setting), "ほかの登録でふさぐ");
            Assert.False(GlobalHotkey.IsAvailable(setting), "ふさがれていたら使えない");
        }

        Assert.True(GlobalHotkey.IsAvailable(setting), "ほかの登録が外れたら使える");
    }
}
