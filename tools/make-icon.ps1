<#
  元画像（PNG）から複数サイズ入りの .ico を作る。Windows PowerShell で実行する。
    powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
  元画像: src\KeepHistory\Assets\icon.png
  出力  : src\KeepHistory\Assets\KeepHistory.ico
  透明な余白は切り詰めてから正方形にする。256px は PNG 圧縮、それ未満は通常のビットマップで格納する
  （System.Drawing.Icon や古い API でも小さいサイズを読めるように）。
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\src\KeepHistory\Assets\icon.png"),
    [string]$Output = (Join-Path $PSScriptRoot "..\src\KeepHistory\Assets\KeepHistory.ico")
)
$ErrorActionPreference = "Stop"

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IconMaker
{
    public static void Make(string source, string output, int[] sizes)
    {
        using (var original = new Bitmap(source))
        {
            Rectangle bounds = AlphaBounds(original);
            int side = Math.Max(bounds.Width, bounds.Height);
            side += (int)(side * 0.04);
            using (var square = new Bitmap(side, side, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(square))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(original,
                        new Rectangle((side - bounds.Width) / 2, (side - bounds.Height) / 2, bounds.Width, bounds.Height),
                        bounds, GraphicsUnit.Pixel);
                }
                var images = new List<byte[]>();
                foreach (int size in sizes)
                {
                    using (var bmp = Resize(square, size))
                    {
                        images.Add(size >= 256 ? ToPng(bmp) : ToDib(bmp));
                    }
                }
                WriteIco(output, sizes, images);
            }
        }
    }

    private static Rectangle AlphaBounds(Bitmap bmp)
    {
        byte[] px = Pixels(bmp);
        int w = bmp.Width, h = bmp.Height;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] > 8)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        if (maxX < 0) return new Rectangle(0, 0, w, h);
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    // 大きく縮めるときは半分ずつ縮めて、ジャギーを抑える
    private static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap current = src;
        bool owned = false;
        while (current.Width / 2 >= size * 2)
        {
            Bitmap half = Draw(current, current.Width / 2);
            if (owned) current.Dispose();
            current = half;
            owned = true;
        }
        Bitmap result = Draw(current, size);
        if (owned) current.Dispose();
        return result;
    }

    private static Bitmap Draw(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        using (var attr = new ImageAttributes())
        {
            attr.SetWrapMode(WrapMode.TileFlipXY);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
        }
        return dst;
    }

    private static byte[] Pixels(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buffer, y * bmp.Width * 4, bmp.Width * 4);
            }
            return buffer;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    // BITMAPINFOHEADER + 32bpp BGRA（下から上）+ AND マスク
    private static byte[] ToDib(Bitmap bmp)
    {
        int size = bmp.Width;
        byte[] px = Pixels(bmp);
        int maskStride = ((size + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(size); w.Write(size * 2);
            w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(size * size * 4 + maskStride * size);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = size - 1; y >= 0; y--) w.Write(px, y * size * 4, size * 4);
            for (int y = size - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < size; x++)
                {
                    if (px[(y * size + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
                w.Write(row);
            }
            return ms.ToArray();
        }
    }

    private static void WriteIco(string path, int[] sizes, List<byte[]> images)
    {
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) w.Write(image);
        }
    }
}
'@

$sizes = [int[]](16, 20, 24, 32, 40, 48, 64, 128, 256)
$src = (Resolve-Path $Source).ProviderPath
$out = [System.IO.Path]::GetFullPath($Output)
[IconMaker]::Make($src, $out, $sizes)
Write-Host "作成しました: $out ($((Get-Item $out).Length) バイト、サイズ: $($sizes -join ', '))"
