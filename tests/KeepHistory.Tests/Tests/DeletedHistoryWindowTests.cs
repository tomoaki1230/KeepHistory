using System;
using System.Linq;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

public sealed class DeletedHistoryWindowTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0);
    private readonly HistoryStore _store = new();
    private readonly DeletedHistoryWindow _window;

    public DeletedHistoryWindowTests()
    {
        UiTestHost.EnsureApplication();
        _store.Register(@"C:\Docs\a.txt", Now.AddHours(-3), Now);
        _store.Register(@"C:\Docs\b.txt", Now.AddHours(-2), Now);
        _store.Register(@"C:\Docs\c.txt", Now.AddHours(-1), Now);
        _store.Remove(new[] { _store.Find(@"C:\Docs\a.txt")! }, Now.AddMinutes(1));
        _store.Remove(new[] { _store.Find(@"C:\Docs\b.txt")! }, Now.AddMinutes(2));
        _window = new DeletedHistoryWindow(_store);
    }

    public void Dispose() => _window.Close();

    [Test]
    public void ListsDeletedHistory_NewestFirst()
    {
        var items = _window.DeletedGrid.Items.Cast<DeletedHistory>().Select(d => d.FileName);
        Assert.SequenceEqual(new[] { "b.txt", "a.txt" }, items, "外した履歴を、新しく外した順に並べる");
        Assert.False(_window.RestoreButton.IsEnabled, "選ぶまでは押せない");
        Assert.Equal(System.Windows.Visibility.Collapsed, _window.EmptyText.Visibility);
        Assert.Equal("外した履歴を戻す", _window.Title, "「削除」ではなく「外す」と言う（ファイルが消えたと誤解させない）");
        Assert.Equal("外した日時", _window.DeletedGrid.Columns.Last().Header as string);
        Assert.Equal("外した履歴はありません。", _window.EmptyText.Text);
    }

    [Test]
    public void RestoreSelected_ReturnsToHistoryImmediately()
    {
        _window.DeletedGrid.SelectedItems.Add(_window.DeletedGrid.Items.Cast<DeletedHistory>().First(d => d.FileName == "a.txt"));
        Assert.True(_window.RestoreButton.IsEnabled);

        Assert.Equal(1, _window.RestoreSelected());
        Assert.NotNull(_store.Find(@"C:\Docs\a.txt"), "履歴に戻る");
        Assert.True(_window.RestoredAny, "閉じた後に保存・再表示するための印");
        Assert.SequenceEqual(new[] { "b.txt" }, _window.DeletedGrid.Items.Cast<DeletedHistory>().Select(d => d.FileName), "一覧から消える");
        Assert.Contains("1 件を元に戻しました", _window.ResultText.Text);

        _window.DeletedGrid.SelectAll();
        _window.RestoreSelected();
        Assert.Equal(System.Windows.Visibility.Visible, _window.EmptyText.Visibility, "無くなったら「ありません」と出す");
        Assert.Equal(3, _store.Entries.Count);
    }
}
