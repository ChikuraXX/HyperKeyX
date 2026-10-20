using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 表示按键选择器中的一个不可变按键条目。
/// </summary>
public sealed class KeyboardKeyCatalogItem
{
    public KeyboardKeyCatalogItem(
        KeyboardKeyIdentity identity,
        KeyboardKeyCategory category,
        string englishAliases,
        string chineseAliases)
    {
        if (!identity.IsValid)
            throw new ArgumentOutOfRangeException(nameof(identity));

        Identity = identity;
        Category = category;
        EnglishAliases = englishAliases ?? string.Empty;
        ChineseAliases = chineseAliases ?? string.Empty;
    }

    public KeyboardKeyIdentity Identity { get; }
    public KeyboardKeyCategory Category { get; }
    public string EnglishAliases { get; }
    public string ChineseAliases { get; }

    public string GetDisplayName(bool isEnglish) =>
        KeyboardKeyDisplayNameFormatter.GetDisplayName(Identity, isEnglish);
}
