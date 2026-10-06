using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 履歴と「消した履歴の記憶」を持つ。UI スレッドからのみ操作すること。
/// </summary>
public sealed class HistoryStore
{
    private readonly Dictionary<string, HistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // 消した履歴の記憶。値の日時以前の利用記録は再登録しない（無いと次の走査で .lnk から復活する）。
    private readonly Dictionary<string, DeletedHistory> _deleted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>画面に渡す一覧。</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public IReadOnlyDictionary<string, DeletedHistory> Deleted => _deleted;

    public int RetentionDays { get; set; } = AppSettings.DefaultRetentionDays;

    public ExclusionFilter Exclusion { get; set; } = ExclusionFilter.Empty;

    public HistoryEntry? Find(string path)
        => _entries.TryGetValue(path, out var entry) ? entry : null;

    public bool IsExpired(DateTime lastUsed, DateTime now)
        => lastUsed < now.AddDays(-RetentionDays);

    /// <summary>
    /// 利用記録を登録する。新規追加または日時の更新があれば true。
    /// 新規なら開いた回数は 1、既存で日時が新しくなったら（開き直されたので）1 増やす。
    /// </summary>
    public bool Register(string path, DateTime lastUsed, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Exclusion.IsExcluded(path)) return false;

        if (_deleted.TryGetValue(path, out var deleted))
        {
            // 消した時点以前の記録なら復活させない。消した後に開き直されたら再登録する。
            if (lastUsed <= deleted.DeletedAt) return false;
            _deleted.Remove(path);
        }

        if (_entries.TryGetValue(path, out var existing))
        {
            if (lastUsed <= existing.LastUsed) return false;
            existing.LastUsed = lastUsed;
            existing.OpenCount++;
            return true;
        }

        if (IsExpired(lastUsed, now)) return false;

        var entry = new HistoryEntry(path, lastUsed);
        _entries.Add(path, entry);
        Entries.Add(entry);
        return true;
    }

    /// <summary>履歴から消し、deleted に記憶する（元に戻せるよう削除前の状態も覚える）。</summary>
    public void Remove(IEnumerable<HistoryEntry> items, DateTime now)
    {
        foreach (var item in items.ToList())
        {
            if (!_entries.Remove(item.Path)) continue;
            Entries.Remove(item);
            var threshold = item.LastUsed > now ? item.LastUsed : now;
            if (_deleted.TryGetValue(item.Path, out var old) && old.DeletedAt > threshold) threshold = old.DeletedAt;
            _deleted[item.Path] = new DeletedHistory(item.Path, threshold, item.LastUsed, item.OpenCount, item.IsKept);
        }
    }

    /// <summary>履歴・キープ・削除した履歴の記憶をすべて消す（「すべて初期状態に戻す」）。</summary>
    public void Clear()
    {
        _entries.Clear();
        _deleted.Clear();
        Entries.Clear();
    }

    /// <summary>
    /// 消した履歴を元に戻す。削除前の状態（前回利用日時・回数・キープ）が分かれば一覧に戻す。
    /// 分からない（旧版で消した）ものは記憶だけ消す（次の走査で .lnk が残っていれば一覧に戻る）。
    /// 一覧に戻した件数を返す。
    /// </summary>
    public int Restore(IEnumerable<string> paths)
    {
        int restored = 0;
        foreach (var path in paths.ToList())
        {
            if (!_deleted.Remove(path, out var deleted)) continue;
            if (deleted.LastUsed is not { } lastUsed || _entries.ContainsKey(path)) continue;
            var entry = new HistoryEntry(deleted.Path, lastUsed, deleted.IsKept, deleted.OpenCount);
            _entries.Add(entry.Path, entry);
            Entries.Add(entry);
            restored++;
        }
        return restored;
    }

    /// <summary>
    /// 保持期限を過ぎた履歴を消す（★キープは残す）。期限切れの deleted 記憶も捨てる
    /// （その日時以前の .lnk は期限切れで登録されないため、覚えておく必要がない）。
    /// 何か変わったら true。
    /// </summary>
    public bool Purge(DateTime now)
    {
        var expired = _entries.Values.Where(e => !e.IsKept && IsExpired(e.LastUsed, now)).ToList();
        RemoveWithoutRemember(expired);

        var staleDeleted = _deleted.Where(d => IsExpired(d.Value.DeletedAt, now)).Select(d => d.Key).ToList();
        foreach (var key in staleDeleted) _deleted.Remove(key);

        return expired.Count > 0 || staleDeleted.Count > 0;
    }

    /// <summary>
    /// 設定を変えたときに消える履歴の件数を数える（消さない）。キープした履歴は数えない。
    /// 除外で消えるものを先に数え、期限切れはそれ以外から数える（重ねて数えない）。
    /// </summary>
    public (int Excluded, int Expired) CountRemovals(ExclusionFilter exclusion, int retentionDays, DateTime now)
    {
        var limit = retentionDays >= (now - DateTime.MinValue).TotalDays ? DateTime.MinValue : now.AddDays(-retentionDays);
        int excluded = 0, expired = 0;
        foreach (var entry in _entries.Values)
        {
            if (entry.IsKept) continue;
            if (exclusion.IsExcluded(entry.Path)) excluded++;
            else if (entry.LastUsed < limit) expired++;
        }
        return (excluded, expired);
    }

    /// <summary>除外パターンに当たる履歴を消す（★キープは残す）。消した件数を返す。</summary>
    public int RemoveExcluded()
    {
        var excluded = _entries.Values.Where(e => !e.IsKept && Exclusion.IsExcluded(e.Path)).ToList();
        RemoveWithoutRemember(excluded);
        return excluded.Count;
    }

    /// <summary>実在確認をやり直す。状態が変わった件数を返す。</summary>
    public int RefreshMissing(FileExistenceChecker checker)
    {
        int changed = 0;
        foreach (var entry in _entries.Values)
        {
            var missing = checker.IsMissing(entry.Path);
            if (entry.IsMissing != missing)
            {
                entry.IsMissing = missing;
                changed++;
            }
        }
        return changed;
    }

    public void Load(IEnumerable<HistoryRecord>? records, IEnumerable<DeletedRecord>? deleted)
    {
        foreach (var r in records ?? Enumerable.Empty<HistoryRecord>())
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Path)) continue;
            if (!DateFormat.TryParseStorage(r.LastUsed, out var lastUsed)) continue;
            if (_entries.TryGetValue(r.Path, out var existing))
            {
                if (lastUsed > existing.LastUsed) existing.LastUsed = lastUsed;
                existing.IsKept |= r.IsKept;
                existing.OpenCount = Math.Max(existing.OpenCount, r.OpenCount);
                continue;
            }
            var entry = new HistoryEntry(r.Path, lastUsed, r.IsKept, r.OpenCount);
            _entries.Add(r.Path, entry);
            Entries.Add(entry);
        }

        foreach (var d in deleted ?? Enumerable.Empty<DeletedRecord>())
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Path)) continue;
            if (!DateFormat.TryParseStorage(d.DeletedAt, out var at)) continue;
            DateTime? lastUsed = DateFormat.TryParseStorage(d.LastUsed, out var parsed) ? parsed : null;
            if (!_deleted.TryGetValue(d.Path, out var old) || old.DeletedAt < at)
            {
                _deleted[d.Path] = new DeletedHistory(d.Path, at, lastUsed, Math.Max(1, d.OpenCount), d.IsKept);
            }
        }
    }

    /// <summary>
    /// 起動時に読めなかった history.json / deleted.json が読めるようになったとき、この起動中に記録した内容とまとめる。
    /// 消した記録（保存してあったもの・この起動中に消したもの）を優先し、それより前の利用記録は戻さない。
    /// キープはどちらかでキープなら残す。開いた回数は、保存してあった回数に、この起動中に増えた分を足す
    /// （この起動中に最初に記録した 1 回は、保存してあった日時より新しいときだけ数える。同じなら同じ 1 回）。
    /// </summary>
    public void MergeLoaded(IEnumerable<HistoryRecord>? records, IEnumerable<DeletedRecord>? deleted)
    {
        foreach (var d in deleted ?? Enumerable.Empty<DeletedRecord>())
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Path)) continue;
            if (!DateFormat.TryParseStorage(d.DeletedAt, out var at)) continue;
            if (_entries.TryGetValue(d.Path, out var current))
            {
                // 消した後に開き直されていれば、一覧に残して記録は捨てる（Register と同じ）
                if (current.LastUsed > at) continue;
                _entries.Remove(d.Path);
                Entries.Remove(current);
            }
            DateTime? lastUsed = DateFormat.TryParseStorage(d.LastUsed, out var parsed) ? parsed : null;
            if (!_deleted.TryGetValue(d.Path, out var old) || old.DeletedAt < at)
            {
                _deleted[d.Path] = new DeletedHistory(d.Path, at, lastUsed, Math.Max(1, d.OpenCount), d.IsKept);
            }
        }

        foreach (var r in records ?? Enumerable.Empty<HistoryRecord>())
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Path)) continue;
            if (!DateFormat.TryParseStorage(r.LastUsed, out var lastUsed)) continue;
            var count = Math.Max(1, r.OpenCount);
            if (_deleted.TryGetValue(r.Path, out var removed))
            {
                if (lastUsed <= removed.DeletedAt)
                {
                    // この起動中に消したもの。元に戻すときのために、保存してあった状態（回数・キープ）も覚える
                    _deleted[r.Path] = removed with
                    {
                        LastUsed = removed.LastUsed is { } l && l > lastUsed ? l : lastUsed,
                        OpenCount = Math.Max(removed.OpenCount, count),
                        IsKept = removed.IsKept || r.IsKept,
                    };
                    continue;
                }
                _deleted.Remove(r.Path);
            }
            if (_entries.TryGetValue(r.Path, out var current))
            {
                current.IsKept |= r.IsKept;
                var added = current.OpenCount - 1 + (current.FirstLastUsed > lastUsed ? 1 : 0);
                current.OpenCount = count + added;
                if (lastUsed > current.LastUsed) current.LastUsed = lastUsed;
                continue;
            }
            var entry = new HistoryEntry(r.Path, lastUsed, r.IsKept, count);
            _entries.Add(r.Path, entry);
            Entries.Add(entry);
        }
    }

    public List<HistoryRecord> ToHistoryRecords()
        => _entries.Values
            .OrderByDescending(e => e.LastUsed)
            .Select(e => new HistoryRecord { Path = e.Path, LastUsed = DateFormat.ToStorage(e.LastUsed), IsKept = e.IsKept, OpenCount = e.OpenCount })
            .ToList();

    public List<DeletedRecord> ToDeletedRecords()
        => _deleted
            .OrderByDescending(d => d.Value.DeletedAt)
            .Select(d => new DeletedRecord
            {
                Path = d.Key,
                DeletedAt = DateFormat.ToStorage(d.Value.DeletedAt),
                LastUsed = d.Value.LastUsed is { } lastUsed ? DateFormat.ToStorage(lastUsed) : null,
                OpenCount = d.Value.OpenCount,
                IsKept = d.Value.IsKept,
            })
            .ToList();

    private void RemoveWithoutRemember(IEnumerable<HistoryEntry> items)
    {
        foreach (var item in items)
        {
            _entries.Remove(item.Path);
            Entries.Remove(item);
        }
    }
}
