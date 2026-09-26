using System.Collections.Generic;
using System.Windows;
using KeepHistory.Models;

namespace KeepHistory.Views;

/// <summary>初回起動時に、通知領域に常駐するかを確かめる。× で閉じたら常駐しない（既定）。</summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow(HotkeySetting hotkey)
    {
        InitializeComponent();
        BenefitList.ItemsSource = BenefitsFor(hotkey);
    }

    /// <summary>常駐するメリット。</summary>
    public static IReadOnlyList<string> BenefitsFor(HotkeySetting hotkey) => new[]
    {
        $"ホットキー（{hotkey.ToDisplayString()}）で、どのアプリを使っていてもすぐに呼び出せます。",
        "画面を閉じても終了しないので、次に開くときに待たされません。",
        "ファイルを開くたびにその場で記録するので、「回数」が正確になります。",
    };

    /// <summary>常駐するを選んだら true。</summary>
    public bool StayResident { get; private set; }

    private void OnStayResidentClick(object sender, RoutedEventArgs e)
    {
        StayResident = true;
        DialogResult = true;
    }

    private void OnNotResidentClick(object sender, RoutedEventArgs e)
    {
        StayResident = false;
        DialogResult = false;
    }
}
