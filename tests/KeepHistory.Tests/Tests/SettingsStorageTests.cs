using System;
using System.IO;
using KeepHistory.Models;
using KeepHistory.Services;
using KeepHistory.Tests.Framework;

namespace KeepHistory.Tests.Tests;

public sealed class SettingsStorageTests : IDisposable
{
    private readonly TempDirectory _dir = new("settings-storage");
    private readonly DataStore _data;
    private readonly SettingsStorage _storage;

    public SettingsStorageTests()
    {
        _data = new DataStore(_dir.Path);
        _storage = new SettingsStorage(_data);
    }

    public void Dispose() => _dir.Dispose();

    [Test]
    public void DeleteAndSuspend_RemovesOnlySettings_AndStopsAutoSave()
    {
        _storage.SaveConfirmed(new AppSettings { StayResident = true });
        _data.SaveHistory(new() { new HistoryRecord { Path = @"C:\a.txt", LastUsed = "2026-09-26T10:00:00" } });
        _data.SaveDeleted(new());
        Assert.True(_storage.Exists);

        _storage.DeleteAndSuspend();
        Assert.False(File.Exists(_data.SettingsPath), "settings.json を削除する");
        Assert.False(_storage.Exists, "次回起動は初回扱い");
        Assert.True(File.Exists(_data.HistoryPath), "履歴は残す");
        Assert.True(File.Exists(_data.DeletedPath), "削除した履歴も残す");

        Assert.False(_storage.SaveAuto(new AppSettings()), "画面を隠す・閉じる・終了時の自動保存では作り直さない");
        Assert.False(File.Exists(_data.SettingsPath));
        Assert.True(_storage.IsSuspended);
    }

    [Test]
    public void SaveConfirmed_AfterDelete_ResumesSaving()
    {
        _storage.SaveConfirmed(new AppSettings());
        _storage.DeleteAndSuspend();

        _storage.SaveConfirmed(new AppSettings { RetentionDays = 90 });
        Assert.True(File.Exists(_data.SettingsPath), "設定画面で OK したら保存する");
        Assert.Equal(90, _storage.Load().RetentionDays);
        Assert.False(_storage.IsSuspended, "自動保存も再開する");
        Assert.True(_storage.SaveAuto(new AppSettings { RetentionDays = 180 }));
        Assert.Equal(180, _storage.Load().RetentionDays);
    }

    [Test]
    public void DeleteSettings_WhenNoFile_DoesNotFail()
    {
        _data.DeleteSettings();
        Assert.False(_storage.Exists);
    }
}
