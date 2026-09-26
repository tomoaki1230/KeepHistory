using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

/// <summary>スクリーンリーダー（UI Automation）から、画面の部品が見えて名前で読み上げられるか。</summary>
public sealed class AccessibilityTests
{
    public AccessibilityTests() => UiTestHost.EnsureApplication();

    /// <summary>UI Automation から見える要素（スクリーンリーダーがたどる木）をすべて集める。</summary>
    private static List<AutomationPeer> VisiblePeers(UIElement root)
    {
        var result = new List<AutomationPeer>();
        void Walk(AutomationPeer peer)
        {
            peer.ResetChildrenCache();
            foreach (var child in peer.GetChildren() ?? new List<AutomationPeer>())
            {
                result.Add(child);
                Walk(child);
            }
        }
        Walk(UIElementAutomationPeer.CreatePeerForElement(root));
        return result;
    }

    private static string NameOf(Control control) => UIElementAutomationPeer.CreatePeerForElement(control)?.GetName() ?? string.Empty;

    [Test]
    public void SettingsTabs_ContentIsVisibleToScreenReaders()
    {
        var window = new SettingsWindow(new AppSettings(), _ => true, () => { }, () => { });
        try
        {
            window.Show();
            UiTestHost.DoEvents();
            var ids = VisiblePeers(window).Select(p => p.GetAutomationId()).ToList();
            Assert.True(ids.Contains("StayResidentBox") && ids.Contains("ThemeBox"), "選んでいるタブ（全般）の中身が読み上げの対象になる");

            window.SettingsTabs.SelectedItem = window.HistoryTab;
            UiTestHost.DoEvents();
            ids = VisiblePeers(window).Select(p => p.GetAutomationId()).ToList();
            Assert.True(ids.Contains("RetentionBox") && ids.Contains("ExcludePatternsBox"), "タブを切り替えるとその中身が読み上げの対象になる");
        }
        finally
        {
            window.Close();
        }
    }

    private static readonly AutomationControlType[] OperableTypes =
    {
        AutomationControlType.Button, AutomationControlType.Edit, AutomationControlType.ComboBox,
        AutomationControlType.CheckBox, AutomationControlType.DataGrid, AutomationControlType.List,
    };

    /// <summary>読み上げの対象になっている操作部品のうち、名前の無いもの。</summary>
    private static IEnumerable<string> UnnamedOperables(Window window)
        => VisiblePeers(window)
            .Where(p => p.IsControlElement() && OperableTypes.Contains(p.GetAutomationControlType()) && string.IsNullOrWhiteSpace(p.GetName()))
            .Select(p => $"{window.GetType().Name}: {p.GetAutomationControlType()} id=[{p.GetAutomationId()}] class=[{p.GetClassName()}]");

    [Test]
    public void EveryControl_HasANameToReadAloud()
    {
        var store = new HistoryStore();
        store.Register(@"C:\Docs\a.txt", DateTime.Now, DateTime.Now);
        store.Register(@"C:\Docs\b.txt", DateTime.Now, DateTime.Now);
        store.Remove(new[] { store.Find(@"C:\Docs\b.txt")! }, DateTime.Now);
        var settings = new SettingsWindow(new AppSettings(), _ => true, () => { }, () => { });
        var windows = new Window[]
        {
            new MainWindow(new MainViewModel(store, () => DateTime.Now)) { AllowClose = true },
            settings,
            new DeletedHistoryWindow(store),
            new LicenseWindow(),
            new AboutWindow(),
            new FirstRunWindow(new HotkeySetting()),
        };
        try
        {
            var unnamed = new List<string>();
            var checkedCount = 0;
            foreach (var window in windows)
            {
                window.Show();
                UiTestHost.DoEvents();
                var tabs = window == settings ? settings.SettingsTabs.Items.OfType<TabItem>().ToList() : new List<TabItem> { null! };
                foreach (var tab in tabs)
                {
                    if (tab != null)
                    {
                        settings.SettingsTabs.SelectedItem = tab;
                        UiTestHost.DoEvents();
                    }
                    checkedCount += VisiblePeers(window).Count(p => p.IsControlElement() && OperableTypes.Contains(p.GetAutomationControlType()));
                    unnamed.AddRange(UnnamedOperables(window));
                }
            }
            Assert.True(checkedCount > 30, $"前提: 操作部品を十分に調べた（{checkedCount} 個）");
            Assert.Equal(0, unnamed.Distinct().Count(), "名前の無い部品: " + string.Join(" / ", unnamed.Distinct()));
        }
        finally
        {
            foreach (var window in windows) window.Close();
        }
    }

    [Test]
    public void MainWindow_ControlsAreReadWithMeaningfulNames()
    {
        var store = new HistoryStore();
        store.Register(@"C:\Docs\a.txt", DateTime.Now, DateTime.Now);
        var vm = new MainViewModel(store, () => DateTime.Now);
        var window = new MainWindow(vm) { AllowClose = true };
        try
        {
            window.Show();
            UiTestHost.DoEvents();
            Assert.Equal("更新", NameOf(window.RefreshButton));
            Assert.Equal("キープのみ", NameOf(window.KeptOnlyToggle));
            Assert.Equal("ファイル名・パスで検索", NameOf(window.SearchBox));
            Assert.Equal("種類で絞り込む", NameOf(window.ExtensionBox));
            Assert.Equal("期間で絞り込む", NameOf(window.PeriodBox));
            Assert.Equal("履歴の一覧", NameOf(window.HistoryGrid));

            var row = (DataGridRow)window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(store.Entries[0]);
            var star = Assert.NotNull(FindVisual<Button>(row));
            Assert.Equal("キープする", NameOf(star), "行の☆は記号ではなく、何をするかで読む");
            vm.ToggleKept(new[] { store.Entries[0] });
            UiTestHost.DoEvents();
            row = (DataGridRow)window.HistoryGrid.ItemContainerGenerator.ContainerFromItem(store.Entries[0]);
            Assert.Equal("キープを外す", NameOf(Assert.NotNull(FindVisual<Button>(row))), "キープ済みなら「キープを外す」");
        }
        finally
        {
            window.Close();
        }
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var found = FindVisual<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
