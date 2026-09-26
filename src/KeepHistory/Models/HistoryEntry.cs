using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeepHistory.Services;

namespace KeepHistory.Models;

/// <summary>履歴 1 件分。一覧の 1 行に対応する。</summary>
public sealed class HistoryEntry : INotifyPropertyChanged
{
    private DateTime _lastUsed;
    private bool _isKept;
    private bool _isMissing;
    private int _openCount;

    public HistoryEntry(string path, DateTime lastUsed, bool isKept = false, int openCount = 1)
    {
        Path = path;
        _lastUsed = lastUsed;
        _isKept = isKept;
        _openCount = Math.Max(1, openCount);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>実ファイルのフルパス（履歴のキー）。</summary>
    public string Path { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string FolderPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>小文字・ドット付きの拡張子。拡張子が無ければ空文字。</summary>
    public string Extension => PathText.ExtensionOf(Path);

    /// <summary>前回利用日時（.lnk の最終更新日時）。</summary>
    public DateTime LastUsed
    {
        get => _lastUsed;
        set
        {
            if (_lastUsed == value) return;
            _lastUsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastUsedText));
        }
    }

    /// <summary>表示用の日時文字列（InvariantCulture で整形）。</summary>
    public string LastUsedText => DateFormat.ToDisplay(_lastUsed);

    /// <summary>
    /// 開いた回数。「最近使った項目」の .lnk が新しい日時に更新されたのを検知した回数
    /// （KeepHistory が動いていない間に何度開いても、次の走査では 1 回と数える）。
    /// </summary>
    public int OpenCount
    {
        get => _openCount;
        set
        {
            if (_openCount == value) return;
            _openCount = value;
            OnPropertyChanged();
        }
    }

    /// <summary>★キープ。保持期限を過ぎても消さない。</summary>
    public bool IsKept
    {
        get => _isKept;
        set
        {
            if (_isKept == value) return;
            _isKept = value;
            OnPropertyChanged();
        }
    }

    /// <summary>ファイルが見つからない（淡色表示する。一覧からは消さない）。</summary>
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (_isMissing == value) return;
            _isMissing = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
