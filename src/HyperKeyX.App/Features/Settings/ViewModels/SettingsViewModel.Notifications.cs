namespace HyperKeyX.Features.Settings.ViewModels;

/// <summary>
/// 管理仅在本机显示的运行状态通知偏好，不包含任何联网消息能力。
/// </summary>
public sealed partial class SettingsViewModel
{
    private bool _runtimeStatusNotificationsEnabled;

    public bool RuntimeStatusNotificationsEnabled
    {
        get => _runtimeStatusNotificationsEnabled;
        private set => SetProperty(ref _runtimeStatusNotificationsEnabled, value);
    }

    public string RuntimeStatusNotificationsText =>
        Localize("运行状态通知", "Runtime status notifications");

    public string RuntimeStatusNotificationsDescriptionText => Localize(
        "通过快捷键启用、关闭或紧急停止自动化时显示 Windows 通知",
        "Show Windows notifications when a shortcut enables, disables, or emergency-stops automation");

    public void SetRuntimeStatusNotificationsEnabled(bool enabled)
    {
        if (_isDisposed || RuntimeStatusNotificationsEnabled == enabled)
            return;

        if (TrySave(_preferences with { RuntimeStatusNotificationsEnabled = enabled }))
            RuntimeStatusNotificationsEnabled = enabled;
    }

    private void InitializeNotificationPreferences() =>
        _runtimeStatusNotificationsEnabled = _preferences.RuntimeStatusNotificationsEnabled;

    private void RaiseNotificationLocalizationChanged()
    {
        OnPropertyChanged(nameof(RuntimeStatusNotificationsText));
        OnPropertyChanged(nameof(RuntimeStatusNotificationsDescriptionText));
    }
}
