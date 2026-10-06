using System;
using System.Collections.Generic;
using System.IO;

namespace KeepHistory.Services;

/// <summary>
/// 見つからないファイルの判定。
/// ネットワーク上のファイルは実在確認しない（応答しない共有への File.Exists は SMB のタイムアウトまで
/// ブロックし、UI スレッドで走る一覧の作り直しごと固まるため）。ネットワーク上のファイルは「ある」とみなす。
/// 再接続する設定のネットワークドライブで、まだドライブ文字が使えない（サインイン直後で再接続前・切断中）ものも
/// ネットワークとみなす（「見つからない」扱いにすると開こうともしなくなるが、開けば Windows が再接続する）。
/// 1 回の走査ごとに new して使う（ドライブ種別をキャッシュするため）。
/// </summary>
public sealed class FileExistenceChecker
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, DriveType?> _driveTypeOf;
    private readonly Func<char, bool> _isRememberedNetworkDrive;
    private readonly Dictionary<string, bool> _networkDriveCache = new(StringComparer.OrdinalIgnoreCase);

    public FileExistenceChecker()
        : this(File.Exists, DefaultDriveTypeOf, IsRememberedNetworkDrive)
    {
    }

    /// <param name="isRememberedNetworkDrive">再接続する設定のネットワークドライブか（ドライブ文字。大文字）。</param>
    public FileExistenceChecker(Func<string, bool> fileExists, Func<string, DriveType?> driveTypeOf, Func<char, bool>? isRememberedNetworkDrive = null)
    {
        _fileExists = fileExists;
        _driveTypeOf = driveTypeOf;
        _isRememberedNetworkDrive = isRememberedNetworkDrive ?? (_ => false);
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
            var letter = char.ToUpperInvariant(p[0]);
            var root = letter + @":\";
            if (!_networkDriveCache.TryGetValue(root, out var network))
            {
                var type = _driveTypeOf(root);
                // ドライブ文字が使えない（NoRootDirectory）ときは、再接続する設定のネットワークドライブか確かめる
                network = type == DriveType.Network
                          || ((type is DriveType.NoRootDirectory or null) && _isRememberedNetworkDrive(letter));
                _networkDriveCache[root] = network;
            }
            return network;
        }
        return false;
    }

    /// <summary>HKCU\Network\{ドライブ文字} があるか（「サインイン時に再接続する」で割り当てたネットワークドライブ）。</summary>
    private static bool IsRememberedNetworkDrive(char letter)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Network\" + letter);
            return key != null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
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
