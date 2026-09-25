using System;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class SearchAndFilterTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0);

    [Test]
    public void Search_SpaceSeparatedTermsAreAnd()
    {
        var entry = new HistoryEntry(@"C:\Projects\Alpha\月次報告.xlsx", Now);
        Assert.True(SearchQuery.Parse("alpha 月次").Matches(entry));
        Assert.True(SearchQuery.Parse("alpha　月次").Matches(entry), "全角スペースでも区切る");
        Assert.False(SearchQuery.Parse("alpha beta").Matches(entry), "どれか 1 語でも外れれば不一致");
    }

    [Test]
    public void Search_TargetsFileNameAndPath_CaseInsensitive()
    {
        var entry = new HistoryEntry(@"C:\Projects\Alpha\Report.docx", Now);
        Assert.True(SearchQuery.Parse("REPORT").Matches(entry));
        Assert.True(SearchQuery.Parse("projects").Matches(entry), "フォルダ部分にも当たる");
        Assert.True(SearchQuery.Parse("   ").Matches(entry), "空の検索語はすべてに一致");
        Assert.True(SearchQuery.Parse("   ").IsEmpty);
    }

    [Test]
    public void Filter_ExtensionPeriodAndKeptOnly()
    {
        var recentXlsx = new HistoryEntry(@"C:\a\x.XLSX", Now.AddHours(-1));
        var oldDocx = new HistoryEntry(@"C:\a\y.docx", Now.AddDays(-40), isKept: true);
        var noExt = new HistoryEntry(@"C:\a\Makefile", Now.AddDays(-2));

        var byExt = new FilterCriteria { Extension = ".xlsx" };
        Assert.True(byExt.Matches(recentXlsx, Now), "拡張子は小文字にそろえて比較");
        Assert.False(byExt.Matches(oldDocx, Now));

        var noExtOnly = new FilterCriteria { Extension = "" };
        Assert.True(noExtOnly.Matches(noExt, Now));
        Assert.False(noExtOnly.Matches(recentXlsx, Now));

        var last30 = new FilterCriteria { Period = PeriodOption.Last30Days };
        Assert.True(last30.Matches(recentXlsx, Now));
        Assert.False(last30.Matches(oldDocx, Now));

        var today = new FilterCriteria { Period = PeriodOption.Today };
        Assert.True(today.Matches(recentXlsx, Now));
        Assert.False(today.Matches(noExt, Now));

        var kept = new FilterCriteria { KeptOnly = true };
        Assert.True(kept.Matches(oldDocx, Now));
        Assert.False(kept.Matches(recentXlsx, Now));
    }
}
