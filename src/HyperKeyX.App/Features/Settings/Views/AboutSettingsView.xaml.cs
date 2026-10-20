using HyperKeyX.Features.Settings.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Settings.Views;

/// <summary>集中展示软件介绍、稳定版更新及项目支持链接。</summary>
public sealed partial class AboutSettingsView : UserControl
{
    /// <summary>复用全局设置状态，切换分类不重复发起网络查询。</summary>
    public AboutSettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    /// <summary>承载版本查询与本地化内容的设置状态。</summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>保存自动检查偏好；持久化失败时恢复原开关。</summary>
    private void OnAutomaticUpdatesToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.AutomaticUpdatesEnabled) return;
        ViewModel.SetAutomaticUpdatesEnabled(toggle.IsOn);
        toggle.IsOn = ViewModel.AutomaticUpdatesEnabled;
    }

    /// <summary>保存预览通道偏好，查询中或保存失败时恢复原值。</summary>
    private void OnPreviewUpdatesToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.IncludePreviewUpdates) return;
        ViewModel.SetIncludePreviewUpdates(toggle.IsOn);
        toggle.IsOn = ViewModel.IncludePreviewUpdates;
    }

    /// <summary>执行一次稳定版检查。</summary>
    private async void OnCheckForUpdates(object sender, RoutedEventArgs args) => await ViewModel.CheckForUpdatesAsync();
    /// <summary>在应用内下载并校验官方 MSI。</summary>
    private async void OnDownloadUpdate(object sender, RoutedEventArgs args) => await ViewModel.DownloadUpdateAsync();
    /// <summary>取消网络传输。</summary>
    private void OnCancelUpdateDownload(object sender, RoutedEventArgs args) => ViewModel.CancelUpdateDownload();
    /// <summary>进入安装确认，保留未保存编辑的现有退出流程。</summary>
    private async void OnInstallUpdate(object sender, RoutedEventArgs args) => await ViewModel.InstallDownloadedUpdateAsync();
    /// <summary>打开官方仓库。</summary>
    private void OnOpenRepository(object sender, RoutedEventArgs args) => ViewModel.OpenRepository();
    /// <summary>打开历史版本与更新说明。</summary>
    private void OnOpenReleases(object sender, RoutedEventArgs args) => ViewModel.OpenReleases();
    /// <summary>打开问题反馈和建议入口。</summary>
    private void OnOpenFeedback(object sender, RoutedEventArgs args) => ViewModel.OpenBugReport();
    /// <summary>打开功能建议草稿。</summary>
    private void OnOpenFeatureRequest(object sender, RoutedEventArgs args) => ViewModel.OpenFeatureRequest();
    /// <summary>打开当前语言的使用说明。</summary>
    private void OnOpenUsageGuide(object sender, RoutedEventArgs args) => ViewModel.OpenUsageGuide();
    /// <summary>打开项目许可声明。</summary>
    private void OnOpenLicense(object sender, RoutedEventArgs args) => ViewModel.OpenLicense();
}
