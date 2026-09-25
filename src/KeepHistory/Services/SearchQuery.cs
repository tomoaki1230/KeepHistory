using System;
using System.Collections.Generic;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 検索語。スペース（全角スペースも可）区切りの AND 検索。
/// 対象はファイル名とパスに固定する。
/// </summary>
public sealed class SearchQuery
{
    private static readonly char[] Separators = { ' ', '　', '\t' };

    private SearchQuery(IReadOnlyList<string> terms) => Terms = terms;

    public static SearchQuery Empty { get; } = new(Array.Empty<string>());

    public IReadOnlyList<string> Terms { get; }

    public bool IsEmpty => Terms.Count == 0;

    public static SearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        return new SearchQuery(text.Split(Separators, StringSplitOptions.RemoveEmptyEntries));
    }

    public bool Matches(HistoryEntry entry)
    {
        foreach (var term in Terms)
        {
            if (!entry.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !entry.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}
