using KeepHistory.Tests.Framework;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class LicenseTests
{
    public LicenseTests() => UiTestHost.EnsureApplication();

    [Test]
    public void Notices_ListAllThirdPartyComponentsWithFullMitText()
    {
        var text = LicenseWindow.LoadNotices();
        // 一覧の各項目は「名前」と「入手先 URL」の 2 行。見出しの名前と取り違えないよう組で確かめる
        foreach (var (name, url) in new[]
                 {
                     (".NET Runtime（System.Text.Json を含む）", "https://github.com/dotnet/runtime"),
                     ("Windows Presentation Foundation (WPF)", "https://github.com/dotnet/wpf"),
                     ("Windows Forms", "https://github.com/dotnet/winforms"),
                 })
        {
            Assert.Contains($"- {name}\n  {url}", text.Replace("\r\n", "\n"), "使用しているソフトウェアを入手先とともに列挙する");
        }
        Assert.Contains("Copyright (c) .NET Foundation and Contributors", text, "著作権表示");
        Assert.Contains("The above copyright notice and this permission notice shall be included in all", text, "MIT の許諾文");
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND", text, "MIT の免責文");
        Assert.Contains("サードパーティー", text, "UTF-8 の日本語が文字化けしない");
    }
}
