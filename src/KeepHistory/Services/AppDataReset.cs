using System;
using System.Collections.Generic;
using System.IO;

namespace KeepHistory.Services;

/// <summary>
/// 「すべて初期状態に戻す」のデータ側の処理。設定・履歴・キープ・削除した履歴をすべて消す。
/// ファイル（settings.json・history.json・deleted.json）と、メモリ上の履歴を同じタイミングで消す
/// （片方だけ先に消すと、その間の自動保存で古い内容が書き戻されるため）。
/// </summary>
public static class AppDataReset
{
    /// <summary>
    /// すべて消す。ファイルを消せなかったものがあっても残りは続け、消せなかったものの名前を返す（すべて消せたら空）。
    /// </summary>
    public static IReadOnlyList<string> Run(SettingsStorage settings, DataStore data, HistoryStore store)
    {
        var failures = new List<string>();

        // メモリ上の履歴は必ず空にする（ファイルを消せなくても、次の保存で空の状態が書かれる）
        store.Clear();
        try
        {
            data.DeleteHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("履歴のファイルを削除できませんでした。", ex);
            failures.Add("履歴（history.json・deleted.json）");
        }

        try
        {
            // 消せたときだけ自動保存を止める（消せなければ、既定値の自動保存で上書きさせる）
            settings.DeleteAndSuspend();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("設定ファイルを削除できませんでした。", ex);
            failures.Add("設定（settings.json）");
        }
        return failures;
    }
}
