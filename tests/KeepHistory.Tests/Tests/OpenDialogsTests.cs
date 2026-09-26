using System;
using System.Windows;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class OpenDialogsTests
{
    public OpenDialogsTests() => UiTestHost.EnsureApplication();

    [Test]
    public void FindTopmost_ReturnsTheLastOpenedDialog_NotTheMainWindow()
    {
        var main = new MainWindow(new MainViewModel(new HistoryStore(), () => DateTime.Now)) { AllowClose = true };
        var settings = new SettingsWindow(new AppSettings(), _ => true);
        var license = new LicenseWindow();
        try
        {
            main.Show();
            Assert.Null(OpenDialogs.FindTopmost(Application.Current.Windows, main), "画面が履歴画面だけなら無し");

            settings.Show();
            license.Show();
            UiTestHost.DoEvents();
            Assert.Same(license, OpenDialogs.FindTopmost(Application.Current.Windows, main), "最後に開いた画面（いちばん手前）");
        }
        finally
        {
            license.Close();
            settings.Close();
            main.Close();
        }
    }

    [Test]
    public void CloseAll_ClosesDialogsButNotTheMainWindow()
    {
        var main = new MainWindow(new MainViewModel(new HistoryStore(), () => DateTime.Now)) { AllowClose = true };
        var settings = new SettingsWindow(new AppSettings(), _ => true);
        var deleted = new DeletedHistoryWindow(new HistoryStore()) { };
        var closed = 0;
        settings.Closed += (_, _) => closed++;
        deleted.Closed += (_, _) => closed++;
        try
        {
            main.Show();
            settings.Show();
            deleted.Owner = settings;
            deleted.Show();
            UiTestHost.DoEvents();

            Assert.True(OpenDialogs.CloseAll(Application.Current.Windows, main), "閉じた画面があれば true");
            UiTestHost.DoEvents();
            Assert.Equal(2, closed, "開いている画面をすべて閉じる（入れ子も）");
            Assert.True(main.IsVisible, "履歴画面は閉じない");
            Assert.False(OpenDialogs.CloseAll(Application.Current.Windows, main), "もう無ければ false");
        }
        finally
        {
            main.Close();
        }
    }
}
