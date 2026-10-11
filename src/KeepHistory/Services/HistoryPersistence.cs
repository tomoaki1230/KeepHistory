using System;
using System.Collections.Generic;
using System.IO;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 履歴（history.json・deleted.json）の読み込みと保存。
/// 起動時に一時的に読めなければ（ネットワークの切断・ほかのソフトがつかんでいるなど）空で始め、読めるまで保存しない
/// （空に近い内容で上書きすると、保存してあった履歴・キープが消えるため。3.9）。読めるようになったら、この起動中の記録とまとめて保存する。
/// UI スレッドで使うこと（ReadForRecovery だけはバックグラウンドで呼んでよい）。
/// </summary>
public sealed class HistoryPersistence
{
    private readonly DataStore _data;
    private readonly HistoryStore _store;

    public HistoryPersistence(DataStore data, HistoryStore store)
    {
        _data = data;
        _store = store;
    }

    /// <summary>起動時に読めず、まだ読み直せていないか（この間は保存しない）。</summary>
    public bool LoadFailed { get; private set; }

    /// <summary>
    /// 起動時に読む。history.json と deleted.json は両方読めたときだけ使う（片方だけだと、後でまとめるときに重なる）。
    /// </summary>
    public void LoadAtStartup()
    {
        try
        {
            var history = _data.LoadHistory();
            var deleted = _data.LoadDeleted();
            _store.Load(history, deleted);
        }
        catch (DataUnavailableException ex)
        {
            LoadFailed = true;
            ErrorLog.Write("履歴を一時的に読み込めないため、空の状態で起動します。読み込めるまで履歴は保存しません。", ex);
        }
    }

    /// <summary>保存する。読めていない間は保存しない。保存に失敗しても例外にしない（記録して、次の保存で書く）。</summary>
    public void Save()
    {
        if (LoadFailed) return;
        try
        {
            _data.SaveHistory(_store.ToHistoryRecords());
            _data.SaveDeleted(_store.ToDeletedRecords());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("履歴を保存できませんでした。", ex);
        }
    }

    /// <summary>読み直す（バックグラウンドで呼んでよい）。まだ読めなければ IOException（DataUnavailableException など）。</summary>
    public (List<HistoryRecord> History, List<DeletedRecord> Deleted) ReadForRecovery()
        => (_data.LoadHistory(), _data.LoadDeleted());

    /// <summary>
    /// 読み直した内容を、この起動中の記録とまとめて保存する（UI スレッドで）。除外・期限切れも消す。
    /// 読み直している間に「すべて初期状態に戻す」をした（LoadFailed が下りている）なら何もしない。まとめたら true。
    /// </summary>
    public bool ApplyRecovered(List<HistoryRecord> history, List<DeletedRecord> deleted, DateTime now)
    {
        if (!LoadFailed) return false;
        _store.MergeLoaded(history, deleted);
        LoadFailed = false;
        ErrorLog.Write("履歴を読み込めるようになりました。この起動中の記録とまとめて保存します。");
        _store.RemoveExcluded();
        _store.Purge(now);
        Save();
        return true;
    }

    /// <summary>「すべて初期状態に戻す」をした。読めなかった履歴を後で読み直してまとめることはしない。</summary>
    public void ForgetLoadFailure() => LoadFailed = false;
}

/// <summary>
/// 最近の検索語（searches.json）の読み込みと保存。起動時に一時的に読めなければ空で始め、この起動中は保存しない
/// （上書きして消さないため。読み直しはしない）。0 件になったらファイルを消す。
/// </summary>
public sealed class RecentSearchPersistence
{
    private readonly DataStore _data;
    private readonly RecentSearchList _list;

    public RecentSearchPersistence(DataStore data, RecentSearchList list)
    {
        _data = data;
        _list = list;
    }

    /// <summary>起動時に読めなかったか（この起動中は保存しない）。</summary>
    public bool LoadFailed { get; private set; }

    public void Load()
    {
        try
        {
            _list.Load(_data.LoadRecentSearches());
        }
        catch (DataUnavailableException ex)
        {
            LoadFailed = true;
            ErrorLog.Write("最近の検索語を一時的に読み込めないため、この起動中は保存しません。", ex);
        }
    }

    public void Save()
    {
        if (LoadFailed) return;
        try
        {
            if (_list.Items.Count == 0)
            {
                _data.DeleteRecentSearches();
            }
            else
            {
                _data.SaveRecentSearches(_list.ToList());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("最近の検索語を保存できませんでした。", ex);
        }
    }

    /// <summary>「すべて初期状態に戻す」をした（ファイルは消してある）。以後は保存してよい。</summary>
    public void ForgetLoadFailure() => LoadFailed = false;
}
