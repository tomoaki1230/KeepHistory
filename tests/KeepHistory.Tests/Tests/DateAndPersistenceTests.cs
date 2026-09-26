using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private readonly string? _originalLogDirectory = ErrorLog.DirectoryOverride;

    public DateAndPersistenceTests()
    {
        ErrorLog.DirectoryOverride = _dir.Path;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        ErrorLog.DirectoryOverride = _originalLogDirectory;
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
        Assert.False(defaults.StayResident, "常駐は既定でしない");

        File.WriteAllText(data.SettingsPath, "{ \"RetentionDays\": 0, \"WindowWidth\": 10, \"ExcludePatterns\": null }");
        var clamped = data.LoadSettings();
        Assert.Equal(30, clamped.RetentionDays, "0 日は最短の 1か月に寄せる");
        Assert.Equal(960.0, clamped.WindowWidth);
        Assert.NotNull(clamped.ExcludePatterns);

        File.WriteAllText(data.SettingsPath, "{ \"RetentionDays\": 400 }");
        Assert.Equal(365, data.LoadSettings().RetentionDays, "保持日数の上限は 365 日（手で書き換えた設定も丸める）");
    }

    [Test]
    public void RetentionChoices_AreOneThreeSixMonthsAndOneYear()
    {
        Assert.SequenceEqual(new[] { "1か月", "3か月", "6か月", "1年" }, AppSettings.RetentionChoices.Select(c => c.Label));
        Assert.SequenceEqual(new[] { 30, 90, 180, 365 }, AppSettings.RetentionChoices.Select(c => c.Value));
        Assert.Equal(365, new AppSettings().RetentionDays, "既定は 1年");
    }

    [Test]
    public void RetentionDays_NotInChoicesAreSnappedToTheNextLongerChoice()
    {
        // 旧版の設定（任意の日数）を読んでも、履歴を消しすぎないよう長い方に寄せる
        Assert.Equal(30, AppSettings.SnapRetentionDays(1));
        Assert.Equal(30, AppSettings.SnapRetentionDays(30));
        Assert.Equal(90, AppSettings.SnapRetentionDays(31));
        Assert.Equal(180, AppSettings.SnapRetentionDays(100));
        Assert.Equal(365, AppSettings.SnapRetentionDays(181));
        Assert.Equal(365, AppSettings.SnapRetentionDays(36500));
    }

    [Test]
    public void Settings_NullElementsAreRemoved_SoStartupDoesNotCrash()
    {
        // 手で書き換えた・壊れた settings.json（配列に null）でも起動時に落ちない
        var data = new DataStore(_dir.Path);
        File.WriteAllText(data.SettingsPath,
            "{ \"Columns\": [ null, { \"Id\": \"FileName\", \"Width\": 300, \"DisplayIndex\": 0 } ], \"ExcludePatterns\": [ null, \"*.tmp\" ] }");
        var settings = data.LoadSettings();
        Assert.Equal(1, settings.Columns.Count, "null の列設定は捨てる");
        Assert.SequenceEqual(new[] { "*.tmp" }, settings.ExcludePatterns, "null の除外パターンは捨てる");

        UiTestHost.EnsureApplication();
        var window = new Views.MainWindow(new ViewModels.MainViewModel(new HistoryStore(), () => DateTime.Now));
        window.ApplyLayout(settings);
        new Views.SettingsWindow(settings, _ => true).Close();
        Assert.Equal(300.0, window.FindColumn("FileName")!.Width.Value, "残りの列設定は反映する");
        window.Close();
    }

    [Test]
    public void Settings_PlacementThemeAndPosition_RoundTrip_StartupIsNotSaved()
    {
        var data = new DataStore(_dir.Path);
        data.SaveSettings(new AppSettings
        {
            Placement = WindowPlacementMode.LastPosition, Theme = AppTheme.Dark, WindowLeft = 120, WindowTop = 40, StartWithWindows = true,
        });
        var json = File.ReadAllText(data.SettingsPath);
        Assert.Contains("\"LastPosition\"", json, "列挙は名前で保存する（読みやすく、並びを変えても壊れない）");
        Assert.False(json.Contains("StartWithWindows"), "起動時の登録は settings.json に保存しない（Windows の登録が正）");

        var loaded = data.LoadSettings();
        Assert.Equal(WindowPlacementMode.LastPosition, loaded.Placement);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(120.0, loaded.WindowLeft);
        Assert.Equal(40.0, loaded.WindowTop);

        var defaults = new AppSettings();
        Assert.Equal(WindowPlacementMode.MouseScreenCenter, defaults.Placement, "既定はマウスのある画面の中央");
        Assert.Equal(AppTheme.System, defaults.Theme, "既定は Windows に合わせる");
    }

    [Test]
    public void SettingsExists_IsFalseOnFirstRun()
    {
        var data = new DataStore(_dir.Path);
        Assert.False(data.SettingsExists, "settings.json が無ければ初回起動");
        data.SaveSettings(data.LoadSettings());
        Assert.True(data.SettingsExists, "一度保存したら初回ではない");
    }

    [Test]
    public void StayResident_RoundTrips()
    {
        var data = new DataStore(_dir.Path);
        data.SaveSettings(new AppSettings { StayResident = true });
        Assert.True(data.LoadSettings().StayResident);
        Assert.True(new AppSettings { StayResident = true }.Clone().StayResident, "Clone でも引き継ぐ");
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
