using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using KeepHistory.Models;
using KeepHistory.Tests.Framework;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class FirstRunWindowTests
{
    public FirstRunWindowTests() => UiTestHost.EnsureApplication();

    [Test]
    public void AsksWhetherToStartWithWindows_AndExplainsTheBenefits()
    {
        var window = new FirstRunWindow(new HotkeySetting());
        try
        {
            var texts = System.Windows.LogicalTreeHelper.GetChildren((DependencyObject)window.Content)
                .OfType<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal("Windows の起動時に KeepHistory を起動しますか？", texts[0], "常駐ではなく、起動時に起動するかを聞く");
            Assert.True(texts.Any(t => t.Contains("通知領域に常駐")), "起動するなら常駐もすることを伝える");
            var benefits = window.BenefitList.Items.Cast<string>().ToList();
            Assert.Equal(3, benefits.Count);
            Assert.Contains("サインインした直後から、ホットキー（Ctrl+Alt+H）", benefits[0], "今のホットキーを具体的に示す");
            Assert.Contains("「回数」が正確", benefits[1]);
            Assert.Contains("待たされません", benefits[2]);
            Assert.Equal("起動時に起動する（おすすめ）", window.StartWithWindowsButton.Content);
            Assert.True(window.StartWithWindowsButton.IsDefault, "Enter は「起動時に起動する」");
            Assert.Equal("起動しない", window.DoNotStartButton.Content);
            Assert.True(window.DoNotStartButton.IsCancel, "× や Esc は「起動しない」");
            Assert.False(window.StartWithWindows, "何も選ばなければ起動しない");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Buttons_SetChoice()
    {
        var window = new FirstRunWindow(new HotkeySetting());
        window.Loaded += (_, _) => window.StartWithWindowsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        window.ShowDialog();
        Assert.True(window.StartWithWindows, "「起動時に起動する」で true");

        var other = new FirstRunWindow(new HotkeySetting());
        other.Loaded += (_, _) => other.DoNotStartButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        other.ShowDialog();
        Assert.False(other.StartWithWindows, "「起動しない」で false");
    }
}
