using System.Collections.Generic;
using System.Windows;
using KeepHistory.Models;

namespace KeepHistory.Views;

/// <summary>
/// 初回起動時に、Windows の起動時に起動するか（起動するなら通知領域にも常駐する）を確かめる。
/// × や Esc で閉じたら起動しない（既定のボタンは「起動時に起動する」）。
/// </summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow(HotkeySetting hotkey)
    {
        InitializeComponent();
        BenefitList.ItemsSource = BenefitsFor(hotkey);
    }

    /// <summary>起動時に起動して常駐するメリット。</summary>
    public static IReadOnlyList<string> BenefitsFor(HotkeySetting hotkey) => new[]
    {
        $"サインインした直後から、ホットキー（{hotkey.ToDisplayString()}）でいつでも呼び出せます。",
        "開いたファイルを漏れなく記録できるので、履歴と「回数」が正確になります。",
        "画面を閉じても終了しないので、次に開くときに待たされません。",
    };

    /// <summary>「起動時に起動する」を選んだら true（常駐もする）。</summary>
    public bool StartWithWindows { get; private set; }

    private void OnStartWithWindowsClick(object sender, RoutedEventArgs e)
    {
        StartWithWindows = true;
        DialogResult = true;
    }

    private void OnDoNotStartClick(object sender, RoutedEventArgs e)
    {
        StartWithWindows = false;
        DialogResult = false;
    }
}
