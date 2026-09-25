using System;

namespace KeepHistory.Tests.Framework;

/// <summary>テストメソッドの印。引数なし・戻り値 void の public メソッドに付ける。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestAttribute : Attribute
{
}
