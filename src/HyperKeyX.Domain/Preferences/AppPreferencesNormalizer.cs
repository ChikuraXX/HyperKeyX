using HyperKeyX.Domain.Appearance;

namespace HyperKeyX.Domain.Preferences;

public static class AppPreferencesNormalizer
{
    public static AppPreferences Normalize(AppPreferences? value)
    {
        value ??= new AppPreferences();
        var trayEnabled = value.TrayIconEnabled;
        var assetName = string.IsNullOrWhiteSpace(value.CustomBackgroundAssetName)
            ? null
            : Path.GetFileName(value.CustomBackgroundAssetName);

        return value with
        {
            Shortcuts = AppShortcutSettings.Normalize(value.Shortcuts),
            SchemaVersion = AppPreferences.CurrentSchemaVersion,
            Language = Enum.IsDefined(value.Language) ? value.Language : AppLanguageMode.System,
            Theme = Enum.IsDefined(value.Theme) ? value.Theme : AppThemeMode.System,
            StartupPage = Enum.IsDefined(value.StartupPage) ? value.StartupPage : StartupPageMode.Keyboard,
            LastVisitedPage = Enum.IsDefined(value.LastVisitedPage) ? value.LastVisitedPage : AppPageId.Keyboard,
            LastSettingsCategory = Enum.IsDefined(value.LastSettingsCategory)
                ? value.LastSettingsCategory
                : SettingsCategoryId.General,
            AccentMode = Enum.IsDefined(value.AccentMode) ? value.AccentMode : AccentMode.System,
            Backdrop = AppearanceOptionResolver.NormalizeBackdrop(value.Backdrop),
            Font = AppearanceOptionResolver.NormalizeFont(value.Font),
            FontFamily = string.IsNullOrWhiteSpace(value.FontFamily) ? null : value.FontFamily.Trim(),
            UiScale = Enum.IsDefined(value.UiScale) ? value.UiScale : UiScaleMode.System,
            CustomAccentColor = NormalizeColor(value.CustomAccentColor),
            CustomBackgroundAssetName = assetName,
            CustomBackgroundEnabled = value.CustomBackgroundEnabled && assetName is not null,
            BackgroundOverlayOpacity = AppearanceOptionResolver.NormalizeOverlayOpacity(
                value.BackgroundOverlayOpacity),
            RuntimeStatusNotificationsEnabled = value.RuntimeStatusNotificationsEnabled,
            StartMinimizedToTray = trayEnabled && value.StartMinimizedToTray
        };
    }

    private static string NormalizeColor(string? value)
    {
        if (value is { Length: 9 } && value[0] == '#' &&
            uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                null, out _))
        {
            return value.ToUpperInvariant();
        }

        return AppPreferences.DefaultCustomAccentColor;
    }
}
