using System;
using System.IO;

namespace KeepHistory.Tests.Framework;

/// <summary>
/// テスト用の作業フォルダ。リポジトリの temp/test-work/ の下に作り、Dispose で消す。
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory(string name)
    {
        Path = System.IO.Path.Combine(Root, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relative) => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "KeepHistory.sln")))
            {
                dir = dir.Parent;
            }
            // リポジトリが見つからなければ OS の一時フォルダを使う
            var root = dir != null
                ? System.IO.Path.Combine(dir.FullName, "temp", "test-work")
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KeepHistory-test-work");
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
