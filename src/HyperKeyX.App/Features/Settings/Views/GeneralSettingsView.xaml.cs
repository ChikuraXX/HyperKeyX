using HyperKeyX.Features.Settings.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyperKeyX.Features.Settings.Views;

/// <summary>
/// 提供通用设置、本地存储、版本检查与官方仓库入口。
/// </summary>
public sealed partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.StartWithWindows)
            return;

        await ViewModel.SetStartWithWindowsAsync(toggle.IsOn);
        toggle.IsOn = ViewModel.StartWithWindows;
    }

    private void OnTrayIconToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.TrayIconEnabled)
            return;

        ViewModel.SetTrayIconEnabled(toggle.IsOn);
        toggle.IsOn = ViewModel.TrayIconEnabled;
    }

    private void OnStartMinimizedToTrayToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle || toggle.IsOn == ViewModel.StartMinimizedToTray)
            return;

        ViewModel.SetStartMinimizedToTray(toggle.IsOn);
        toggle.IsOn = ViewModel.StartMinimizedToTray;
    }

    private void OnRuntimeStatusNotificationsToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle ||
            toggle.IsOn == ViewModel.RuntimeStatusNotificationsEnabled)
        {
            return;
        }

        ViewModel.SetRuntimeStatusNotificationsEnabled(toggle.IsOn);
        toggle.IsOn = ViewModel.RuntimeStatusNotificationsEnabled;
    }

    private void OnSaveShortcuts(object sender, RoutedEventArgs e) => ViewModel.SaveShortcutSettings();

    private async void OnStorageExpanding(Expander sender, ExpanderExpandingEventArgs args) =>
        await ViewModel.EnsureStorageLoadedAsync();

    private async void OnRefreshStorage(object sender, RoutedEventArgs args) =>
        await ViewModel.RefreshStorageCommand.ExecuteAsync(null);

    private void OnOpenStorage(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: StorageAreaViewModel area })
            ViewModel.OpenStorageCommand.Execute(area);
    }
}
