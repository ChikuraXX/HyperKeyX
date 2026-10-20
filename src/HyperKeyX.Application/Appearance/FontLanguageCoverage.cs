namespace HyperKeyX.Application.Appearance;

/// <summary>基于代表字符的字体分类，不把少量字形覆盖误称为完整语言支持。</summary>
public static class FontLanguageCoverage
{
    /// <summary>语言筛选项及预览文本。字体可以同时属于多个分类。</summary>
    public sealed record Group(string Id, string ChineseName, string EnglishName, string Sample);

    public static IReadOnlyList<Group> Groups { get; } = Array.AsReadOnly(new[]
    {
        new Group("zh", "中文", "Chinese", "中文汉字繁體"),
        new Group("ja", "日文", "Japanese", "日本語あいうえおアイウエオ"),
        new Group("ko", "韩文", "Korean", "한국어가나다라마"),
        new Group("latin", "拉丁字母", "Latin", "AaZzÉéÖö"),
        new Group("arabic", "阿拉伯文", "Arabic", "العربية"),
        new Group("cyrillic", "西里尔字母", "Cyrillic", "АБЯабя"),
        new Group("greek", "希腊文", "Greek", "ΑΩαω"),
        new Group("hebrew", "希伯来文", "Hebrew", "אבשת"),
        new Group("devanagari", "天城文", "Devanagari", "हिन्दी"),
        new Group("thai", "泰文", "Thai", "ภาษาไทย")
    });

    /// <summary>由调用方检查真实字体字形，不能使用系统字体回退结果。</summary>
    public static IReadOnlyList<string> Classify(Func<int, bool> hasCharacter)
    {
        var matches = Groups.Where(group => group.Sample.EnumerateRunes()
            .All(rune => hasCharacter(rune.Value))).Select(group => group.Id).ToArray();
        return matches.Length == 0 ? ["other"] : matches;
    }
}
