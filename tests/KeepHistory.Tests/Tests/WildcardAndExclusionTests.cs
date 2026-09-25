using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class WildcardAndExclusionTests
{
    [Test]
    public void Wildcard_AsteriskAndQuestion()
    {
        Assert.True(WildcardMatcher.IsMatch("~$*", "~$Book1.xlsx"));
        Assert.True(WildcardMatcher.IsMatch("*.tmp", "ABC.TMP"), "大文字小文字を区別しない");
        Assert.True(WildcardMatcher.IsMatch("a?c.txt", "abc.txt"));
        Assert.True(WildcardMatcher.IsMatch("*", ""));
        Assert.True(WildcardMatcher.IsMatch("*report*2026*", "月次report_2026_09.docx"));
        Assert.False(WildcardMatcher.IsMatch("a?c.txt", "ac.txt"));
        Assert.False(WildcardMatcher.IsMatch("*.tmp", "tmp.txt"));
        Assert.False(WildcardMatcher.IsMatch("~$*", "Book~$1.xlsx"));
    }

    [Test]
    public void Exclusion_DefaultPatternsExcludeOfficeTempAndDownloads()
    {
        var filter = new ExclusionFilter(ExclusionFilter.DefaultPatterns);
        Assert.True(filter.IsExcluded(@"C:\Docs\~$見積書.xlsx"), "Office の一時ファイル");
        Assert.True(filter.IsExcluded(@"C:\Users\u\Downloads\setup.exe.crdownload"), "Chrome のダウンロード途中");
        Assert.True(filter.IsExcluded(@"C:\Users\u\Downloads\movie.mp4.part"), "Firefox のダウンロード途中");
        Assert.True(filter.IsExcluded(@"C:\Work\~WRL0001.tmp"));
        Assert.False(filter.IsExcluded(@"C:\Docs\見積書.xlsx"));
    }

    [Test]
    public void Exclusion_PathPatternMatchesFullPath_NamePatternMatchesFileNameOnly()
    {
        var filter = new ExclusionFilter(new[] { @"C:\Temp\*", "secret*" });
        Assert.True(filter.IsExcluded(@"c:\temp\a.txt"));
        Assert.True(filter.IsExcluded(@"D:\x\Secret-plan.docx"));
        Assert.False(filter.IsExcluded(@"D:\secret\plan.docx"), "名前パターンはフォルダ名に当てない");
    }

    [Test]
    public void Exclusion_IgnoresBlankAndCommentLines()
    {
        var filter = new ExclusionFilter(new[] { "", "   ", "# *.txt", " *.log " });
        Assert.False(filter.IsExcluded(@"C:\a.txt"));
        Assert.True(filter.IsExcluded(@"C:\a.log"));
    }
}
