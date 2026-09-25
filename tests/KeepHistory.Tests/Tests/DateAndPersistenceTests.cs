using System;
using System.Globalization;
using System.IO;
using System.Threading;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class DateAndPersistenceTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly TempDirectory _dir = new("persistence");

    public DateAndPersistenceTests()
    {
        ErrorLog.DirectoryOverride = _dir.Path;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        ErrorLog.DirectoryOverride = null;
        _dir.Dispose();
    }

    /// <summary>和暦（ja-JP + JapaneseCalendar）を現在のカルチャにする。</summary>
    private static void UseJapaneseEraCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ja-JP").Clone();
        culture.DateTimeFormat.Calendar = new JapaneseCalendar();
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }

    [Test]
    public void DateFormat_IsNotAffectedByJapaneseEraCulture()
    {
        UseJapaneseEraCulture();
        var value = new DateTime(2026, 9, 25, 8, 5, 3).AddTicks(1234567);

        Assert.Equal("2026/09/25 08:05", DateFormat.ToDisplay(value));
        Assert.Equal("2026-09-25T08:05:03.1234567", DateFormat.ToStorage(value));
        Assert.True(DateFormat.TryParseStorage(DateFormat.ToStorage(value), out var parsed));
        Assert.Equal(value, parsed, "秒未満まで往復できる");
        Assert.Equal("2026/09/25 08:05", new HistoryEntry(@"C:\a.txt", value).LastUsedText);
    }

    [Test]
    public void DataStore_RoundTripsUnderJapaneseEraCulture()
    {
        UseJapaneseEraCulture();
        var data = new DataStore(_dir.Path);
        var store = new HistoryStore();
        var lastUsed = new DateTime(2026, 9, 1, 10, 30, 0).AddTicks(42);
        store.Register(@"C:\資料\見積書.xlsx", lastUsed, lastUsed);
        store.Find(@"C:\資料\見積書.xlsx")!.IsKept = true;
        data.SaveHistory(store.ToHistoryRecords());
        data.SaveDeleted(store.ToDeletedRecords());

        var json = File.ReadAllText(data.HistoryPath);
        Assert.Contains("2026-09-01T10:30:00", json, "西暦で保存する");
        Assert.Contains("見積書", json, "日本語をエスケープせず読める形で保存する");

        var reloaded = new HistoryStore();
        reloaded.Load(data.LoadHistory(), data.LoadDeleted());
        var entry = Assert.NotNull(reloaded.Find(@"C:\資料\見積書.xlsx"));
        Assert.Equal(lastUsed, entry.LastUsed);
        Assert.True(entry.IsKept);
    }

    [Test]
    public void DataStore_BrokenFileStartsEmptyAndIsBackedUp()
    {
        var data = new DataStore(_dir.Path);
        File.WriteAllText(data.HistoryPath, "{ これは JSON ではない");
        Assert.Equal(0, data.LoadHistory().Count);
        Assert.True(File.Exists(data.HistoryPath + ".broken"));
    }

    [Test]
    public void Settings_DefaultsWhenMissingAndClampedWhenInvalid()
    {
        var data = new DataStore(_dir.Path);
        var defaults = data.LoadSettings();
        Assert.Equal(365, defaults.RetentionDays, "保持日数の既定は 365 日");
        Assert.True(defaults.ExcludePatterns.Contains("~$*"));
        Assert.True(defaults.Hotkey.Enabled);

        File.WriteAllText(data.SettingsPath, "{ \"RetentionDays\": 0, \"WindowWidth\": 10, \"ExcludePatterns\": null }");
        var clamped = data.LoadSettings();
        Assert.Equal(1, clamped.RetentionDays);
        Assert.Equal(960.0, clamped.WindowWidth);
        Assert.NotNull(clamped.ExcludePatterns);
    }

    [Test]
    public void Settings_DoNotPersistSortFilterOrSearch()
    {
        // 並び順・絞り込み・検索語は保存しない。列レイアウトとウインドウサイズは保存する
        var names = Array.ConvertAll(typeof(AppSettings).GetProperties(), p => p.Name);
        foreach (var forbidden in new[] { "Sort", "Search", "Filter", "Extension", "Period", "KeptOnly" })
        {
            Assert.False(Array.Exists(names, n => n.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), $"{forbidden} を保存してはいけない");
        }
        Assert.True(Array.Exists(names, n => n == nameof(AppSettings.Columns)));
        Assert.True(Array.Exists(names, n => n == nameof(AppSettings.WindowWidth)));
    }
}
