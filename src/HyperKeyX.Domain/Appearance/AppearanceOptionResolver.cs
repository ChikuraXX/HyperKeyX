using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Domain.Appearance;

/// <summary>
/// 将持久化的外观选项限制在应用明确支持的范围内。
/// </summary>
public static class AppearanceOptionResolver
{
    public const double DefaultOverlayOpacity = 0.82;
    public const double MinimumOverlayOpacity = 0.65;
    public const double MaximumOverlayOpacity = 1.0;

    /// <summary>优先使用已安装字体名称，兼容没有该字段的旧偏好。</summary>
    public static string FontFamilyName(AppPreferences preferences) =>
        string.IsNullOrWhiteSpace(preferences.FontFamily)
            ? FontFamilyName(preferences.Font)
            : preferences.FontFamily.Trim();

    public static string FontFamilyName(AppFontOption option) =>
        NormalizeFont(option) switch
        {
            AppFontOption.MicrosoftYaHeiUi => "Microsoft YaHei UI",
            _ => "Segoe UI Variable Text"
        };

    public static AppFontOption NormalizeFont(AppFontOption option) =>
        Enum.IsDefined(option) ? option : AppFontOption.System;

    public static BackdropMode NormalizeBackdrop(BackdropMode mode) =>
        Enum.IsDefined(mode) ? mode : BackdropMode.System;

    public static double NormalizeOverlayOpacity(double value) =>
        Math.Clamp(
            double.IsFinite(value) ? value : DefaultOverlayOpacity,
            MinimumOverlayOpacity,
            MaximumOverlayOpacity);
}
