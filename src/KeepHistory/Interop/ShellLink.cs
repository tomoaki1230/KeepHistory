using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace KeepHistory.Interop;

/// <summary>.lnk の解決結果。</summary>
public sealed record ShortcutTarget(string Path, bool IsDirectory);

public interface IShortcutResolver
{
    /// <summary>.lnk のリンク先を返す。ファイルシステム上のパスを持たないリンクは null。</summary>
    ShortcutTarget? Resolve(string lnkPath);
}

/// <summary>
/// IShellLinkW で .lnk を解決する。
/// IShellLink.Resolve は呼ばない（リンク先を探しに行き、ネットワークで固まるため）。
/// GetPath は .lnk に記録されたパスと属性を返すだけでリンク先にはアクセスしない。
/// </summary>
public sealed class ShellLinkResolver : IShortcutResolver
{
    private const int MaxPath = 32768;
    private const uint FileAttributeDirectory = 0x10;
    private const int StgmRead = 0x00000000;

    public ShortcutTarget? Resolve(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(lnkPath, StgmRead);
            var buffer = new StringBuilder(MaxPath);
            ((IShellLinkW)link).GetPath(buffer, buffer.Capacity, out var data, 0);
            var path = buffer.ToString();
            if (string.IsNullOrWhiteSpace(path)) return null;
            return new ShortcutTarget(path, (data.dwFileAttributes & FileAttributeDirectory) != 0);
        }
        finally
        {
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>.lnk を作成する（テスト用）。</summary>
    internal static void Create(string lnkPath, string targetPath)
    {
        object link = new CShellLink();
        try
        {
            ((IShellLinkW)link).SetPath(targetPath);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindDataW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out Win32FindDataW pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
