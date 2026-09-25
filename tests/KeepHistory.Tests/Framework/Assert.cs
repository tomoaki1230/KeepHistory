using System;
using System.Collections.Generic;
using System.Linq;

namespace KeepHistory.Tests.Framework;

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message)
    {
    }
}

/// <summary>最小限のアサーション。</summary>
public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            Fail($"期待値: <{expected}> 実際: <{actual}>", message);
        }
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            Fail($"<{actual}> 以外を期待しました", message);
        }
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (!e.SequenceEqual(a))
        {
            Fail($"期待値: [{string.Join(", ", e)}] 実際: [{string.Join(", ", a)}]", message);
        }
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition) Fail("true を期待しました", message);
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition) Fail("false を期待しました", message);
    }

    public static void Null(object? value, string? message = null)
    {
        if (value != null) Fail($"null を期待しました 実際: <{value}>", message);
    }

    public static T NotNull<T>(T? value, string? message = null) where T : class
    {
        if (value == null) Fail("null 以外を期待しました", message);
        return value!;
    }

    public static void Same(object? expected, object? actual, string? message = null)
    {
        if (!ReferenceEquals(expected, actual)) Fail($"同じインスタンスを期待しました 期待値: <{expected}> 実際: <{actual}>", message);
    }

    public static void Contains(string expectedSubstring, string? actual, string? message = null)
    {
        if (actual == null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            Fail($"<{actual}> に <{expectedSubstring}> が含まれていません", message);
        }
    }

    public static TException Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            Fail($"{typeof(TException).Name} を期待しましたが {ex.GetType().Name} が発生しました: {ex.Message}", message);
        }
        Fail($"{typeof(TException).Name} を期待しましたが例外が発生しませんでした", message);
        return null!;
    }

    public static void Fail(string detail, string? message = null)
        => throw new AssertionException(message == null ? detail : $"{message} ({detail})");
}
