using System;
using System.Collections.Generic;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 検索語。スペース（全角スペースも可）区切りの AND 検索。
/// 対象はファイル名とパスに固定する。ひらがな／カタカナ、全角／半角、大文字／小文字は区別しない（TextNormalizer）。
/// </summary>
public sealed class SearchQuery
{
    private static readonly char[] Separators = { ' ', '　', '\t' };

    private SearchQuery(IReadOnlyList<string> terms)
    {
        Terms = terms;
        var normalized = new List<string>(terms.Count);
        foreach (var term in terms) normalized.Add(TextNormalizer.Normalize(term));
        NormalizedTerms = normalized;
    }

    public static SearchQuery Empty { get; } = new(Array.Empty<string>());

    public IReadOnlyList<string> Terms { get; }

    /// <summary>正規化した検索語（一致の判定と強調に使う）。</summary>
    public IReadOnlyList<string> NormalizedTerms { get; }

    public bool IsEmpty => Terms.Count == 0;

    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        return new SearchQuery(text.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
    }

    public bool Matches(HistoryEntry entry)
    {
        foreach (var term in NormalizedTerms)
        {
            if (!entry.NormalizedFileName.Contains(term, StringComparison.Ordinal)
                && !entry.NormalizedPath.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
