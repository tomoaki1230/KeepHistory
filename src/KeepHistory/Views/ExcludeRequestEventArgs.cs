using System;

namespace KeepHistory.Views;

/// <summary>右クリックの「記録しない」で追加する除外パターンと、確認に出す説明。</summary>
public sealed class ExcludeRequestEventArgs : EventArgs
{
    public ExcludeRequestEventArgs(string pattern, string description)
    {
        Pattern = pattern;
        Description = description;
    }

    public string Pattern { get; }
    public string Description { get; }
}
