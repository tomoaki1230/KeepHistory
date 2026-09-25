using System;
using System.Globalization;

namespace KeepHistory.Services;

/// <summary>
/// 日時の文字列化は必ずここを通す。
/// 和暦設定（ja-JP + JapaneseCalendar）の環境で現在のカルチャを使うと年が「8」などに化けるため、
/// 保存も表示も CultureInfo.InvariantCulture で行う。
/// </summary>
public static class DateFormat
{
    /// <summary>保存形式。秒未満まで残す（切り捨てると .lnk の時刻と一致せず、消した履歴が復活する）。</summary>
    public const string StorageFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff";

    private static readonly string[] AcceptedStorageFormats =
    {
        StorageFormat,
        "yyyy-MM-dd'T'HH:mm:ss",
    };

    public const string DisplayFormat = "yyyy/MM/dd HH:mm";

    public static string ToStorage(DateTime value)
        => value.ToString(StorageFormat, CultureInfo.InvariantCulture);

    public static bool TryParseStorage(string? text, out DateTime value)
        => DateTime.TryParseExact(text, AcceptedStorageFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);

    public static string ToDisplay(DateTime value)
        => value.ToString(DisplayFormat, CultureInfo.InvariantCulture);
}
