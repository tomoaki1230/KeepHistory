using System;

namespace KeepHistory.Services;

/// <summary>一覧を日付でまとめるときの区切り（エクスプローラーの「日付でグループ化」に近い分け方）。</summary>
public enum DateGroup
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    ThisMonth,
    Earlier,
}

public static class DateGroups
{
    /// <summary>
    /// 前回利用日時がどの区切りに入るか。週は月曜はじまり。
    /// 今日・昨日を先に判定し、残りを今週・先週・今月・それより前に分ける（今月は、先週より前で今月のもの）。
    /// 未来の日時（時計のずれなど）は今日とする。
    /// </summary>
    public static DateGroup Of(DateTime lastUsed, DateTime now)
    {
        var today = now.Date;
        var day = lastUsed.Date;
        if (day >= today) return DateGroup.Today;
        if (day == today.AddDays(-1)) return DateGroup.Yesterday;
        var thisWeek = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (day >= thisWeek) return DateGroup.ThisWeek;
        if (day >= thisWeek.AddDays(-7)) return DateGroup.LastWeek;
        if (day >= new DateTime(today.Year, today.Month, 1)) return DateGroup.ThisMonth;
        return DateGroup.Earlier;
    }

    public static string LabelOf(DateGroup group) => group switch
    {
        DateGroup.Today => "今日",
        DateGroup.Yesterday => "昨日",
        DateGroup.ThisWeek => "今週",
        DateGroup.LastWeek => "先週",
        DateGroup.ThisMonth => "今月",
        _ => "それより前",
    };
}
