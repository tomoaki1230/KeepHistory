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
    private readonly Dictionary<string, DateTime> _deleted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>画面に渡す一覧。</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    public IReadOnlyDictionary<string, DateTime> Deleted => _deleted;

    public int RetentionDays { get; set; } = AppSettings.DefaultRetentionDays;

    public ExclusionFilter Exclusion { get; set; } = ExclusionFilter.Empty;

    public HistoryEntry? Find(string path)
        => _entries.TryGetValue(path, out var entry) ? entry : null;

    public bool IsExpired(DateTime lastUsed, DateTime now)
        => lastUsed < now.AddDays(-RetentionDays);

    /// <summary>
    /// 利用記録を登録する。新規追加または日時の更新があれば true。
    /// </summary>
    public bool Register(string path, DateTime lastUsed, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Exclusion.IsExcluded(path)) return false;

        if (_deleted.TryGetValue(path, out var deletedAt))
        {
            // 消した時点以前の記録なら復活させない。消した後に開き直されたら再登録する。
            if (lastUsed <= deletedAt) return false;
            _deleted.Remove(path);
        }

        if (_entries.TryGetValue(path, out var existing))
        {
            if (lastUsed <= existing.LastUsed) return false;
            existing.LastUsed = lastUsed;
            return true;
        }

        if (IsExpired(lastUsed, now)) return false;

        var entry = new HistoryEntry(path, lastUsed);
        _entries.Add(path, entry);
        Entries.Add(entry);
        return true;
    }

    /// <summary>履歴から消し、deleted に記憶する。</summary>
    public void Remove(IEnumerable<HistoryEntry> items, DateTime now)
    {
        foreach (var item in items.ToList())
        {
            if (!_entries.Remove(item.Path)) continue;
            Entries.Remove(item);
            var threshold = item.LastUsed > now ? item.LastUsed : now;
            if (!_deleted.TryGetValue(item.Path, out var old) || old < threshold)
            {
                _deleted[item.Path] = threshold;
            }
        }
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

        var staleDeleted = _deleted.Where(d => IsExpired(d.Value, now)).Select(d => d.Key).ToList();
        foreach (var key in staleDeleted) _deleted.Remove(key);

        return expired.Count > 0 || staleDeleted.Count > 0;
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
                continue;
            }
            var entry = new HistoryEntry(r.Path, lastUsed, r.IsKept);
            _entries.Add(r.Path, entry);
            Entries.Add(entry);
        }

        foreach (var d in deleted ?? Enumerable.Empty<DeletedRecord>())
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Path)) continue;
            if (!DateFormat.TryParseStorage(d.DeletedAt, out var at)) continue;
            if (!_deleted.TryGetValue(d.Path, out var old) || old < at) _deleted[d.Path] = at;
        }
    }

    public List<HistoryRecord> ToHistoryRecords()
        => _entries.Values
            .OrderByDescending(e => e.LastUsed)
            .Select(e => new HistoryRecord { Path = e.Path, LastUsed = DateFormat.ToStorage(e.LastUsed), IsKept = e.IsKept })
            .ToList();

    public List<DeletedRecord> ToDeletedRecords()
        => _deleted
            .OrderByDescending(d => d.Value)
            .Select(d => new DeletedRecord { Path = d.Key, DeletedAt = DateFormat.ToStorage(d.Value) })
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
