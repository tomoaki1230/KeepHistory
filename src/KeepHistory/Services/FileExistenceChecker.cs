using System;
using System.Collections.Generic;
using System.IO;

namespace KeepHistory.Services;

/// <summary>
/// 見つからないファイルの判定。
/// ネットワーク上のファイルは実在確認しない（応答しない共有への File.Exists は SMB のタイムアウトまで
/// ブロックし、UI スレッドで走る一覧の作り直しごと固まるため）。ネットワーク上のファイルは「ある」とみなす。
/// 1 回の走査ごとに new して使う（ドライブ種別をキャッシュするため）。
/// </summary>
public sealed class FileExistenceChecker
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, DriveType?> _driveTypeOf;
    private readonly Dictionary<string, DriveType?> _driveTypeCache = new(StringComparer.OrdinalIgnoreCase);

    public FileExistenceChecker()
        : this(File.Exists, DefaultDriveTypeOf)
    {
    }

    public FileExistenceChecker(Func<string, bool> fileExists, Func<string, DriveType?> driveTypeOf)
    {
        _fileExists = fileExists;
        _driveTypeOf = driveTypeOf;
    }

    public bool IsMissing(string path)
    {
        if (IsNetworkPath(path)) return false;
        return !_fileExists(path);
    }

    public bool IsNetworkPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('/', '\\');

        // \\?\ や \\.\ 形式（長いパス）
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            var rest = p.Substring(4);
            if (rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) return true;
            p = rest;
        }

        // UNC パス（\\server\share）
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        // ドライブレター：割り当てたネットワークドライブか
        if (p.Length >= 2 && p[1] == ':' && char.IsLetter(p[0]))
        {
            var root = char.ToUpperInvariant(p[0]) + @":\";
            if (!_driveTypeCache.TryGetValue(root, out var type))
            {
                type = _driveTypeOf(root);
                _driveTypeCache[root] = type;
            }
            return type == DriveType.Network;
        }
        return false;
    }

    private static DriveType? DefaultDriveTypeOf(string root)
    {
        try
        {
            // GetDriveType はドライブの応答を待たない
            return new DriveInfo(root).DriveType;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
