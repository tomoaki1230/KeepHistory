using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using KeepHistory.Interop;

namespace KeepHistory.Services;

/// <summary>「最近使った項目」の .lnk 1 件を解決した結果。</summary>
public sealed record RecentItem(string TargetPath, DateTime LastUsed);

/// <summary>
/// %APPDATA%\Microsoft\Windows\Recent の .lnk だけを収集源にする。
/// .lnk の最終更新日時を「前回利用日時」とする。フォルダへのリンクは対象外。
/// </summary>
public sealed class RecentFolderScanner
{
    private readonly IShortcutResolver _resolver;

    public RecentFolderScanner(string folder, IShortcutResolver resolver)
    {
        Folder = folder;
        _resolver = resolver;
    }

    public static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public string Folder { get; }

    /// <summary>フォルダ内の .lnk を一括走査する（バックグラウンドスレッドで呼んでよい）。</summary>
    public List<RecentItem> ScanAll()
    {
        var result = new List<RecentItem>();
        if (!Directory.Exists(Folder)) return result;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(Folder, "*.lnk", SearchOption.TopDirectoryOnly);
            foreach (var lnk in files)
            {
                var item = Read(lnk);
                if (item != null) result.Add(item);
            }
        }
        catch (IOException ex)
        {
            ErrorLog.Write("最近使った項目の走査に失敗しました。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorLog.Write("最近使った項目の走査に失敗しました。", ex);
        }
        return result;
    }

    /// <summary>.lnk 1 件を読む。対象外・読めない場合は null。</summary>
    public RecentItem? Read(string lnkPath)
    {
        if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (!File.Exists(lnkPath)) return null;
            var target = _resolver.Resolve(lnkPath);
            if (target == null || target.IsDirectory || string.IsNullOrWhiteSpace(target.Path)) return null;
            var lastUsed = File.GetLastWriteTime(lnkPath);
            return new RecentItem(target.Path, lastUsed);
        }
        catch (Exception ex)
        {
            // 壊れた・想定外の .lnk が 1 件あっても、ほかの .lnk の読み込みを止めない
            if (ex is not (IOException or UnauthorizedAccessException or COMException))
            {
                ErrorLog.Write($"最近使った項目を読めませんでした: {lnkPath}", ex);
            }
            return null;
        }
    }
}
