using System;
using System.Globalization;
using System.IO;

namespace KeepHistory.Services;

/// <summary>%APPDATA%\KeepHistory\error.log への簡易ログ。ログ出力の失敗は無視する。</summary>
public static class ErrorLog
{
    public static string? DirectoryOverride { get; set; }

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            var dir = DirectoryOverride ?? DataStore.DefaultDirectory;
            Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] {message}";
            if (ex != null) line += Environment.NewLine + ex;
            File.AppendAllText(Path.Combine(dir, "error.log"), line + Environment.NewLine);
        }
        catch (Exception)
        {
            // ログが書けなくてもアプリは止めない
        }
    }
}
