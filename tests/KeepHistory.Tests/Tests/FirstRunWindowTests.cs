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
    public void ExplainsBenefitsOfStayingResident()
    {
        var window = new FirstRunWindow(new HotkeySetting());
        try
        {
            var benefits = window.BenefitList.Items.Cast<string>().ToList();
            Assert.Equal(3, benefits.Count);
            Assert.Contains("ホットキー（Ctrl+Alt+H）", benefits[0], "今のホットキーを具体的に示す");
            Assert.Contains("待たされません", benefits[1]);
            Assert.Contains("「回数」が正確", benefits[2]);
            Assert.Equal("常駐する", window.StayResidentButton.Content);
            Assert.Equal("常駐しない", window.NotResidentButton.Content);
            Assert.True(window.NotResidentButton.IsCancel, "× や Esc は「常駐しない」");
            Assert.False(window.StayResident, "何も選ばなければ常駐しない（既定）");
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
        window.Loaded += (_, _) => window.StayResidentButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        window.ShowDialog();
        Assert.True(window.StayResident, "「常駐する」で true");

        var other = new FirstRunWindow(new HotkeySetting());
        other.Loaded += (_, _) => other.NotResidentButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        other.ShowDialog();
        Assert.False(other.StayResident, "「常駐しない」で false");
    }
}
