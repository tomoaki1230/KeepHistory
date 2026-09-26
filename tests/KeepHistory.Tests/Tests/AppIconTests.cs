using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using KeepHistory.Interop;
using KeepHistory.Tests.Framework;
using Drawing = System.Drawing;

namespace KeepHistory.Tests.Tests;

public sealed class AppIconTests
{
    public AppIconTests() => UiTestHost.EnsureApplication();

    /// <summary>.ico のヘッダーから格納サイズを読む（256 は 0 で記録される）。</summary>
    private static List<int> ReadIcoSizes()
    {
        using var stream = AppIcon.OpenStream();
        using var reader = new BinaryReader(stream);
        Assert.Equal((short)0, reader.ReadInt16());
        Assert.Equal((short)1, reader.ReadInt16(), ".ico 形式");
        int count = reader.ReadInt16();
        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            reader.ReadBytes(15);
            sizes.Add(width == 0 ? 256 : width);
        }
        return sizes;
    }

    [Test]
    public void Icon_ContainsSizesForTrayWindowAndExplorer()
    {
        var sizes = ReadIcoSizes();
        foreach (var required in new[] { 16, 20, 24, 32, 40, 48, 256 })
        {
            Assert.True(sizes.Contains(required), $"{required}px が入っていること（格納: {string.Join(",", sizes)}）");
        }
    }

    [Test]
    public void TrayIcon_LoadsExactSmallSizes()
    {
        // 通知領域は画面の拡大率により 16/20/24px などを使う。拡大・縮小せずに読めること
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            using var icon = AppIcon.LoadIcon(new Drawing.Size(size, size));
            Assert.Equal(size, icon.Width, $"{size}px");
        }
        Assert.NotNull(AppIcon.GetIcon());
    }

    [Test]
    public void WindowIcon_KeepsAllFramesForWpf()
    {
        // WPF はデコーダーの全フレームからタイトルバー用・タスクバー用に最適なサイズを選ぶ
        var frame = Assert.NotNull(AppIcon.GetImageSource() as BitmapFrame);
        Assert.True(Assert.NotNull(frame.Decoder).Frames.Count >= 9);
    }
}
