using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// 保存したファイルが一時的に読めない（ネットワークの切断、ほかのソフトがファイルをつかんでいるなど）。
/// 「ファイルが無い」とは区別する。読めないまま空の内容で保存すると、保存してあった内容が消えるため。
/// </summary>
public sealed class DataUnavailableException : IOException
{
    public DataUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// %APPDATA%\KeepHistory\ の history.json / deleted.json / settings.json の読み書き。
/// 読み込みでは「ファイルが無い（初回）」「壊れている（退避して空にする）」「一時的に読めない（DataUnavailableException）」を区別する。
/// </summary>
public sealed class DataStore
{
    public const string HistoryFileName = "history.json";
    public const string DeletedFileName = "deleted.json";
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public DataStore(string directory) => Directory = directory;

    public static string DefaultDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeepHistory");

    public string Directory { get; }

    public string HistoryPath => Path.Combine(Directory, HistoryFileName);
    public string DeletedPath => Path.Combine(Directory, DeletedFileName);
    public string SettingsPath => Path.Combine(Directory, SettingsFileName);

    /// <summary>ほかのソフトがつかんでいるときに読み直す回数と間隔（テストで差し替える）。</summary>
    internal static int ReadAttempts { get; set; } = 3;
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <exception cref="DataUnavailableException">一時的に読めない。</exception>
    public List<HistoryRecord> LoadHistory() => Load<List<HistoryRecord>>(HistoryPath) ?? new();

    public void SaveHistory(List<HistoryRecord> records) => Save(HistoryPath, records);

    /// <exception cref="DataUnavailableException">一時的に読めない。</exception>
    public List<DeletedRecord> LoadDeleted() => Load<List<DeletedRecord>>(DeletedPath) ?? new();

    public void SaveDeleted(List<DeletedRecord> records) => Save(DeletedPath, records);

    /// <summary>settings.json があるか（無ければ初回起動）。</summary>
    public bool SettingsExists => File.Exists(SettingsPath);

    /// <exception cref="DataUnavailableException">一時的に読めない。</exception>
    public AppSettings LoadSettings() => (Load<AppSettings>(SettingsPath) ?? new AppSettings()).Normalize();

    /// <summary>
    /// 設定を読む。settings.json が無ければ（初回起動）null。壊れていれば既定値。
    /// 保存場所に届かない（ネットワークの切断など）ときは、初回と取り違えないよう DataUnavailableException。
    /// </summary>
    public AppSettings? LoadSettingsIfExists()
    {
        var loaded = Load<AppSettings>(SettingsPath, out var found);
        return found ? (loaded ?? new AppSettings()).Normalize() : null;
    }

    public void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    /// <summary>history.json と deleted.json を削除する（書きかけの一時ファイルも）。</summary>
    public void DeleteHistory()
    {
        File.Delete(HistoryPath);
        File.Delete(HistoryPath + ".tmp");
        File.Delete(DeletedPath);
        File.Delete(DeletedPath + ".tmp");
    }

    /// <summary>settings.json を削除する（書きかけの一時ファイルも）。履歴のファイルには触れない。</summary>
    public void DeleteSettings()
    {
        File.Delete(SettingsPath);
        File.Delete(SettingsPath + ".tmp");
    }

    private T? Load<T>(string path) where T : class => Load<T>(path, out _);

    /// <param name="found">ファイルがあったか（壊れていても true）。</param>
    private T? Load<T>(string path, out bool found) where T : class
    {
        found = false;
        Exception? last = null;
        for (int attempt = 1; attempt <= Math.Max(1, ReadAttempts); attempt++)
        {
            if (!File.Exists(path))
            {
                // 保存場所に届くなら本当に無い（初回）。届かないなら（ネットワークの切断など）読めないだけ。
                // 届かない場所への確認は時間がかかるので読み直さない
                if (IsLocationReachable()) return null;
                throw Unavailable(path, null);
            }
            found = true;
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<T>(json, Options);
            }
            catch (JsonException ex)
            {
                // 壊れたファイルは退避して、空の状態で起動する
                ErrorLog.Write($"{Path.GetFileName(path)} を読み込めませんでした。退避して空の状態で起動します。", ex);
                TryBackupBroken(path);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ほかのソフトがつかんでいる、など。少し待って読み直す
                last = ex;
                if (attempt < ReadAttempts) System.Threading.Thread.Sleep(RetryDelay);
            }
        }
        throw Unavailable(path, last);
    }

    /// <summary>保存フォルダ（まだ無ければその親）に届くか。</summary>
    private bool IsLocationReachable()
    {
        if (System.IO.Directory.Exists(Directory)) return true;
        var parent = Path.GetDirectoryName(Directory);
        return parent != null && System.IO.Directory.Exists(parent);
    }

    // ログは呼び出し側で書く（読めない間は何度も読み直すので、ここで書くと同じ内容が並ぶ）
    private static DataUnavailableException Unavailable(string path, Exception? inner)
        => new($"{Path.GetFileName(path)} を一時的に読み込めません（{path}）。", inner);

    private void Save<T>(string path, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(value, Options);
        // 書き込み途中で落ちても元ファイルを壊さないよう、一時ファイルに書いてから置き換える
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryBackupBroken(string path)
    {
        try
        {
            File.Copy(path, path + ".broken", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
