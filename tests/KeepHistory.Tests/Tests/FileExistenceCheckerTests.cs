using System.Collections.Generic;
using System.IO;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class FileExistenceCheckerTests
{
    private readonly List<string> _checkedPaths = new();

    private FileExistenceChecker NewChecker(DriveType zDriveType = DriveType.Network)
        => new(path =>
        {
            _checkedPaths.Add(path);
            return false;
        }, root => root.StartsWith("Z") ? zDriveType : DriveType.Fixed);

    [Test]
    public void NetworkFiles_AreNeverCheckedForExistence()
    {
        // 応答しない共有への File.Exists は SMB のタイムアウトまでブロックする
        var checker = NewChecker();
        Assert.False(checker.IsMissing(@"\\fileserver\share\a.xlsx"), "UNC は見つからない扱いにしない");
        Assert.False(checker.IsMissing(@"\\?\UNC\fileserver\share\a.xlsx"));
        Assert.False(checker.IsMissing(@"//fileserver/share/a.xlsx"));
        Assert.False(checker.IsMissing(@"Z:\share\a.xlsx"), "割り当てたネットワークドライブ");
        Assert.Equal(0, _checkedPaths.Count, "ネットワーク上のファイルに File.Exists を呼ばない");
    }

    [Test]
    public void LocalFiles_AreChecked()
    {
        var checker = NewChecker();
        Assert.True(checker.IsMissing(@"C:\gone.txt"));
        Assert.True(checker.IsMissing(@"\\?\C:\very\long\gone.txt"));
        Assert.Equal(2, _checkedPaths.Count);
    }

    [Test]
    public void DriveTypeIsCachedPerChecker()
    {
        int calls = 0;
        var checker = new FileExistenceChecker(_ => true, _ =>
        {
            calls++;
            return DriveType.Fixed;
        });
        checker.IsMissing(@"C:\a.txt");
        checker.IsMissing(@"c:\b.txt");
        Assert.Equal(1, calls);
    }
}
