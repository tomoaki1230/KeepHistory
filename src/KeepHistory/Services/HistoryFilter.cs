using System;
using System.Collections.Generic;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>期間プルダウンの選択肢。</summary>
public enum PeriodOption
{
    All,
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    Last90Days,
    Last365Days,
}

/// <summary>プルダウン用の表示名と値の組。</summary>
public sealed class Choice<T>
{
    public Choice(string label, T value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public T Value { get; }

    public override string ToString() => Label;
}

public static class PeriodOptions
{
    public static IReadOnlyList<Choice<PeriodOption>> Choices { get; } = new[]
    {
        new Choice<PeriodOption>("すべての期間", PeriodOption.All),
        new Choice<PeriodOption>("今日", PeriodOption.Today),
        new Choice<PeriodOption>("昨日", PeriodOption.Yesterday),
        new Choice<PeriodOption>("過去 7 日", PeriodOption.Last7Days),
        new Choice<PeriodOption>("過去 30 日", PeriodOption.Last30Days),
        new Choice<PeriodOption>("過去 90 日", PeriodOption.Last90Days),
        new Choice<PeriodOption>("過去 1 年", PeriodOption.Last365Days),
    };

    /// <summary>期間の開始日時。All は null。</summary>
    public static DateTime? StartOf(PeriodOption option, DateTime now) => option switch
    {
        PeriodOption.Today => now.Date,
        PeriodOption.Yesterday => now.Date.AddDays(-1),
        PeriodOption.Last7Days => now.AddDays(-7),
        PeriodOption.Last30Days => now.AddDays(-30),
        PeriodOption.Last90Days => now.AddDays(-90),
        PeriodOption.Last365Days => now.AddDays(-365),
        _ => null,
    };

    /// <summary>期間の終わり（この日時より前だけ）。終わりの無い期間は null。「昨日」は今日の 0 時まで（今日の分は入れない）。</summary>
    public static DateTime? EndOf(PeriodOption option, DateTime now) => option switch
    {
        PeriodOption.Yesterday => now.Date,
        _ => null,
    };
}

/// <summary>一覧の絞り込み条件（保存しない）。</summary>
public sealed class FilterCriteria
{
    public SearchQuery Query { get; init; } = SearchQuery.Empty;

    /// <summary>拡張子（小文字・ドット付き）。null はすべて、空文字は拡張子なし。</summary>
    public string? Extension { get; init; }

    public PeriodOption Period { get; init; } = PeriodOption.All;

    public bool KeptOnly { get; init; }

    /// <summary>このフォルダの直下のファイルだけ（右クリックの「このフォルダのファイルを表示」）。null は絞り込まない。</summary>
    public string? Folder { get; init; }

    /// <summary>見つからないファイル（淡色の行）を出さない（表示メニューの「削除済みのローカルファイルを表示しない」）。キープした行は出す。</summary>
    public bool HideMissing { get; init; }

    /// <summary>
    /// ネットワーク上のパスか（表示メニューの「ネットワーク上のファイルを表示しない」）。null なら絞り込まない。キープした行は出す。
    /// ドライブ種別の問い合わせは応答を待たない（FileExistenceChecker.IsNetworkPath）。
    /// </summary>
    public Func<string, bool>? IsNetworkPath { get; init; }

    public bool Matches(HistoryEntry entry, DateTime now)
    {
        // キープした行は「表示しない」の対象にしない（残しておきたいという意思表示なので、見つからなくても・ネットワーク上でも出す）
        if (!entry.IsKept)
        {
            if (HideMissing && entry.IsMissing) return false;
            if (IsNetworkPath != null && IsNetworkPath(entry.Path)) return false;
        }
        if (KeptOnly && !entry.IsKept) return false;
        if (Folder != null && !SameFolder(entry.FolderPath, Folder)) return false;
        if (Extension != null && !string.Equals(entry.Extension, Extension, StringComparison.OrdinalIgnoreCase)) return false;
        var start = PeriodOptions.StartOf(Period, now);
        var end = PeriodOptions.EndOf(Period, now);
        if (end.HasValue && entry.LastUsed >= end.Value) return false;
        if (start.HasValue && entry.LastUsed < start.Value) return false;
        return Query.Matches(entry);
    }

    /// <summary>同じフォルダか（大文字小文字・末尾の \ を区別しない）。</summary>
    public static bool SameFolder(string a, string b)
        => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
