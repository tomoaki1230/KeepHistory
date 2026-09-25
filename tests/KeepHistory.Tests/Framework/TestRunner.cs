using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace KeepHistory.Tests.Framework;

/// <summary>
/// [Test] の付いたメソッドをすべて実行する。
/// 画面テストのため、呼び出し元（Main）は STA スレッドであること。
/// テストクラスは 1 テストごとに生成し、IDisposable なら後始末する。
/// </summary>
public static class TestRunner
{
    public static int Run(Assembly assembly, string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var filter = args.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        var tests = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Select(m => (Type: t, Method: m)))
            .Where(x => filter == null || $"{x.Type.Name}.{x.Method.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Method.Name, StringComparer.Ordinal)
            .ToList();

        int passed = 0, failed = 0;
        var total = Stopwatch.StartNew();
        foreach (var (type, method) in tests)
        {
            var name = $"{type.Name}.{method.Name}";
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(type);
                method.Invoke(instance, null);
                (instance as IDisposable)?.Dispose();
                instance = null;
                passed++;
                Console.WriteLine($"[成功] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Console.WriteLine($"[失敗] {name}");
                Console.WriteLine(inner is AssertionException ? "    " + inner.Message : Indent(inner.ToString()));
                try
                {
                    (instance as IDisposable)?.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Console.WriteLine(Indent("後始末でも例外: " + disposeEx.Message));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"テスト結果: 成功 {passed} / 失敗 {failed} / 合計 {tests.Count}（{total.Elapsed.TotalSeconds:0.0} 秒）");
        if (tests.Count == 0)
        {
            Console.WriteLine("[失敗] 実行対象のテストがありません");
            return 1;
        }
        return failed == 0 ? 0 : 1;
    }

    private static string Indent(string text)
        => string.Join(Environment.NewLine, text.Split('\n').Select(l => "    " + l.TrimEnd('\r')));
}
