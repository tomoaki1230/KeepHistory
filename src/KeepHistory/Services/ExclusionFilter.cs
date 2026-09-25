using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeepHistory.Services;

/// <summary>
/// 記録しないファイルの判定。
/// \ または / を含むパターンはフルパスと、含まないパターンはファイル名と照合する。
/// 空行と # で始まる行は無視する。
/// </summary>
public sealed class ExclusionFilter
{
    /// <summary>既定の除外パターン（Office の一時ファイル、ダウンロード途中のファイルなど）。</summary>
    public static readonly string[] DefaultPatterns =
    {
        "~$*",
        "~*.tmp",
        "*.tmp",
        "*.crdownload",
        "*.part",
        "*.partial",
        "*.download",
        "*.opdownload",
    };

    private readonly string[] _namePatterns;
    private readonly string[] _pathPatterns;

    public ExclusionFilter(IEnumerable<string>? patterns)
    {
        var list = (patterns ?? Array.Empty<string>())
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0 && !p.StartsWith('#'))
            .ToList();
        _pathPatterns = list.Where(IsPathPattern).Select(p => p.Replace('/', '\\')).ToArray();
        _namePatterns = list.Where(p => !IsPathPattern(p)).ToArray();
    }

    public static ExclusionFilter Empty { get; } = new(null);

    public bool IsExcluded(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var name = Path.GetFileName(path);
        foreach (var pattern in _namePatterns)
        {
            if (WildcardMatcher.IsMatch(pattern, name)) return true;
        }
        if (_pathPatterns.Length > 0)
        {
            var normalized = path.Replace('/', '\\');
            foreach (var pattern in _pathPatterns)
            {
                if (WildcardMatcher.IsMatch(pattern, normalized)) return true;
            }
        }
        return false;
    }

    private static bool IsPathPattern(string pattern) => pattern.Contains('\\') || pattern.Contains('/');
}
