using System;

namespace KeepHistory.Services;

/// <summary>
/// 一時的に読めなかったデータの読み直し・止まった監視の始め直しを試す間隔。
/// 切断が続く間は 30 秒 → 1 分 → 2 分 → 4 分 → 5 分（上限）と空けていき、ネットワークへの問い合わせを控えめにする。
/// 直ったら（または新しく止まったら）30 秒に戻す。
/// </summary>
public sealed class RecoveryBackoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    /// <summary>次に試すまでの間隔。</summary>
    public TimeSpan Current { get; private set; } = Initial;

    /// <summary>試したが、まだ直らなかった。間隔を倍にする（上限まで）。</summary>
    public void Failed()
    {
        var next = TimeSpan.FromTicks(Current.Ticks * 2);
        Current = next > Max ? Max : next;
    }

    /// <summary>直った・直すものが無い・新しく止まった。最初の間隔に戻す。</summary>
    public void Reset() => Current = Initial;
}
