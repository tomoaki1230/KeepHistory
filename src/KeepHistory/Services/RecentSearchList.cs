using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace KeepHistory.Services;

/// <summary>
/// 最近の検索語（新しい順、最大 MaxCount 件）。検索欄の右端の「▾」・Alt+↓ から選べる。
/// 保存は searches.json（settings.json には入れない。設定には「今の検索語」を保存しない決まりのため。3.8）。
/// </summary>
public sealed class RecentSearchList
{
    public const int MaxCount = 10;

    /// <summary>画面に渡す一覧（新しい順）。</summary>
    public ObservableCollection<string> Items { get; } = new();

    /// <summary>内容が変わった（保存が必要）。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 検索語を覚える。前後の空白を除き、連続する空白は 1 つにまとめる。空なら何もしない。
    /// 同じ語（大文字小文字を区別しない）があれば先頭へ移す。変わったら true。
    /// </summary>
    public bool Remember(string? text)
    {
        var term = Clean(text);
        if (term.Length == 0) return false;
        var index = IndexOf(term);
        if (index == 0 && Items[0] == term) return false;
        if (index >= 0) Items.RemoveAt(index);
        Items.Insert(0, term);
        while (Items.Count > MaxCount) Items.RemoveAt(Items.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>すべて消す（設定画面の「検索語の履歴を消す」）。</summary>
    public void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>保存してあった内容を読み込む（Changed は上げない）。壊れた値は捨てる。</summary>
    public void Load(IEnumerable<string?>? terms)
    {
        Items.Clear();
        foreach (var raw in terms ?? Enumerable.Empty<string?>())
        {
            var term = Clean(raw);
            if (term.Length == 0 || IndexOf(term) >= 0) continue;
            Items.Add(term);
            if (Items.Count >= MaxCount) break;
        }
    }

    public List<string> ToList() => Items.ToList();

    private int IndexOf(string term)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (string.Equals(Items[i], term, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string Clean(string? text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
