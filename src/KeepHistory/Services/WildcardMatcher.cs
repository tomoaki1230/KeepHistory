using System;

namespace KeepHistory.Services;

/// <summary>* と ? だけのワイルドカード照合（大文字小文字を区別しない）。</summary>
public static class WildcardMatcher
{
    public static bool IsMatch(string pattern, string text)
    {
        int p = 0, t = 0;
        int starP = -1, starT = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharEquals(pattern[p], text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++;
                starT = t;
            }
            else if (starP >= 0)
            {
                // 直前の * にもう 1 文字吸わせてやり直す
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool CharEquals(char a, char b)
        => a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
