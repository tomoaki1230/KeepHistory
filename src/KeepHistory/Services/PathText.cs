using System.IO;

namespace KeepHistory.Services;

public static class PathText
{
    /// <summary>小文字・ドット付きの拡張子を返す。無ければ空文字。</summary>
    public static string ExtensionOf(string path)
        => Path.GetExtension(path).ToLowerInvariant();
}
