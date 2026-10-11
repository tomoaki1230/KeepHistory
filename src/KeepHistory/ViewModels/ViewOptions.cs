using System.ComponentModel;
using KeepHistory.Models;

namespace KeepHistory.ViewModels;

/// <summary>
/// 表示の設定（日付でまとめる・削除済みのローカルファイルを表示しない・ネットワーク上のファイルを表示しない）を、設定と一覧の間でそろえる。
/// 表示メニューで切り替えたら設定へ（保存が必要）、設定画面の OK・初期状態に戻す・読み直しの後は一覧へ。
/// </summary>
public static class ViewOptions
{
    /// <summary>設定の値を一覧に入れる。</summary>
    public static void Apply(AppSettings settings, MainViewModel vm)
    {
        vm.GroupByDate = settings.GroupByDate;
        vm.HideMissingFiles = settings.HideMissingFiles;
        vm.HideNetworkFiles = settings.HideNetworkFiles;
    }

    /// <summary>
    /// 一覧の値が変わった（PropertyChanged）ときに、表示の設定なら設定に入れる。設定が変わったら true（保存が必要）。
    /// 表示の設定以外の変化や、設定と同じ値なら false。
    /// </summary>
    public static bool TryTake(MainViewModel vm, AppSettings settings, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GroupByDate) && settings.GroupByDate != vm.GroupByDate)
        {
            settings.GroupByDate = vm.GroupByDate;
            return true;
        }
        if (e.PropertyName == nameof(MainViewModel.HideMissingFiles) && settings.HideMissingFiles != vm.HideMissingFiles)
        {
            settings.HideMissingFiles = vm.HideMissingFiles;
            return true;
        }
        if (e.PropertyName == nameof(MainViewModel.HideNetworkFiles) && settings.HideNetworkFiles != vm.HideNetworkFiles)
        {
            settings.HideNetworkFiles = vm.HideNetworkFiles;
            return true;
        }
        return false;
    }
}
