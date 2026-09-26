using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KeepHistory.Services;

namespace KeepHistory.Views;

/// <summary>
/// TextBlock に文字列を表示し、検索語に一致した部分を強調（太字＋背景色 HighlightBrush）する添付プロパティ。
/// Text に表示する文字列、Terms に正規化済みの検索語を渡す。
/// </summary>
public static class SearchHighlight
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchHighlight), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty TermsProperty = DependencyProperty.RegisterAttached(
        "Terms", typeof(IReadOnlyList<string>), typeof(SearchHighlight), new PropertyMetadata(null, OnChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    public static IReadOnlyList<string>? GetTerms(DependencyObject d) => (IReadOnlyList<string>?)d.GetValue(TermsProperty);

    public static void SetTerms(DependencyObject d, IReadOnlyList<string>? value) => d.SetValue(TermsProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        var text = GetText(block) ?? string.Empty;
        var terms = GetTerms(block) ?? Array.Empty<string>();

        block.Inlines.Clear();
        int position = 0;
        foreach (var range in TextNormalizer.FindMatches(text, terms))
        {
            if (range.Start > position) block.Inlines.Add(new Run(text.Substring(position, range.Start - position)));
            var match = new Run(text.Substring(range.Start, range.Length)) { FontWeight = FontWeights.Bold };
            match.SetResourceReference(TextElement.BackgroundProperty, "HighlightBrush");
            match.SetResourceReference(TextElement.ForegroundProperty, "HighlightTextBrush");
            block.Inlines.Add(match);
            position = range.Start + range.Length;
        }
        if (position < text.Length) block.Inlines.Add(new Run(text.Substring(position)));
    }
}
