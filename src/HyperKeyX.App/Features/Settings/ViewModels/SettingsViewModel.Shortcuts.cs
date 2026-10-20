using CommunityToolkit.Mvvm.ComponentModel;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel
{
    /// <summary>当前进程启动时的快捷键快照；编辑偏好不会改变正在运行的停止条件。</summary>
    public AppShortcutSettings ActiveShortcuts { get; } = new();
    [ObservableProperty] private string _toggleShortcutDraft = string.Empty;
    [ObservableProperty] private string _recordingShortcutDraft = string.Empty;
    [ObservableProperty] private string _emergencyShortcutDraft = string.Empty;
    /// <summary>仅在快捷键捕获框具有活动窗口焦点时暂停全局启停热键。</summary>
    [ObservableProperty] private bool _isShortcutCaptureActive;
    public string ShortcutSettingsTitle => Localize("快捷键", "Keyboard shortcuts");
    public string ShortcutSettingsDescription => Localize("保存后重启应用生效", "Restart the app after saving to apply");
    public string ToggleShortcutLabel => Localize("启动／停止运行", "Start / stop automation");
    public string RecordingShortcutLabel => Localize("完成录制", "Finish recording");
    public string EmergencyShortcutLabel => Localize("紧急停止", "Emergency stop");
    public string SaveShortcutsText => Localize("保存快捷键", "Save shortcuts");
    /// <summary>捕获框的键盘操作提示，同时提供给辅助技术。</summary>
    public string ShortcutCaptureHint => Localize("点击后按下快捷键组合", "Click, then press a shortcut combination");

    private void InitializeShortcutDraft()
    {
        ToggleShortcutDraft = _preferences.Shortcuts.ToggleRuntime.ToString();
        RecordingShortcutDraft = _preferences.Shortcuts.FinishRecording.ToString();
        EmergencyShortcutDraft = _preferences.Shortcuts.EmergencyStop.ToString();
    }

    /// <summary>整组校验后保存，非法或冲突组合不覆盖已保存的配置。</summary>
    public bool SaveShortcutSettings()
    {
        if (!AppShortcut.TryParse(ToggleShortcutDraft, out var toggle) ||
            !AppShortcut.TryParse(RecordingShortcutDraft, out var recording) ||
            !AppShortcut.TryParse(EmergencyShortcutDraft, out var emergency))
        {
            ShowError(Localize("快捷键格式无效，请使用 Ctrl+Shift+F11 等组合。", "Invalid shortcut. Use a combination such as Ctrl+Shift+F11."));
            return false;
        }
        var shortcuts = new AppShortcutSettings { ToggleRuntime = toggle, FinishRecording = recording, EmergencyStop = emergency };
        if (!shortcuts.IsValid)
        {
            ShowError(Localize("快捷键不能重复或使用保留组合；运行启停不能使用 F12。", "Shortcuts must be distinct and cannot use reserved combinations; F12 cannot start/stop automation."));
            return false;
        }
        if (!TrySave(_preferences with { Shortcuts = shortcuts })) return false;
        InitializeShortcutDraft();
        return true;
    }

    private void RaiseShortcutLocalizationChanged()
    {
        foreach (var name in new[] { nameof(ShortcutSettingsTitle), nameof(ShortcutSettingsDescription),
            nameof(ToggleShortcutLabel), nameof(RecordingShortcutLabel), nameof(EmergencyShortcutLabel), nameof(SaveShortcutsText),
            nameof(ShortcutCaptureHint) })
            OnPropertyChanged(name);
    }
}
