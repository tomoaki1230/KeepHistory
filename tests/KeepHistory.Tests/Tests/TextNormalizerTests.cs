using System.Linq;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class TextNormalizerTests
{
    [Test]
    public void Normalize_FoldsKanaWidthAndCase()
    {
        Assert.Equal(TextNormalizer.Normalize("ミツモリ"), TextNormalizer.Normalize("みつもり"), "ひらがなとカタカナを区別しない");
        Assert.Equal(TextNormalizer.Normalize("エクセル"), TextNormalizer.Normalize("ｴｸｾﾙ"), "半角カナと全角カナを区別しない");
        Assert.Equal(TextNormalizer.Normalize("ガイド"), TextNormalizer.Normalize("ｶﾞｲﾄﾞ"), "半角カナの濁点もまとめる");
        Assert.Equal(TextNormalizer.Normalize("パブ"), TextNormalizer.Normalize("ぱぶ"), "半濁点・濁点のひらがな");
        Assert.Equal(TextNormalizer.Normalize("report2026"), TextNormalizer.Normalize("ＲＥＰＯＲＴ２０２６"), "全角英数と大文字小文字");
        Assert.NotEqual(TextNormalizer.Normalize("カ"), TextNormalizer.Normalize("ガ"), "濁点の有無は区別する");
    }

    [Test]
    public void FindMatches_ReturnsRangesInOriginalText()
    {
        // 半角カナは正規化で長さが変わるが、強調の位置は元の文字列で返す
        var text = @"C:\資料\ｶﾞｲﾄﾞ_Report.xlsx";
        var ranges = TextNormalizer.FindMatches(text, new[] { TextNormalizer.Normalize("がいど"), TextNormalizer.Normalize("REPORT") });
        Assert.SequenceEqual(new[] { "ｶﾞｲﾄﾞ", "Report" }, ranges.Select(r => text.Substring(r.Start, r.Length)));
    }

    [Test]
    public void FindMatches_MergesOverlapsAndFindsAllOccurrences()
    {
        var text = "abc-abc";
        var ranges = TextNormalizer.FindMatches(text, new[] { "ab", "bc" });
        Assert.SequenceEqual(new[] { (0, 3), (4, 3) }, ranges.Select(r => (r.Start, r.Length)), "重なる一致はまとめ、離れて何度出ても全部返す");
        Assert.SequenceEqual(new[] { (0, 6) }, TextNormalizer.FindMatches("abcabc", new[] { "abc" }).Select(r => (r.Start, r.Length)),
            "隣り合う一致も 1 つにまとめる（続けて太字になるので見た目は同じ）");
        Assert.Equal(0, TextNormalizer.FindMatches(text, new string[0]).Count, "検索語が無ければ強調しない");
        Assert.Equal(0, TextNormalizer.FindMatches("", new[] { "a" }).Count);
    }

    [Test]
    public void Search_UsesNormalizedMatching()
    {
        var entry = new HistoryEntry(@"C:\ﾄﾞｷｭﾒﾝﾄ\ＲＥＰＯＲＴ_みつもり.xlsx", System.DateTime.Now);
        Assert.True(SearchQuery.Parse("ドキュメント report ミツモリ").Matches(entry), "かな・全角半角・大文字小文字を区別せず AND");
        Assert.False(SearchQuery.Parse("どきゅめんと ぎがく").Matches(entry));
    }
}
