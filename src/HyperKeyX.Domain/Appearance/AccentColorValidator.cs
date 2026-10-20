using System.Globalization;
using HyperKeyX.Domain.Preferences;

namespace HyperKeyX.Domain.Appearance;

/// <summary>
/// 校验应用强调色格式，并提供独立的主题对比度提示依据。
/// </summary>
public static class AccentColorValidator
{
    private const double MinimumContrastRatio = 3.0;
    private const uint LightBackground = 0xFFFFFFFF;
    private const uint DarkBackground = 0xFF202020;

    public static bool TryNormalize(
        string? value,
        AppThemeMode theme,
        out string normalized)
    {
        if (!TryNormalize(value, out normalized))
            return false;
        var argb = uint.Parse(normalized.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return theme switch
        {
            AppThemeMode.Light => HasRequiredContrast(argb, LightBackground),
            AppThemeMode.Dark => HasRequiredContrast(argb, DarkBackground),
            _ => HasRequiredContrast(argb, LightBackground) &&
                 HasRequiredContrast(argb, DarkBackground)
        };
    }

    /// <summary>仅校验并规范化 ARGB 格式，不以对比度阻止用户选择颜色。</summary>
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is not { Length: 9 } ||
            value[0] != '#' ||
            !uint.TryParse(
                value.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var argb))
        {
            return false;
        }

        normalized = $"#{argb:X8}";
        return true;
    }

    private static bool HasRequiredContrast(uint foreground, uint background)
    {
        var alpha = Channel(foreground, 24);

        // 半透明强调色先与实际主题背景合成，再按 WCAG 相对亮度计算。
        var red = Composite(Channel(foreground, 16), Channel(background, 16), alpha);
        var green = Composite(Channel(foreground, 8), Channel(background, 8), alpha);
        var blue = Composite(Channel(foreground, 0), Channel(background, 0), alpha);
        var foregroundLuminance = RelativeLuminance(red, green, blue);
        var backgroundLuminance = RelativeLuminance(
            Channel(background, 16),
            Channel(background, 8),
            Channel(background, 0));
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);

        return (lighter + 0.05) / (darker + 0.05) >= MinimumContrastRatio;
    }

    private static double Channel(uint color, int shift) =>
        ((color >> shift) & 0xFF) / 255.0;

    private static double Composite(double foreground, double background, double alpha) =>
        foreground * alpha + background * (1.0 - alpha);

    private static double RelativeLuminance(double red, double green, double blue) =>
        0.2126 * Linearize(red) +
        0.7152 * Linearize(green) +
        0.0722 * Linearize(blue);

    private static double Linearize(double channel) =>
        channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
