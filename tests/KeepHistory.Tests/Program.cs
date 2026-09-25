using System;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests;

internal static class Program
{
    /// <summary>引数にテスト名の一部を渡すと、それを含むテストだけを実行する。</summary>
    [STAThread]
    private static int Main(string[] args) => TestRunner.Run(typeof(Program).Assembly, args);
}
