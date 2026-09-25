using System;
using System.Collections.Generic;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>期間プルダウンの選択肢。</summary>
public enum PeriodOption
{
    All,
    Today,
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
        new Choice<PeriodOption>("過去 7 日", PeriodOption.Last7Days),
        new Choice<PeriodOption>("過去 30 日", PeriodOption.Last30Days),
        new Choice<PeriodOption>("過去 90 日", PeriodOption.Last90Days),
        new Choice<PeriodOption>("過去 1 年", PeriodOption.Last365Days),
    };

    /// <summary>期間の開始日時。All は null。</summary>
    public static DateTime? StartOf(PeriodOption option, DateTime now) => option switch
    {
        PeriodOption.Today => now.Date,
        PeriodOption.Last7Days => now.AddDays(-7),
        PeriodOption.Last30Days => now.AddDays(-30),
        PeriodOption.Last90Days => now.AddDays(-90),
        PeriodOption.Last365Days => now.AddDays(-365),
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

    public bool Matches(HistoryEntry entry, DateTime now)
    {
        if (KeptOnly && !entry.IsKept) return false;
        if (Extension != null && !string.Equals(entry.Extension, Extension, StringComparison.OrdinalIgnoreCase)) return false;
        var start = PeriodOptions.StartOf(Period, now);
        if (start.HasValue && entry.LastUsed < start.Value) return false;
        return Query.Matches(entry);
    }
}
