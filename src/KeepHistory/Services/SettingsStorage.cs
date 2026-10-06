using KeepHistory.Models;

namespace KeepHistory.Services;

/// <summary>
/// settings.json の保存と削除。
/// 「すべて初期状態に戻す」で削除した後は、利用者が次に設定を確定する（設定画面の OK）まで自動では保存しない
/// （画面を隠す・閉じる・終了時の自動保存で作り直すと、削除した意味がなくなり、次回起動も初回扱いにならないため）。
/// 起動時に settings.json を一時的に読めなかったとき（ネットワークの切断など）も、読めるようになるまで自動では保存しない
/// （既定値で上書きすると、保存してあった設定が消えるため）。
/// </summary>
public sealed class SettingsStorage
{
    private readonly DataStore _data;

    public SettingsStorage(DataStore data) => _data = data;

    /// <summary>削除した後で、自動保存を止めているか。</summary>
    public bool IsSuspended { get; private set; }

    /// <summary>settings.json があるか（無ければ初回起動）。</summary>
    public bool Exists => _data.SettingsExists;

    /// <summary>settings.json を一時的に読めず、既定値で動いているか（読めるまで自動保存しない）。</summary>
    public bool LoadFailed { get; private set; }

    public AppSettings Load() => _data.LoadSettings();

    /// <summary>
    /// 設定を読む。結果は Loaded（読めた）・NotFound（初回起動。settings は既定値）・Unavailable（一時的に読めない。settings は既定値）。
    /// Unavailable なら LoadFailed になり、読めるか利用者が確定するまで自動保存しない。
    /// </summary>
    public SettingsLoadResult TryLoad(out AppSettings settings)
    {
        try
        {
            var loaded = _data.LoadSettingsIfExists();
            LoadFailed = false;
            settings = loaded ?? new AppSettings().Normalize();
            return loaded != null ? SettingsLoadResult.Loaded : SettingsLoadResult.NotFound;
        }
        catch (DataUnavailableException)
        {
            LoadFailed = true;
            settings = new AppSettings().Normalize();
            return SettingsLoadResult.Unavailable;
        }
    }

    /// <summary>
    /// 読めなかった settings.json を読み直す（バックグラウンドで呼んでよい）。まだ読めなければ DataUnavailableException。
    /// 無くなっていた（届くのにファイルが無い）なら null。読めた内容を反映したら MarkRecovered を呼ぶこと。
    /// </summary>
    public AppSettings? Reload() => _data.LoadSettingsIfExists();

    /// <summary>読み直した設定を反映した（UI スレッドで、反映と同時に呼ぶ）。自動保存を再開する。</summary>
    public void MarkRecovered() => LoadFailed = false;

    /// <summary>
    /// 自動保存（画面を隠す・閉じる・終了時など）。削除した後と、起動時に読めなかったときは保存しない。保存したら true。
    /// </summary>
    public bool SaveAuto(AppSettings settings)
    {
        if (IsSuspended || LoadFailed) return false;
        _data.SaveSettings(settings);
        return true;
    }

    /// <summary>利用者が設定を確定した（設定画面の OK・初回起動の確認）。自動保存も再開する。</summary>
    public void SaveConfirmed(AppSettings settings)
    {
        IsSuspended = false;
        LoadFailed = false;
        _data.SaveSettings(settings);
    }

    /// <summary>settings.json を削除し、自動保存を止める。</summary>
    public void DeleteAndSuspend()
    {
        _data.DeleteSettings();
        IsSuspended = true;
        LoadFailed = false;
    }
}

public enum SettingsLoadResult
{
    Loaded,
    NotFound,
    Unavailable,
}
