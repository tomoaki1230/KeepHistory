using System;
using System.Collections.Generic;
using System.Linq;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class ExclusionAdderTests
{
    private static readonly DateTime Now = DateTime.Now;

    private static (AppSettings Settings, HistoryStore Store) Setup()
    {
        var settings = new AppSettings();
        var store = new HistoryStore { Exclusion = new ExclusionFilter(settings.ExcludePatterns) };
        store.Register(@"C:\Docs\a.xlsx", Now, Now);
        store.Register(@"C:\Docs\sub\b.docx", Now, Now);
        store.Register(@"C:\Docs2\c.txt", Now, Now);
        store.Register(@"C:\Docs\kept.xlsx", Now, Now);
        store.Find(@"C:\Docs\kept.xlsx")!.IsKept = true;
        return (settings, store);
    }

    [Test]
    public void Patterns_MatchTheIntendedFiles()
    {
        var folder = new ExclusionFilter(new[] { ExclusionPatterns.ForFolder(@"C:\Docs") });
        Assert.True(folder.IsExcluded(@"C:\Docs\a.xlsx"));
        Assert.True(folder.IsExcluded(@"C:\Docs\sub\b.docx"), "サブフォルダも含む");
        Assert.False(folder.IsExcluded(@"C:\Docs2\c.txt"), "名前が似た別のフォルダは含まない");
        Assert.Equal(@"C:\*", ExclusionPatterns.ForFolder(@"C:\"), "ドライブ直下");

        var file = new ExclusionFilter(new[] { ExclusionPatterns.ForFile(@"C:\Docs\a.xlsx") });
        Assert.True(file.IsExcluded(@"C:\Docs\a.xlsx"));
        Assert.False(file.IsExcluded(@"C:\Other\a.xlsx"), "同じ名前の別のファイルは含まない");

        Assert.Equal("*.xlsx", ExclusionPatterns.ForExtension(".xlsx"));
    }

    [Test]
    public void TryAdd_Confirmed_AddsPatternAndRemovesNonKeptEntries()
    {
        var (settings, store) = Setup();
        string? asked = null;
        var added = ExclusionAdder.TryAdd(settings, store, @"C:\Docs\*", "フォルダ「C:\\Docs」", m =>
        {
            asked = m;
            return true;
        });

        Assert.True(added);
        Assert.Contains("2 件を一覧から削除します", Assert.NotNull(asked), "消える件数を示す（キープは数えない）");
        Assert.Contains("キープした履歴は残ります", asked);
        Assert.True(settings.ExcludePatterns.Contains(@"C:\Docs\*"), "設定に追加する");
        Assert.SequenceEqual(new[] { @"C:\Docs\kept.xlsx", @"C:\Docs2\c.txt" }, store.Entries.Select(e => e.Path).OrderBy(p => p));
        Assert.False(store.Register(@"C:\Docs\new.txt", Now, Now), "以後は記録しない");
    }

    [Test]
    public void TryAdd_Cancelled_ChangesNothing()
    {
        var (settings, store) = Setup();
        var before = settings.ExcludePatterns.Count;
        Assert.False(ExclusionAdder.TryAdd(settings, store, "*.xlsx", "拡張子「.xlsx」", _ => false));
        Assert.Equal(before, settings.ExcludePatterns.Count);
        Assert.Equal(4, store.Entries.Count);
    }

    [Test]
    public void TryAdd_ExistingPattern_DoesNotAskAgain()
    {
        var (settings, store) = Setup();
        var asked = 0;
        Assert.False(ExclusionAdder.TryAdd(settings, store, "*.TMP", "拡張子", _ =>
        {
            asked++;
            return true;
        }), "既定の *.tmp と同じ（大文字小文字は区別しない）");
        Assert.Equal(0, asked);
    }
}
