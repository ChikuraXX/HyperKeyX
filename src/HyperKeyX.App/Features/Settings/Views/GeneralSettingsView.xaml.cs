using HyperKeyX.Features.Settings.ViewModels;
using HyperKeyX.Domain.Preferences;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

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
        Unloaded += (_, _) => ViewModel.IsShortcutCaptureActive = false;
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

    /// <summary>聚焦捕获框时暂停已注册的全局启停键，防止编辑时误启动。</summary>
    private void OnShortcutCaptureGotFocus(object sender, RoutedEventArgs args) =>
        ViewModel.IsShortcutCaptureActive = true;

    /// <summary>离开捕获框后恢复全局启停键。</summary>
    private void OnShortcutCaptureLostFocus(object sender, RoutedEventArgs args) =>
        ViewModel.IsShortcutCaptureActive = false;

    /// <summary>读取当前输入消息中的修饰键；不接受手工文本、粘贴或单独修饰键。</summary>
    private void OnShortcutCaptureKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not TextBox capture) return;
        var modifiers = new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift,
            VirtualKey.LeftWindows, VirtualKey.RightWindows }.Where(key =>
                (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0).ToArray();
        // Tab 与 Shift+Tab 保持常规焦点导航；带 Ctrl、Alt 或 Win 的组合继续捕获。
        if (args.Key == VirtualKey.Tab && modifiers.All(key => key == VirtualKey.Shift)) return;
        args.Handled = true;
        if (AppShortcut.TryCapture((int)args.Key, modifiers.Select(key => (int)key), out var shortcut))
            capture.Text = shortcut.ToString();
    }

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
