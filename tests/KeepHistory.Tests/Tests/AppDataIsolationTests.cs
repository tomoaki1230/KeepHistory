using System;
using System.IO;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class AppDataIsolationTests
{
    [Test]
    public void ErrorLog_DuringTests_IsNotWrittenToRealAppData()
    {
        var real = Path.GetFullPath(DataStore.DefaultDirectory);
        var current = Assert.NotNull(ErrorLog.DirectoryOverride, "テスト中はログの書き先を差し替えている");
        Assert.False(Path.GetFullPath(current).StartsWith(real, StringComparison.OrdinalIgnoreCase),
            $"本物のデータフォルダ（{real}）に書かない");
    }
}
