using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// キープしたファイルの一覧を CSV にする（ツール ＞ キープの一覧を CSV に出力。1.2.1）。
/// 画面の絞り込みにかかわらず、キープしたものをすべて、前回利用日時の新しい順に出す。
/// Excel でそのまま開けるよう、BOM 付き UTF-8・CRLF・すべての値を "" で囲む。
/// </summary>
public static class KeptCsvExporter
{
    public static readonly string[] Header = { "ファイル名", "フォルダ", "フルパス", "前回利用日時", "回数", "状態" };

    /// <summary>見つからないファイルの「状態」欄。</summary>
    public const string MissingText = "見つからない";

    public static List<HistoryEntry> KeptEntries(IEnumerable<HistoryEntry> entries)
        => entries.Where(e => e.IsKept).OrderByDescending(e => e.LastUsed).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    public static string BuildCsv(IEnumerable<HistoryEntry> kept)
    {
        var sb = new StringBuilder();
        AppendLine(sb, Header);
        foreach (var e in kept)
        {
            AppendLine(sb, new[]
            {
                e.FileName, e.FolderPath, e.Path, e.LastUsedText,
                e.OpenCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                e.IsMissing ? MissingText : string.Empty,
            });
        }
        return sb.ToString();
    }

    /// <summary>BOM 付き UTF-8 で書く。書きかけのファイルを残さないよう、一時ファイルに書いてから置き換える。</summary>
    public static void Write(string path, IEnumerable<HistoryEntry> kept)
    {
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, BuildCsv(kept), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // 置き換えられなかった（Excel で開いたままなど）。一時ファイルを残さない
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>既定のファイル名（例: KeepHistory-キープ-20261013.csv）。</summary>
    public static string DefaultFileName(DateTime now) => $"KeepHistory-キープ-{now:yyyyMMdd}.csv";

    private static void AppendLine(StringBuilder sb, IEnumerable<string> fields)
    {
        sb.Append(string.Join(",", fields.Select(Quote)));
        sb.Append("\r\n");
    }

    private static string Quote(string value)
    {
        // Excel が数式として実行しないよう、= + - @ で始まる値は先頭に ' を付ける（CSV インジェクション対策）
        if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
