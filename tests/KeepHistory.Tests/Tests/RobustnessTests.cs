using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

/// <summary>
/// 大量のでたらめな入力で、いつも成り立つはずの性質（落ちない・範囲外にならない・結果が矛盾しない）を確かめる。
/// 乱数の種は固定し、失敗したら同じ入力で再現できるようにする。
/// </summary>
public sealed class RobustnessTests : IDisposable
{
    private readonly TempDirectory _dir = new("robustness");
    private readonly string? _originalLog = ErrorLog.DirectoryOverride;

    public RobustnessTests() => ErrorLog.DirectoryOverride = _dir.Path;

    public void Dispose()
    {
        ErrorLog.DirectoryOverride = _originalLog;
        _dir.Dispose();
    }

    private static string RandomText(Random r, string[] pool, int maxLength)
    {
        var sb = new StringBuilder();
        var n = r.Next(0, maxLength + 1);
        for (int i = 0; i < n; i++) sb.Append(pool[r.Next(pool.Length)]);
        return sb.ToString();
    }

    [Test]
    public void Wildcard_AgreesWithRegex_OnRandomPatterns()
    {
        var r = new Random(12345);
        var patternPool = new[] { "a", "b", "A", "*", "?", "." };
        var textPool = new[] { "a", "b", "A", "B", "." };
        for (int i = 0; i < 20000; i++)
        {
            var pattern = RandomText(r, patternPool, 6);
            var text = RandomText(r, textPool, 8);
            var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            var expected = Regex.IsMatch(text, regex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Assert.Equal(expected, WildcardMatcher.IsMatch(pattern, text), $"パターン「{pattern}」文字列「{text}」");
        }
    }

    private static readonly string[] KanaPool =
    {
        "あ", "ア", "ｱ", "が", "ガ", "ｶﾞ", "ぱ", "パ", "ﾊﾟ", "ゔ", "ヴ", "ｳﾞ", "ー", "ｰ", "a", "A", "Ａ", "ａ", "1", "１",
        "😀", "👍🏽", "e\u0301", "é", "ﾞ", "ﾟ", " ", "　", "_", ".", "\\", "漢",
    };

    [Test]
    public void FindMatches_RangesAreValid_OnRandomText()
    {
        var r = new Random(777);
        for (int i = 0; i < 5000; i++)
        {
            var text = RandomText(r, KanaPool, 12);
            var normalized = TextNormalizer.Normalize(text);
            var terms = new List<string>();
            for (int k = 0; k < r.Next(1, 3); k++)
            {
                if (normalized.Length == 0) break;
                var start = r.Next(normalized.Length);
                var length = r.Next(1, Math.Min(4, normalized.Length - start) + 1);
                terms.Add(normalized.Substring(start, length));
            }
            if (r.Next(4) == 0) terms.Add(TextNormalizer.Normalize(RandomText(r, KanaPool, 2)));

            var ranges = TextNormalizer.FindMatches(text, terms);
            var where = $"文字列「{text}」検索語「{string.Join("｜", terms)}」";
            int previousEnd = -1;
            foreach (var range in ranges)
            {
                Assert.True(range.Length > 0, "長さ 0 の強調は無い " + where);
                Assert.True(range.Start >= 0 && range.Start + range.Length <= text.Length, "元の文字列の範囲内 " + where);
                Assert.True(range.Start > previousEnd, "位置の順で、重ならず・隣り合わない " + where);
                previousEnd = range.Start + range.Length;
                Assert.False(char.IsLowSurrogate(text[range.Start]), "絵文字（サロゲートペア）の途中から始めない " + where);
                Assert.False(char.IsHighSurrogate(text[range.Start + range.Length - 1]), "絵文字の途中で終わらない " + where);
            }
            foreach (var term in terms.Where(t => t.Length > 0))
            {
                if (normalized.Contains(term, StringComparison.Ordinal))
                {
                    Assert.True(ranges.Count > 0, "正規化した文字列に含まれる検索語は、必ずどこかを強調する " + where);
                }
            }
        }
    }

    [Test]
    public void Normalize_IsIdempotent_AndSearchIsConsistent()
    {
        var r = new Random(4242);
        for (int i = 0; i < 5000; i++)
        {
            var text = RandomText(r, KanaPool, 10);
            var once = TextNormalizer.Normalize(text);
            Assert.Equal(once, TextNormalizer.Normalize(once), $"正規化を 2 回しても変わらない「{text}」");

            // 検索: 正規化した文字列の一部で探したら、必ず見つかる
            var entry = new HistoryEntry(@"C:\dir\" + text.Replace("\\", "_") + ".txt", DateTime.Now);
            var name = entry.NormalizedFileName;
            if (name.Length == 0) continue;
            var start = r.Next(name.Length);
            var term = name.Substring(start, r.Next(1, name.Length - start + 1)).Trim();
            if (term.Length == 0 || term.Contains(' ') || term.Contains('　')) continue;
            Assert.True(SearchQuery.Parse(term).Matches(entry), $"ファイル名「{entry.FileName}」の一部「{term}」で見つかる");
        }
    }

    [Test]
    public void DataStore_NeverThrows_OnCorruptedFiles()
    {
        var r = new Random(99);
        var data = new DataStore(_dir.Path);
        var valid = new[]
        {
            "[ { \"Path\": \"C:\\\\a.txt\", \"LastUsed\": \"2026-09-25T10:30:00.1234567\", \"IsKept\": true, \"OpenCount\": 3 } ]",
            "{ \"RetentionDays\": 90, \"Placement\": \"LastPosition\", \"Theme\": \"Dark\", \"Columns\": [ { \"Id\": \"FileName\", \"Width\": 300, \"DisplayIndex\": 0 } ] }",
        };
        var weird = new[]
        {
            "", " ", "null", "[]", "{}", "[null]", "[1,2,3]", "\"text\"", "{\"RetentionDays\":\"abc\"}", "{\"Placement\":\"Nowhere\"}",
            "{\"Theme\":7}", "{\"Hotkey\":null,\"Columns\":null}", "{\"WindowWidth\":1e308}", "{\"WindowLeft\":\"x\"}",
            "[{\"Path\":null,\"LastUsed\":null}]", "[{\"Path\":\"C:\\\\a\",\"LastUsed\":\"令和\",\"OpenCount\":-5}]",
        };
        for (int i = 0; i < 400; i++)
        {
            string content;
            switch (r.Next(4))
            {
                case 0:
                    content = weird[r.Next(weird.Length)];
                    break;
                case 1:
                    var v = valid[r.Next(valid.Length)];
                    content = v.Substring(0, r.Next(v.Length + 1)); // 書きかけで切れた
                    break;
                case 2:
                    var bytes = new byte[r.Next(0, 200)];
                    r.NextBytes(bytes);
                    content = Encoding.UTF8.GetString(bytes);
                    break;
                default:
                    var chars = valid[r.Next(valid.Length)].ToCharArray();
                    for (int k = 0; k < 3; k++) chars[r.Next(chars.Length)] = "{}[]\",:0x"[r.Next(9)];
                    content = new string(chars);
                    break;
            }
            foreach (var file in new[] { data.HistoryPath, data.DeletedPath, data.SettingsPath }) File.WriteAllText(file, content);

            var history = data.LoadHistory();
            var deleted = data.LoadDeleted();
            var settings = data.LoadSettings();
            var store = new HistoryStore();
            store.Load(history, deleted);
            Assert.True(settings.RetentionDays is 30 or 90 or 180 or 365, $"保持日数は選択肢のどれか（内容: {content}）");
            Assert.True(settings.WindowWidth >= 300 && settings.WindowHeight >= 200 && !double.IsInfinity(settings.WindowWidth), $"ウインドウの大きさは妥当（内容: {content}）");
            Assert.True(Enum.IsDefined(settings.Placement) && Enum.IsDefined(settings.Theme), $"列挙は定義された値（内容: {content}）");
            Assert.True(settings.Columns.All(c => c != null) && settings.ExcludePatterns.All(p => p != null), $"null 要素なし（内容: {content}）");
            Assert.True(store.Entries.All(e => e.OpenCount >= 1), "回数は 1 以上");
        }
    }

    [Test]
    public void HistoryStore_RandomOperations_KeepInvariants()
    {
        var r = new Random(2026);
        var now = new DateTime(2026, 9, 26, 12, 0, 0);
        var paths = Enumerable.Range(0, 12).Select(i => i % 3 == 0 ? $@"C:\Dir\File{i}.TXT" : $@"c:\dir\file{i}.txt").ToArray();
        var store = new HistoryStore { RetentionDays = 30, Exclusion = new ExclusionFilter(ExclusionFilter.DefaultPatterns) };
        for (int step = 0; step < 20000; step++)
        {
            var path = paths[r.Next(paths.Length)];
            switch (r.Next(9))
            {
                case 0:
                case 1:
                case 2:
                    store.Register(path, now.AddDays(-r.Next(0, 60)).AddSeconds(r.Next(3600)), now);
                    break;
                case 3:
                    if (store.Entries.Count > 0) store.Remove(new[] { store.Entries[r.Next(store.Entries.Count)] }, now);
                    break;
                case 4:
                    store.Restore(new[] { path });
                    break;
                case 5:
                    if (store.Find(path) is { } e) e.IsKept = !e.IsKept;
                    break;
                case 6:
                    now = now.AddDays(r.Next(0, 5));
                    var keptBefore = store.Entries.Where(x => x.IsKept).Select(x => x.Path).ToList();
                    store.Purge(now);
                    Assert.True(keptBefore.All(p => store.Find(p) != null), "期限切れの削除でキープは消えない");
                    break;
                case 7:
                    // 保存して読み直しても同じ
                    var reloaded = new HistoryStore { RetentionDays = 30, Exclusion = store.Exclusion };
                    reloaded.Load(store.ToHistoryRecords(), store.ToDeletedRecords());
                    Assert.SequenceEqual(store.ToHistoryRecords().Select(x => (x.Path, x.LastUsed, x.IsKept, x.OpenCount)),
                        reloaded.ToHistoryRecords().Select(x => (x.Path, x.LastUsed, x.IsKept, x.OpenCount)), "保存と読み込みで変わらない");
                    Assert.SequenceEqual(store.ToDeletedRecords().Select(x => (x.Path, x.DeletedAt, x.LastUsed)),
                        reloaded.ToDeletedRecords().Select(x => (x.Path, x.DeletedAt, x.LastUsed)), "削除の記憶も変わらない");
                    break;
                default:
                    if (r.Next(50) == 0) store.Clear();
                    break;
            }

            // いつも成り立つこと
            var entryPaths = store.Entries.Select(x => x.Path).ToList();
            Assert.Equal(entryPaths.Count, entryPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), $"同じパスが 2 件にならない（{step} 手目）");
            Assert.True(entryPaths.All(p => store.Find(p) != null), "一覧と索引が一致する");
            Assert.False(entryPaths.Any(p => store.Deleted.ContainsKey(p)), $"一覧にあるものは削除の記憶に無い（{step} 手目）");
            Assert.True(store.Entries.All(x => x.OpenCount >= 1), "回数は 1 以上");
        }
    }

    [Test]
    public void InvalidUnicodeInFileNameOrSearch_DoesNotThrow()
    {
        // NTFS はファイル名に不正な UTF-16（対になっていないサロゲート）を許す。貼り付けで検索語に入ることもある
        var entry = new HistoryEntry("C:\\dir\\bad\uD800name.txt", DateTime.Now);
        Assert.NotNull(entry.NormalizedPath, "パスの正規化で落ちない");
        Assert.True(SearchQuery.Parse("bad").Matches(entry), "ほかの部分では普通に検索できる");
        Assert.False(SearchQuery.Parse("x\uDC00").Matches(entry), "不正な文字を含む検索語でも落ちない");
        Assert.True(TextNormalizer.FindMatches(entry.FileName, new[] { "name" }).Count == 1, "強調も落ちない");
    }

    [Test]
    public void UnusualFileNames_DoNotBreakEntryOrSearchOrHighlight()
    {
        var now = DateTime.Now;
        var longPath = @"C:\" + string.Join(@"\", Enumerable.Repeat("とても長いフォルダ名", 40)) + @"\最後.txt";
        var paths = new[]
        {
            @"C:\", @"C:\.gitignore", @"C:\noext", @"C:\a.b.c.TXT", @"C:\dir.with.dot\file", @"\\server\share\報告書.pdf",
            @"\\?\C:\long\path.txt", @"C:\絵文字😀\写真👍🏽.jpg", "C:\\cafe\u0301\\re\u0301sume\u0301.doc", @"C:\ｶﾞｲﾄﾞ\ﾃﾞｰﾀ.csv",
            @"C:\space  double\ file .txt", longPath, @"C:\[brackets]\(paren)\{brace}.txt", @"C:\*star?\x.txt",
        };
        foreach (var path in paths)
        {
            var entry = new HistoryEntry(path, now);
            _ = entry.FileName;
            _ = entry.FolderPath;
            _ = entry.Extension;
            _ = entry.LastUsedText;
            Assert.NotNull(entry.NormalizedPath);
            var query = SearchQuery.Parse(entry.FileName.Length > 0 ? entry.FileName.Substring(0, Math.Min(2, entry.FileName.Length)) : "x");
            _ = query.Matches(entry);
            var ranges = TextNormalizer.FindMatches(entry.FolderPath, query.NormalizedTerms);
            Assert.True(ranges.All(x => x.Start >= 0 && x.Start + x.Length <= entry.FolderPath.Length), "強調が範囲内: " + path);
            _ = new ExclusionFilter(ExclusionFilter.DefaultPatterns).IsExcluded(path);
            _ = new FileExistenceChecker().IsMissing(path);
        }
        Assert.Equal("", new HistoryEntry(@"C:\", now).FileName, "ドライブ直下はファイル名なし（落ちない）");
    }
}
