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

    /// <summary>保持日数の上限。1 年より長くは残さない（★キープした履歴は除く）。</summary>
    public const int MaxRetentionDays = 365;

    /// <summary>保持期間の選択肢（設定画面のプルダウン）。値は日数。</summary>
    public static IReadOnlyList<Choice<int>> RetentionChoices { get; } = new[]
    {
        new Choice<int>("1か月", 30),
        new Choice<int>("3か月", 90),
        new Choice<int>("6か月", 180),
        new Choice<int>("1年", MaxRetentionDays),
    };

    /// <summary>保持日数。RetentionChoices のいずれか。★キープした履歴はこの日数を過ぎても残す。</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>通知領域に常駐するか。false なら画面を閉じたら終了する。</summary>
    public bool StayResident { get; set; }

    /// <summary>記録しないファイルのワイルドカード。</summary>
    public List<string> ExcludePatterns { get; set; } = new(ExclusionFilter.DefaultPatterns);

    public HotkeySetting Hotkey { get; set; } = new();

    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 600;

    public List<ColumnLayout> Columns { get; set; } = new();

    /// <summary>読み込んだ値を妥当な範囲に収める。</summary>
    public AppSettings Normalize()
    {
        RetentionDays = SnapRetentionDays(RetentionDays);
        ExcludePatterns ??= new List<string>(ExclusionFilter.DefaultPatterns);
        Hotkey ??= new HotkeySetting();
        if (string.IsNullOrWhiteSpace(Hotkey.Key)) Hotkey.Key = "H";
        Columns ??= new List<ColumnLayout>();
        // 手で書き換えた・壊れた settings.json の配列の null 要素は捨てる（残すと起動時に落ちる）
        ExcludePatterns.RemoveAll(p => p == null);
        Columns.RemoveAll(c => c == null || c.Id == null);
        if (double.IsNaN(WindowWidth) || WindowWidth < 300) WindowWidth = 960;
        if (double.IsNaN(WindowHeight) || WindowHeight < 200) WindowHeight = 600;
        return this;
    }

    /// <summary>
    /// 選択肢に無い日数（旧版の設定や手での書き換え）を、それ以上で最も短い選択肢に寄せる
    /// （勝手に短くして履歴を消しすぎないように）。上限を超える値は 1 年にする。
    /// </summary>
    public static int SnapRetentionDays(int days)
    {
        foreach (var choice in RetentionChoices)
        {
            if (days <= choice.Value) return choice.Value;
        }
        return MaxRetentionDays;
    }

    public AppSettings Clone() => new()
    {
        RetentionDays = RetentionDays,
        StayResident = StayResident,
        ExcludePatterns = new List<string>(ExcludePatterns),
        Hotkey = Hotkey.Clone(),
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        Columns = Columns.Select(c => new ColumnLayout { Id = c.Id, Width = c.Width, DisplayIndex = c.DisplayIndex }).ToList(),
    };
}
