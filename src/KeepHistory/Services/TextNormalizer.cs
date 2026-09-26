using System.Collections.Generic;
using System.Text;

namespace KeepHistory.Services;

/// <summary>検索で一致した部分（元の文字列での位置）。</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>
/// 検索のための文字の正規化。ひらがな／カタカナ、全角／半角（半角カナの濁点・半濁点を含む）、大文字／小文字を区別しない。
/// 正規化で長さが変わる（例: 半角「ｶﾞ」2 文字 → 「ガ」1 文字）ので、正規化後の各文字が元の文字列のどこから来たかも持つ
/// （一致部分の強調を、元の文字列の正しい位置に出すため）。
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string text) => Build(text, null, null);

    /// <summary>
    /// text の中で、正規化済みの検索語のどれかに一致する部分を返す（元の文字列での位置。重なりはまとめ、位置の順）。
    /// </summary>
    public static IReadOnlyList<TextRange> FindMatches(string text, IReadOnlyList<string> normalizedTerms)
    {
        var result = new List<TextRange>();
        if (string.IsNullOrEmpty(text) || normalizedTerms.Count == 0) return result;

        var starts = new List<int>(text.Length);
        var ends = new List<int>(text.Length);
        var normalized = Build(text, starts, ends);

        var ranges = new List<(int Start, int End)>();
        foreach (var term in normalizedTerms)
        {
            if (string.IsNullOrEmpty(term)) continue;
            for (int i = normalized.IndexOf(term, System.StringComparison.Ordinal); i >= 0;
                 i = normalized.IndexOf(term, i + 1, System.StringComparison.Ordinal))
            {
                ranges.Add((starts[i], ends[i + term.Length - 1]));
            }
        }
        ranges.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        foreach (var (start, end) in ranges)
        {
            if (result.Count > 0 && start <= result[^1].Start + result[^1].Length)
            {
                var last = result[^1];
                var mergedEnd = System.Math.Max(last.Start + last.Length, end);
                result[^1] = new TextRange(last.Start, mergedEnd - last.Start);
            }
            else
            {
                result.Add(new TextRange(start, end - start));
            }
        }
        return result;
    }

    /// <summary>正規化する。starts/ends を渡すと、正規化後の各文字の元の位置（開始・終了）を入れる。</summary>
    private static string Build(string text, List<int>? starts, List<int>? ends)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            // 1 つの単位: サロゲートペア、または半角カナ＋半角の濁点・半濁点
            int length = 1;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) length = 2;
            else if (i + 1 < text.Length && IsHalfwidthSoundMark(text[i + 1])) length = 2;

            var folded = Fold(text.Substring(i, length));
            foreach (var c in folded)
            {
                sb.Append(c);
                starts?.Add(i);
                ends?.Add(i + length);
            }
            i += length;
        }
        return sb.ToString();
    }

    private static bool IsValidUnicode(string unit)
    {
        for (int i = 0; i < unit.Length; i++)
        {
            if (char.IsHighSurrogate(unit[i]))
            {
                if (i + 1 >= unit.Length || !char.IsLowSurrogate(unit[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(unit[i]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsHalfwidthSoundMark(char c) => c == 'ﾞ' || c == 'ﾟ';

    private static string Fold(string unit)
    {
        // NFKC: 全角英数→半角、半角カナ→全角（濁点を合成）。
        // 対になっていないサロゲート（NTFS のファイル名や貼り付けで入りうる不正な UTF-16）は正規化できず例外になるので、そのまま使う
        var normalized = IsValidUnicode(unit) ? unit.Normalize(NormalizationForm.FormKC) : unit;
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var x = c;
            if (x >= 'ぁ' && x <= 'ゖ') x = (char)(x + 0x60); // ひらがな → カタカナ
            sb.Append(char.ToLowerInvariant(x));
        }
        return sb.ToString();
    }
}
