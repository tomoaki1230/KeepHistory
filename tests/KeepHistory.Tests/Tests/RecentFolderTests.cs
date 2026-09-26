using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeepHistory.Interop;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class RecentFolderTests : IDisposable
{
    private readonly TempDirectory _dir = new("recent");

    public void Dispose() => _dir.Dispose();

    private sealed class FakeResolver : IShortcutResolver
    {
        public Dictionary<string, ShortcutTarget?> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ShortcutTarget? Resolve(string lnkPath)
            => Map.TryGetValue(Path.GetFileName(lnkPath), out var t) ? t : null;
    }

    private string CreateLink(string name, DateTime lastWrite)
    {
        var path = _dir.Combine(name);
        File.WriteAllText(path, "dummy");
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }

    [Test]
    public void ScanAll_UsesLinkLastWriteTimeAndSkipsFoldersAndNonLinks()
    {
        var time = new DateTime(2026, 9, 20, 9, 15, 0);
        CreateLink("report.docx.lnk", time);
        CreateLink("Projects.lnk", time);
        CreateLink("virtual.lnk", time);
        CreateLink("readme.txt", time);
        var resolver = new FakeResolver();
        resolver.Map["report.docx.lnk"] = new ShortcutTarget(@"C:\Docs\report.docx", false);
        resolver.Map["Projects.lnk"] = new ShortcutTarget(@"C:\Projects", true);
        resolver.Map["virtual.lnk"] = null; // コントロールパネルなどパスを持たないリンク

        var items = new RecentFolderScanner(_dir.Path, resolver).ScanAll();

        Assert.Equal(1, items.Count);
        Assert.Equal(@"C:\Docs\report.docx", items[0].TargetPath);
        Assert.Equal(time, items[0].LastUsed, ".lnk の最終更新日時が前回利用日時");
    }

    private sealed class ThrowingResolver : IShortcutResolver
    {
        public ShortcutTarget? Resolve(string lnkPath)
            => Path.GetFileName(lnkPath) == "bad.lnk"
                ? throw new InvalidOperationException("想定外の例外")
                : new ShortcutTarget(@"C:\ok\" + Path.GetFileNameWithoutExtension(lnkPath), false);
    }

    [Test]
    public void ScanAll_OneBrokenLink_DoesNotStopTheOthers()
    {
        var time = new DateTime(2026, 9, 20, 9, 15, 0);
        CreateLink("a.lnk", time);
        CreateLink("bad.lnk", time);
        CreateLink("z.lnk", time);

        var items = new RecentFolderScanner(_dir.Path, new ThrowingResolver()).ScanAll();

        Assert.SequenceEqual(new[] { @"C:\ok\a", @"C:\ok\z" }, items.Select(i => i.TargetPath).OrderBy(p => p),
            "想定外のエラーを起こす .lnk は読み飛ばし、ほかは読み込む");
    }

    [Test]
    public void ScanAll_MissingFolderReturnsEmpty()
    {
        var scanner = new RecentFolderScanner(_dir.Combine("nothing"), new FakeResolver());
        Assert.Equal(0, scanner.ScanAll().Count);
    }

    [Test]
    public void ShellLinkResolver_ResolvesRealShortcut()
    {
        // 実物の .lnk を IShellLinkW で作って解決する
        var target = _dir.Combine("実ファイル.txt");
        File.WriteAllText(target, "x");
        var folder = _dir.Combine("フォルダ");
        Directory.CreateDirectory(folder);
        var fileLink = _dir.Combine("file.lnk");
        var folderLink = _dir.Combine("folder.lnk");
        ShellLinkResolver.Create(fileLink, target);
        ShellLinkResolver.Create(folderLink, folder);

        var resolver = new ShellLinkResolver();
        var resolvedFile = Assert.NotNull(resolver.Resolve(fileLink));
        Assert.Equal(target, resolvedFile.Path, "リンク先のパス");
        Assert.False(resolvedFile.IsDirectory);
        Assert.True(Assert.NotNull(resolver.Resolve(folderLink)).IsDirectory);

        var items = new RecentFolderScanner(_dir.Path, resolver).ScanAll();
        Assert.SequenceEqual(new[] { target }, items.Select(i => i.TargetPath));
    }
}
