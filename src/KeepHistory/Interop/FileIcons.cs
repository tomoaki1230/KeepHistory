using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeepHistory.Interop;

/// <summary>
/// ファイルの種類のアイコン（エクスプローラーと同じ小さいアイコン）。
/// 拡張子だけから求める（SHGFI_USEFILEATTRIBUTES）ので、ファイル自体にはアクセスしない
/// （ネットワーク上のファイルでも待たされない。見つからないファイルにも出せる）。
/// そのため .exe・.lnk・.ico など、ファイルごとにアイコンが違う種類は、その種類の共通のアイコンになる。
/// 拡張子ごとに一度だけ作って使い回す。UI スレッドで使うこと。
/// </summary>
public static class FileIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>アイコンを作った回数（テスト用。使い回しの確認）。</summary>
    internal static int CreatedCount { get; private set; }

    /// <summary>拡張子（ドット付き。無ければ空文字）の小さいアイコン。取れなければ null。</summary>
    public static ImageSource? SmallIconFor(string? extension)
    {
        var key = extension ?? string.Empty;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var icon = Create(key);
        Cache[key] = icon;
        return icon;
    }

    private static ImageSource? Create(string extension)
    {
        CreatedCount++;
        // 実在しない名前でよい（属性と名前だけで種類を判断させる）
        var name = "file" + extension;
        var info = new ShFileInfo();
        var result = SHGetFileInfo(name, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiSmallIcon = 0x1;
    private const uint ShgfiUseFileAttributes = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
