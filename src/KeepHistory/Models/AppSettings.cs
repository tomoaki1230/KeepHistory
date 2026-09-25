using System;
using System.Collections.Generic;
using System.Linq;
using KeepHistory.Services;

namespace KeepHistory.Models;

/// <summary>
/// settings.json の内容。
/// 並び順・絞り込み・検索語は保存しない（ここに項目を足さないこと）。
/// 列レイアウトとウインドウサイズは保存する。
/// </summary>
public sealed class AppSettings
{
    public const int DefaultRetentionDays = 365;
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 36500;

    /// <summary>保持日数。★キープした履歴はこの日数を過ぎても残す。</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>記録しないファイルのワイルドカード。</summary>
    public List<string> ExcludePatterns { get; set; } = new(ExclusionFilter.DefaultPatterns);

    public HotkeySetting Hotkey { get; set; } = new();

    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 600;

    public List<ColumnLayout> Columns { get; set; } = new();

    /// <summary>読み込んだ値を妥当な範囲に収める。</summary>
    public AppSettings Normalize()
    {
        RetentionDays = Math.Clamp(RetentionDays, MinRetentionDays, MaxRetentionDays);
        ExcludePatterns ??= new List<string>(ExclusionFilter.DefaultPatterns);
        Hotkey ??= new HotkeySetting();
        if (string.IsNullOrWhiteSpace(Hotkey.Key)) Hotkey.Key = "H";
        Columns ??= new List<ColumnLayout>();
        if (double.IsNaN(WindowWidth) || WindowWidth < 300) WindowWidth = 960;
        if (double.IsNaN(WindowHeight) || WindowHeight < 200) WindowHeight = 600;
        return this;
    }

    public AppSettings Clone() => new()
    {
        RetentionDays = RetentionDays,
        ExcludePatterns = new List<string>(ExcludePatterns),
        Hotkey = Hotkey.Clone(),
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        Columns = Columns.Select(c => new ColumnLayout { Id = c.Id, Width = c.Width, DisplayIndex = c.DisplayIndex }).ToList(),
    };
}
