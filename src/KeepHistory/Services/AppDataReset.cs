using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 「すべて初期状態に戻す」のデータ側の処理。設定・履歴・キープ・削除した履歴をすべて消す。
/// ファイル（settings.json・history.json・deleted.json）と、メモリ上の履歴を同じタイミングで消す
/// （片方だけ先に消すと、その間の自動保存で古い内容が書き戻されるため）。
/// </summary>
public static class AppDataReset
{
    public static void Run(SettingsStorage settings, DataStore data, HistoryStore store)
    {
        store.Clear();
        data.DeleteHistory();
        settings.DeleteAndSuspend();
    }
}
