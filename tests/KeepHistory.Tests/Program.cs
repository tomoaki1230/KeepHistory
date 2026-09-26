using System;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests;

internal static class Program
{
    /// <summary>引数にテスト名の一部を渡すと、それを含むテストだけを実行する。</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // テスト中のエラーログを本物のデータフォルダ（%APPDATA%\KeepHistory）に書かない
        using var logDirectory = new TempDirectory("error-log");
        ErrorLog.DirectoryOverride = logDirectory.Path;
        return TestRunner.Run(typeof(Program).Assembly, args);
    }
}
