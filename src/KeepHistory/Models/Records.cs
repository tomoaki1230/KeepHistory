using System.Collections.Generic;

namespace KeepHistory.Models;

// JSON に保存する形。日時は DateFormat.ToStorage の文字列で持つ（カルチャ非依存にするため）。

/// <summary>history.json の 1 件。</summary>
public sealed class HistoryRecord
{
    public string Path { get; set; } = string.Empty;
    public string LastUsed { get; set; } = string.Empty;
    public bool IsKept { get; set; }
}

/// <summary>deleted.json の 1 件。この日時以前の利用記録（.lnk）は再登録しない。</summary>
public sealed class DeletedRecord
{
    public string Path { get; set; } = string.Empty;
    public string DeletedAt { get; set; } = string.Empty;
}

/// <summary>一覧の列レイアウト。</summary>
public sealed class ColumnLayout
{
    public string Id { get; set; } = string.Empty;
    public double Width { get; set; }
    public int DisplayIndex { get; set; }
}

/// <summary>グローバルホットキーの設定。</summary>
public sealed class HotkeySetting
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public bool Enabled { get; set; } = true;
    public bool Control { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }

    /// <summary>System.Windows.Input.Key の名前（例: "H", "F9"）。</summary>
    public string Key { get; set; } = "H";

    public bool HasModifier => Control || Alt || Shift || Win;

    /// <summary>RegisterHotKey に渡す修飾キーのフラグ。</summary>
    public uint ToNativeModifiers()
    {
        uint m = ModNoRepeat;
        if (Alt) m |= ModAlt;
        if (Control) m |= ModControl;
        if (Shift) m |= ModShift;
        if (Win) m |= ModWin;
        return m;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    public HotkeySetting Clone() => new()
    {
        Enabled = Enabled, Control = Control, Alt = Alt, Shift = Shift, Win = Win, Key = Key,
    };
}
