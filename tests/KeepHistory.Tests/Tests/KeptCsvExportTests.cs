using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;
using KeepHistory.ViewModels;
using KeepHistory.Views;

namespace KeepHistory.Tests.Tests;

/// <summary>キープの一覧を CSV に出力（1.2.1）。</summary>
public sealed class KeptCsvExportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0);
    private readonly TempDirectory _dir = new("kept-csv");
    private readonly HistoryStore _store = new();
    private MainWindow? _window;

    public KeptCsvExportTests()
    {
        _store.Register(@"C:\Docs\old-kept.xlsx", Now.AddDays(-30), Now);
        _store.Register(@"C:\Docs\not-kept.docx", Now.AddHours(-1), Now);
        _store.Register(@"\\server\share\new-kept.pdf", Now.AddHours(-2), Now);
        _store.Register(@"C:\Docs\gone-kept.txt", Now.AddDays(-3), Now);
        _store.Find(@"C:\Docs\old-kept.xlsx")!.IsKept = true;
        _store.Find(@"\\server\share\new-kept.pdf")!.IsKept = true;
        var gone = _store.Find(@"C:\Docs\gone-kept.txt")!;
        gone.IsKept = true;
        gone.IsMissing = true;
        _store.Find(@"C:\Docs\old-kept.xlsx")!.OpenCount = 5;
    }

    public void Dispose()
    {
        if (_window != null)
        {
            _window.AllowClose = true;
            _window.Close();
            UiTestHost.DoEvents();
        }
        _dir.Dispose();
    }

    private static string[] Lines(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Test]
    public void Csv_HasOnlyKeptEntries_NewestFirst_WithAllColumns()
    {
        var csv = KeptCsvExporter.BuildCsv(KeptCsvExporter.KeptEntries(_store.Entries));
        var lines = Lines(csv);
        Assert.Equal("\"ファイル名\",\"フォルダ\",\"フルパス\",\"前回利用日時\",\"回数\",\"状態\"", lines[0], "見出し");
        Assert.Equal(4, lines.Length, "見出し + キープした 3 件（キープしていないものは出さない）");
        Assert.True(lines[1].StartsWith("\"new-kept.pdf\",\"\\\\server\\share\",\"\\\\server\\share\\new-kept.pdf\""), "新しい順。ネットワーク上のものも出す: " + lines[1]);
        Assert.True(lines[2].StartsWith("\"gone-kept.txt\""), lines[2]);
        Assert.True(lines[2].EndsWith(",\"見つからない\""), "見つからないファイルは状態に書く（出さないことはしない）: " + lines[2]);
        Assert.Equal($"\"old-kept.xlsx\",\"C:\\Docs\",\"C:\\Docs\\old-kept.xlsx\",\"{DateFormat.ToDisplay(Now.AddDays(-30))}\",\"5\",\"\"", lines[3],
            "日時は一覧と同じ表示、回数、見つかるものは状態が空");
        Assert.True(csv.EndsWith("\r\n"), "行の終わりは CRLF（Excel 向け）");
    }

    [Test]
    public void Csv_EscapesQuotesCommasAndFormulaLikeValues()
    {
        var store = new HistoryStore();
        store.Register(@"C:\a,b\say ""hi"".txt", Now, Now);
        store.Register(@"C:\x\=SUM(A1).csv", Now.AddHours(-1), Now);
        store.Register(@"C:\x\-plan.txt", Now.AddHours(-2), Now);
        foreach (var e in store.Entries) e.IsKept = true;
        var lines = Lines(KeptCsvExporter.BuildCsv(KeptCsvExporter.KeptEntries(store.Entries)));
        Assert.True(lines[1].StartsWith("\"say \"\"hi\"\".txt\",\"C:\\a,b\""), "\" は 2 つ重ね、カンマを含む値も 1 つの欄: " + lines[1]);
        Assert.True(lines[2].StartsWith("\"'=SUM(A1).csv\""), "= で始まる値は ' を付けて数式にしない: " + lines[2]);
        Assert.True(lines[3].StartsWith("\"'-plan.txt\""), "- で始まる値も同じ: " + lines[3]);
        Assert.True(lines[2].Contains("\"C:\\x\\=SUM(A1).csv\""), "途中の = はそのまま");
    }

    [Test]
    public void Write_UsesUtf8WithBom_AndOverwrites_WithoutLeavingTempFile()
    {
        var path = Path.Combine(_dir.Path, "キープ.csv");
        File.WriteAllText(path, "古い内容");
        KeptCsvExporter.Write(path, KeptCsvExporter.KeptEntries(_store.Entries));
        var bytes = File.ReadAllBytes(path);
        Assert.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3), "BOM 付き（Excel で文字化けしない）");
        Assert.True(File.ReadAllText(path, Encoding.UTF8).StartsWith("\"ファイル名\""), "上書きする");
        Assert.False(File.Exists(path + ".tmp"), "一時ファイルを残さない");
        Assert.Equal("KeepHistory-キープ-20261013.csv", KeptCsvExporter.DefaultFileName(Now));
    }

    // ---- 画面 ----

    private MainWindow CreateWindow()
    {
        UiTestHost.EnsureApplication();
        var vm = new MainViewModel(_store, () => Now);
        _window = new MainWindow(vm) { ShowActivated = true, CurrentModifiers = () => ModifierKeys.None, Now = () => Now };
        _window.ShowAndActivate();
        UiTestHost.DoEvents();
        return _window;
    }

    [Test]
    public void Menu_HasExportItemInTools()
    {
        var window = CreateWindow();
        var items = window.ToolsMenu.Items.OfType<object>().ToList();
        Assert.Same(window.ExportKeptCsvMenuItem, items[0], "ツールのいちばん上");
        Assert.True(items[1] is Separator, "設定との間に区切り線");
        Assert.Equal("キープの一覧を CSV に出力(_E)...", window.ExportKeptCsvMenuItem.Header);
    }

    [Test]
    public void Export_SavesAllKeptEvenWhenFiltered_AndShowsResult()
    {
        var window = CreateWindow();
        window.ViewModel.SearchText = "not-kept";
        window.ViewModel.HideMissingFiles = true;
        string? offered = null;
        var path = Path.Combine(_dir.Path, "out.csv");
        window.ChooseCsvPath = name => { offered = name; return path; };

        window.ExportKeptCsv();

        Assert.Equal("KeepHistory-キープ-20261013.csv", offered, "既定のファイル名を渡す");
        Assert.Equal(4, Lines(File.ReadAllText(path, Encoding.UTF8)).Length, "絞り込み・検索にかかわらず、キープした 3 件をすべて出す");
        Assert.Equal($"キープ 3 件を CSV に保存しました: {path}", window.StatusMessageText.Text);
        Assert.Same(Application.Current.FindResource("SubTextBrush"), window.StatusMessageText.Foreground, "うまくいったときは赤字にしない");
    }

    [Test]
    public void Export_DoesNothingWhenCanceled_AndTellsWhenNoKept()
    {
        var window = CreateWindow();
        window.ChooseCsvPath = _ => null;
        window.ExportKeptCsv();
        Assert.Equal(0, Directory.GetFiles(_dir.Path).Length, "やめたら何も書かない");
        Assert.Equal(Visibility.Collapsed, window.StatusMessageText.Visibility);

        foreach (var e in _store.Entries) e.IsKept = false;
        var asked = false;
        window.ChooseCsvPath = _ => { asked = true; return null; };
        window.ExportKeptCsv();
        Assert.False(asked, "キープが無ければ保存先を聞かない");
        Assert.Contains("キープしたファイルはありません", window.StatusMessageText.Text);
    }

    [Test]
    public void Export_ShowsMessageWhenItCannotWrite()
    {
        var window = CreateWindow();
        string? shown = null;
        window.ShowMessage = m => shown = m;
        var path = Path.Combine(_dir.Path, "locked.csv");
        File.WriteAllText(path, "x");
        window.ChooseCsvPath = _ => path;
        var logDir = Path.Combine(_dir.Path, "log");
        Directory.CreateDirectory(logDir);
        var previous = ErrorLog.DirectoryOverride;
        ErrorLog.DirectoryOverride = logDir;
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                window.ExportKeptCsv(); // Excel で開いたままのときなど
            }
        }
        finally
        {
            ErrorLog.DirectoryOverride = previous;
        }
        Assert.NotNull(shown, "保存できなかったことを知らせる");
        Assert.Contains("CSV ファイルを保存できませんでした", shown!);
        Assert.Equal("x", File.ReadAllText(path), "元のファイルはそのまま");
        Assert.False(File.Exists(path + ".tmp"), "一時ファイルを残さない");
    }
}
