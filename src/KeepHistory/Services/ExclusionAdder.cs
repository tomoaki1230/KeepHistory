using System;
using System.Linq;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>一覧の右クリック「記録しない」で使う除外パターン。</summary>
public static class ExclusionPatterns
{
    /// <summary>このファイルだけ（フルパス）。</summary>
    public static string ForFile(string path) => path;

    /// <summary>このフォルダの中のファイル（サブフォルダを含む）。</summary>
    public static string ForFolder(string folderPath) => folderPath.TrimEnd('\\') + @"\*";

    /// <summary>この拡張子（例: .xlsx → *.xlsx）。</summary>
    public static string ForExtension(string extension) => "*" + extension;
}

/// <summary>
/// 一覧の右クリックから除外パターンを追加する。追加すると、当たる履歴（キープ以外）を一覧から消すので、
/// 件数を示して確認してから追加する。
/// </summary>
public static class ExclusionAdder
{
    public static string BuildConfirmMessage(string description, string pattern, int removed)
        => $"{description}を記録しないようにします。\n\n"
           + $"除外パターン: {pattern}\n"
           + (removed > 0 ? $"今ある履歴のうち {removed} 件を一覧から削除します（キープした履歴は残ります）。\n" : "今ある履歴で削除されるものはありません。\n")
           + "\n元に戻すときは「ツール ＞ 設定」の除外パターンから削除してください。\n\nよろしいですか？";

    /// <summary>
    /// 確認して除外パターンを追加し、当たる履歴を消す。追加したら true。
    /// すでに同じパターンがあれば何もしない（確認も出さない）。
    /// </summary>
    public static bool TryAdd(AppSettings settings, HistoryStore store, string pattern, string description, Func<string, bool> confirm)
    {
        if (settings.ExcludePatterns.Any(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase))) return false;
        var filter = new ExclusionFilter(settings.ExcludePatterns.Append(pattern));
        var (removed, _) = store.CountRemovals(filter, int.MaxValue / 2, DateTime.Now);
        if (!confirm(BuildConfirmMessage(description, pattern, removed))) return false;

        settings.ExcludePatterns.Add(pattern);
        store.Exclusion = filter;
        store.RemoveExcluded();
        return true;
    }
}
