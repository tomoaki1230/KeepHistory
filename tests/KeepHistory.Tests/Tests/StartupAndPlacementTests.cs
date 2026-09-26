using System;
using System.IO;
using System.Windows;
using KeepHistory.Interop;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using Microsoft.Win32;

namespace KeepHistory.Tests.Tests;

public sealed class StartupAndPlacementTests : IDisposable
{
    // 本物のスタートアップ（Run）には触れず、テスト用のキーを使う
    private const string TestKey = @"Software\KeepHistory.Tests\Run";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\KeepHistory.Tests", throwOnMissingSubKey: false);

    [Test]
    public void RunKey_EnableAndDisable()
    {
        var startup = new RunKeyStartupRegistration(TestKey);
        Assert.Null(startup.GetCommand(), "最初は登録なし");
        startup.Enable(@"""C:\App\KeepHistory.exe"" --tray");
        Assert.Equal(@"""C:\App\KeepHistory.exe"" --tray", startup.GetCommand());
        startup.Disable();
        Assert.Null(startup.GetCommand(), "解除できる");
        startup.Disable();
        Assert.Null(startup.GetCommand(), "登録が無くても解除で失敗しない");
    }

    [Test]
    public void Command_BuildAndExtract()
    {
        Assert.Equal(@"""C:\Program Files\KH\KeepHistory.exe"" --tray", StartupCommand.Build(@"C:\Program Files\KH\KeepHistory.exe"),
            "空白を含むパスでも動くよう引用符で囲み、画面を出さずに始める");
        Assert.Equal(@"C:\Program Files\KH\KeepHistory.exe", StartupCommand.ExtractExePath(@"""C:\Program Files\KH\KeepHistory.exe"" --tray"));
        Assert.Equal(@"C:\KH\KeepHistory.exe", StartupCommand.ExtractExePath(@"C:\KH\KeepHistory.exe --tray"));
        Assert.Null(StartupCommand.ExtractExePath(null));
    }

    [Test]
    public void ExecutablePath_UsesAppExe_EvenWhenRunByDotnet()
    {
        using var dir = new TempDirectory("startup-exe");
        Assert.Equal(@"C:\App\KeepHistory.exe", StartupCommand.ExecutablePath(@"C:\App\KeepHistory.exe", dir.Path));
        Assert.Null(StartupCommand.ExecutablePath(@"C:\Program Files\dotnet\dotnet.exe", dir.Path), "dotnet.exe では起動できないので登録しない");
        File.WriteAllText(dir.Combine("KeepHistory.exe"), "x");
        Assert.Equal(dir.Combine("KeepHistory.exe"), StartupCommand.ExecutablePath(@"C:\Program Files\dotnet\dotnet.exe", dir.Path),
            "dotnet.exe 経由なら同じフォルダの KeepHistory.exe を使う");
    }

    [Test]
    public void CenterIn_CentersAndKeepsTitleBarOnScreen()
    {
        var work = new Rect(1920, 0, 1920, 1040);
        Assert.Equal(new Point(1920 + 480, 220), WindowPlacementCalculator.CenterIn(work, new Size(960, 600)), "作業領域の中央");
        Assert.Equal(new Point(1920, 0), WindowPlacementCalculator.CenterIn(work, new Size(3000, 2000)), "大きすぎたら左上をそろえる");
    }

    [Test]
    public void IsReachable_DetectsOffScreenPositions()
    {
        var areas = new[] { new Rect(0, 0, 1920, 1040) };
        Assert.True(WindowPlacementCalculator.IsReachable(new Rect(100, 100, 960, 600), areas));
        Assert.True(WindowPlacementCalculator.IsReachable(new Rect(1500, 500, 960, 600), areas), "一部がはみ出していてもタイトルバーをつかめればよい");
        Assert.False(WindowPlacementCalculator.IsReachable(new Rect(2500, 100, 960, 600), areas), "外したモニターの位置");
        Assert.False(WindowPlacementCalculator.IsReachable(new Rect(100, -300, 960, 600), areas), "タイトルバーが画面の上に出ている");
    }
}
