using System;
using System.IO;
using System.Runtime.CompilerServices;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests;

internal static class Program
{
    /// <summary>
    /// Windows のアプリ制御（Smart App Control）がアプリの dll の読み込みを止めたときの終了コード。
    /// テストの csproj は、この終了コードなら識別子を変えてビルドし直し、同じテストをもう一度動かす。
    /// </summary>
    public const int BlockedByAppControl = 3;

    private const int HResultBlockedByPolicy = unchecked((int)0x800711C7);

    /// <summary>引数にテスト名の一部を渡すと、それを含むテストだけを実行する。</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // 「止められた」の知らせも文字化けしないよう、最初に出力の文字コードを決める（csproj は UTF-8 で読む）
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // KeepHistory.dll の型はここでは使わない（読み込みを止められたとき、Main 自体が動かなくなるため）
        try
        {
            if (SimulateBlock()) return BlockedByAppControl;
            return Run(args);
        }
        catch (FileLoadException ex) when (ex.HResult == HResultBlockedByPolicy)
        {
            Console.WriteLine($"[ブロック] Windows のアプリ制御（Smart App Control）が読み込みを止めました: {ex.FileName}");
            return BlockedByAppControl;
        }
    }

    /// <summary>
    /// 止められたときの作り直し（テストの csproj）を確かめるための仕組み。環境変数 KEEPHISTORY_SIMULATE_BLOCK_FILE に
    /// 回数を書いたファイルを指定すると、その回数だけ「止められた」として終わる（1 回ごとに 1 減らす）。「crash:回数」なら、
    /// テストの dll そのものを止められたときと同じ出力で異常終了する。ふだんは何もしない。
    /// </summary>
    private static bool SimulateBlock()
    {
        var file = Environment.GetEnvironmentVariable("KEEPHISTORY_SIMULATE_BLOCK_FILE");
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
        // 「回数」なら終了コード 3 で、「crash:回数」ならテストの dll を止められたときと同じ出力で異常終了する
        var text = File.ReadAllText(file).Trim();
        var crash = text.StartsWith("crash:", StringComparison.Ordinal);
        if (!int.TryParse(crash ? text.Substring(6) : text, out var remaining) || remaining <= 0) return false;
        File.WriteAllText(file, (crash ? "crash:" : string.Empty) + (remaining - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (crash)
        {
            Console.WriteLine("Unhandled exception. System.IO.FileLoadException: （確認用）KeepHistory.Tests.dll (0x800711C7)");
            Environment.Exit(unchecked((int)0xE0434352));
        }
        Console.WriteLine("[ブロック] （確認用に、止められたことにしました）");
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        // テスト中のエラーログを本物のデータフォルダ（%APPDATA%\KeepHistory）に書かない
        using var logDirectory = new TempDirectory("error-log");
        ErrorLog.DirectoryOverride = logDirectory.Path;
        return TestRunner.Run(typeof(Program).Assembly, args);
    }
}
