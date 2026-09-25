using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// %APPDATA%\KeepHistory\ の history.json / deleted.json / settings.json の読み書き。
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

    public List<HistoryRecord> LoadHistory() => Load<List<HistoryRecord>>(HistoryPath) ?? new();

    public void SaveHistory(List<HistoryRecord> records) => Save(HistoryPath, records);

    public List<DeletedRecord> LoadDeleted() => Load<List<DeletedRecord>>(DeletedPath) ?? new();

    public void SaveDeleted(List<DeletedRecord> records) => Save(DeletedPath, records);

    public AppSettings LoadSettings() => (Load<AppSettings>(SettingsPath) ?? new AppSettings()).Normalize();

    public void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    private static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
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
        catch (IOException ex)
        {
            ErrorLog.Write($"{Path.GetFileName(path)} を読み込めませんでした。", ex);
            return null;
        }
    }

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
