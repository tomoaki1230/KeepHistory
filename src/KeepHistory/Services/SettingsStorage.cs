using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// settings.json の保存と削除。
/// 「すべて初期状態に戻す」で削除した後は、利用者が次に設定を確定する（設定画面の OK）まで自動では保存しない
/// （画面を隠す・閉じる・終了時の自動保存で作り直すと、削除した意味がなくなり、次回起動も初回扱いにならないため）。
/// </summary>
public sealed class SettingsStorage
{
    private readonly DataStore _data;

    public SettingsStorage(DataStore data) => _data = data;

    /// <summary>削除した後で、自動保存を止めているか。</summary>
    public bool IsSuspended { get; private set; }

    /// <summary>settings.json があるか（無ければ初回起動）。</summary>
    public bool Exists => _data.SettingsExists;

    public AppSettings Load() => _data.LoadSettings();

    /// <summary>自動保存（画面を隠す・閉じる・終了時など）。削除した後は保存しない。保存したら true。</summary>
    public bool SaveAuto(AppSettings settings)
    {
        if (IsSuspended) return false;
        _data.SaveSettings(settings);
        return true;
    }

    /// <summary>利用者が設定を確定した（設定画面の OK・初回起動の確認）。自動保存も再開する。</summary>
    public void SaveConfirmed(AppSettings settings)
    {
        IsSuspended = false;
        _data.SaveSettings(settings);
    }

    /// <summary>settings.json を削除し、自動保存を止める。</summary>
    public void DeleteAndSuspend()
    {
        _data.DeleteSettings();
        IsSuspended = true;
    }
}
