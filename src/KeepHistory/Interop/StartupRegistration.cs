using System;
using System.IO;
using Microsoft.Win32;

namespace KeepHistory.Interop;

/// <summary>Windows の起動時に起動する登録。</summary>
public interface IStartupRegistration
{
    /// <summary>登録されているコマンド。登録が無ければ null。</summary>
    string? GetCommand();

    void Enable(string command);

    void Disable();
}

/// <summary>
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run への登録（ユーザーごと。管理者権限は要らない）。
/// テストでは別のキーを指定して、本物の登録に触れない。
/// </summary>
public sealed class RunKeyStartupRegistration : IStartupRegistration
{
    public const string DefaultKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "KeepHistory";

    private readonly string _keyPath;
    private readonly string _valueName;

    public RunKeyStartupRegistration(string keyPath = DefaultKeyPath, string valueName = DefaultValueName)
    {
        _keyPath = keyPath;
        _valueName = valueName;
    }

    public string? GetCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(_valueName) as string;
    }

    public void Enable(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        key.SetValue(_valueName, command, RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }
}

/// <summary>起動時に登録するコマンドの組み立て。</summary>
public static class StartupCommand
{
    public const string ExeName = "KeepHistory.exe";

    /// <summary>
    /// 登録する exe のパス。dotnet.exe 経由で動いているとき（開発時）は、同じフォルダの KeepHistory.exe を使う。見つからなければ null。
    /// </summary>
    public static string? ExecutablePath(string? processPath, string baseDirectory)
    {
        if (string.IsNullOrEmpty(processPath)) return null;
        if (string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(baseDirectory, ExeName);
            return File.Exists(candidate) ? candidate : null;
        }
        return processPath;
    }

    /// <summary>画面を出さずに始める（--tray。常駐しない設定なら画面を出す）。</summary>
    public static string Build(string exePath) => $"\"{exePath}\" --tray";

    /// <summary>登録されたコマンドから exe のパスを取り出す。</summary>
    public static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text.Substring(1, end - 1) : null;
        }
        var space = text.IndexOf(' ');
        return space < 0 ? text : text.Substring(0, space);
    }
}
