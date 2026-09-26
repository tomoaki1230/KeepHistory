using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

    /// <summary>画面を出す位置。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowPlacementMode Placement { get; set; } = WindowPlacementMode.MouseScreenCenter;

    /// <summary>前回の位置（Placement が LastPosition のとき使う）。無ければ null。</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>画面の色（Windows に合わせる／ライト／ダーク）。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Windows の起動時に起動するか。settings.json には保存しない（Windows のスタートアップ登録そのものが正）。
    /// 設定画面を開く前に登録の状態を入れ、OK したら登録・解除する。
    /// </summary>
    [JsonIgnore]
    public bool StartWithWindows { get; set; }

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
        if (!Enum.IsDefined(Placement)) Placement = WindowPlacementMode.MouseScreenCenter;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.System;
        if (WindowLeft is { } left && (double.IsNaN(left) || double.IsInfinity(left))) WindowLeft = null;
        if (WindowTop is { } top && (double.IsNaN(top) || double.IsInfinity(top))) WindowTop = null;
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
        Placement = Placement,
        WindowLeft = WindowLeft,
        WindowTop = WindowTop,
        Theme = Theme,
        StartWithWindows = StartWithWindows,
        Columns = Columns.Select(c => new ColumnLayout { Id = c.Id, Width = c.Width, DisplayIndex = c.DisplayIndex }).ToList(),
    };
}

/// <summary>画面を出す位置。</summary>
public enum WindowPlacementMode
{
    /// <summary>マウスのある画面の中央（既定）。</summary>
    MouseScreenCenter,
    /// <summary>前回の位置（画面外なら中央）。</summary>
    LastPosition,
}

/// <summary>画面の色。</summary>
public enum AppTheme
{
    /// <summary>Windows の設定（アプリのモード）に合わせる（既定）。</summary>
    System,
    Light,
    Dark,
}
