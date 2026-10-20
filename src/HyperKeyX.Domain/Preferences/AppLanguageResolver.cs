using System.Globalization;

namespace HyperKeyX.Domain.Preferences;

/// <summary>
/// 统一解释持久化的语言模式，并为系统未支持的语言提供英文回退。
/// </summary>
public static class AppLanguageResolver
{
    public const int SimplifiedChineseIndex = 0;
    public const int EnglishIndex = 1;
    public const int SystemLanguageIndex = 2;

    public static int NormalizeIndex(int languageIndex)
    {
        return languageIndex is SimplifiedChineseIndex or EnglishIndex or SystemLanguageIndex
            ? languageIndex
            : SystemLanguageIndex;
    }

    public static bool ResolveEnglish(int languageIndex, CultureInfo? systemCulture = null)
    {
        return NormalizeIndex(languageIndex) switch
        {
            SimplifiedChineseIndex => false,
            EnglishIndex => true,
            _ => !IsChineseCulture(systemCulture ?? CultureInfo.CurrentUICulture)
        };
    }

    private static bool IsChineseCulture(CultureInfo culture)
    {
        // 当前仅提供中文和英文；除中文外的所有系统语言都确定性回退英文。
        return string.Equals(
            culture.TwoLetterISOLanguageName,
            "zh",
            StringComparison.OrdinalIgnoreCase);
    }
}
