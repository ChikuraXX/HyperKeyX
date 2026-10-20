namespace HyperKeyX.Domain.Preferences;

/// <summary>
/// 不依赖应用包身份且可版本化的全局界面偏好。
/// </summary>
public sealed record AppPreferences
{
    public const int CurrentSchemaVersion = 4;
    public const string DefaultCustomAccentColor = "#FF0078D4";

    private AppLanguageMode _language = AppLanguageMode.System;
    private AppThemeMode _theme = AppThemeMode.System;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AppLanguageMode Language { get => _language; init => _language = value; }
    public AppThemeMode Theme { get => _theme; init => _theme = value; }
    public StartupPageMode StartupPage { get; init; } = StartupPageMode.Keyboard;
    public AppPageId LastVisitedPage { get; init; } = AppPageId.Keyboard;
    public SettingsCategoryId LastSettingsCategory { get; init; } = SettingsCategoryId.General;
    public bool StartWithWindows { get; init; }
    /// <summary>启动时检查 GitHub 稳定版；默认关闭以保持本地使用无需联网。</summary>
    public bool AutomaticUpdatesEnabled { get; init; }
    /// <summary>是否同时检查 test 分支发布的预览版；默认仅检查稳定版。</summary>
    public bool IncludePreviewUpdates { get; init; }
    public bool TrayIconEnabled { get; init; }
    public bool StartMinimizedToTray { get; init; }
    /// <summary>用户快捷键，保存后下次启动统一应用。</summary>
    public AppShortcutSettings Shortcuts { get; init; } = new();
    public bool RuntimeStatusNotificationsEnabled { get; init; } = true;
    public AccentMode AccentMode { get; init; } = AccentMode.System;
    public string CustomAccentColor { get; init; } = DefaultCustomAccentColor;
    public BackdropMode Backdrop { get; init; } = BackdropMode.System;
    public AppFontOption Font { get; init; } = AppFontOption.System;
    /// <summary>用户选择的已安装字体家族；空值沿用旧版 Font 预设。</summary>
    public string? FontFamily { get; init; }
    public UiScaleMode UiScale { get; init; } = UiScaleMode.System;
    public bool CustomBackgroundEnabled { get; init; }
    public string? CustomBackgroundAssetName { get; init; }
    public double BackgroundOverlayOpacity { get; init; } = 0.82;

}
