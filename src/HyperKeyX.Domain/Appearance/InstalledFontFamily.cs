namespace HyperKeyX.Domain.Appearance;

/// <summary>一个本机字体家族及其本地化别名、代表字符覆盖分类；不包含字体文件。</summary>
public sealed record InstalledFontFamily(
    string FamilyName, string DisplayName, IReadOnlyList<string> Aliases, IReadOnlyList<string> Languages)
{
    /// <summary>按语言标识和任意本地化名称筛选；空语言表示全部。</summary>
    public bool Matches(string? language, string? search) =>
        (string.IsNullOrEmpty(language) || Languages.Contains(language)) &&
        (string.IsNullOrWhiteSpace(search) || Aliases.Append(FamilyName).Append(DisplayName)
            .Any(name => name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));
}
