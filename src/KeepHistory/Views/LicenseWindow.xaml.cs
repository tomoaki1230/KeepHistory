using System;
using System.IO;
using System.Text;
using System.Windows;

namespace KeepHistory.Views;

/// <summary>ヘルプ ＞ サードパーティーのライセンス。Licenses/THIRD-PARTY-NOTICES.txt を表示する。</summary>
public partial class LicenseWindow : Window
{
    public const string NoticesUri = "pack://application:,,,/KeepHistory;component/Licenses/THIRD-PARTY-NOTICES.txt";

    public LicenseWindow()
    {
        InitializeComponent();
        NoticeText.Text = LoadNotices();
    }

    /// <summary>ライセンス表記の全文を読む。</summary>
    public static string LoadNotices()
    {
        var info = Application.GetResourceStream(new Uri(NoticesUri))
                   ?? throw new InvalidOperationException("ライセンス表記のリソースが見つかりません: " + NoticesUri);
        using var reader = new StreamReader(info.Stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
