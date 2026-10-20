using HyperKeyX.Domain.Automation;

namespace HyperKeyX.Features.Automation.ViewModels;

/// <summary>
/// 提供与 108 键虚拟键盘严格一致的可搜索按键目录。
/// </summary>
public sealed class KeyboardKeyCatalog
{
    // 分组中的键值必须与 KeyboardMatrixControl.xaml 的唯一 Tag 集合保持一致。
    private static readonly IReadOnlyDictionary<KeyboardKeyCategory, int[]> StorageKeys =
        new Dictionary<KeyboardKeyCategory, int[]>
        {
            [KeyboardKeyCategory.FunctionAndSystem] =
                [27, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
                 44, 145, 19, 173, 174, 175, 183],
            [KeyboardKeyCategory.Letters] =
                [65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77,
                 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90],
            [KeyboardKeyCategory.NumberRowAndPunctuation] =
                [48, 49, 50, 51, 52, 53, 54, 55, 56, 57,
                 186, 187, 188, 189, 190, 191, 192, 219, 220, 221, 222],
            [KeyboardKeyCategory.Modifiers] =
                [91, 92, 93, 160, 161, 162, 163, 164, 165],
            [KeyboardKeyCategory.EditingAndNavigation] =
                [8, 9, 13, 20, 32, 33, 34, 35, 36, 45, 46],
            [KeyboardKeyCategory.ArrowKeys] = [37, 38, 39, 40],
            [KeyboardKeyCategory.Numpad] =
                [96, 97, 98, 99, 100, 101, 102, 103, 104, 105,
                 106, 107, 109, 110, 111, 144, KeyboardKeyIdentity.NumpadEnterStorageKey]
        };

    private static readonly IReadOnlyDictionary<int, (string English, string Chinese)> Aliases =
        new Dictionary<int, (string English, string Chinese)>
        {
            [13] = ("enter return", "回车 确认"),
            [KeyboardKeyIdentity.NumpadEnterStorageKey] =
                ("numpad enter keypad return", "小键盘回车 数字键盘确认"),
            [173] = ("volume mute", "静音 音量"),
            [174] = ("volume down", "降低音量 音量减"),
            [175] = ("volume up", "提高音量 音量加"),
            [37] = ("left arrow", "左方向键"),
            [38] = ("up arrow", "上方向键"),
            [39] = ("right arrow", "右方向键"),
            [40] = ("down arrow", "下方向键")
        };

    private KeyboardKeyCatalog(IEnumerable<KeyboardKeyCatalogItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items.ToArray();
        if (Items.Select(item => item.Identity.StorageKey).Distinct().Count() != Items.Count)
            throw new InvalidOperationException("键盘按键目录包含重复键。");
    }

    public static KeyboardKeyCatalog Default { get; } = CreateDefault();

    public IReadOnlyList<KeyboardKeyCatalogItem> Items { get; }

    public IReadOnlyList<KeyboardKeyCatalogItem> Search(string? query, bool isEnglish)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return Items;

        return Items
            .Where(item =>
            {
                var aliases = isEnglish ? item.EnglishAliases : item.ChineseAliases;
                var searchable = string.Join(
                    ' ',
                    item.GetDisplayName(isEnglish),
                    GetCategoryName(item.Category, isEnglish),
                    item.Identity.StorageKey.ToString(),
                    aliases);
                return searchable.Contains(normalized, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
    }

    public static string GetCategoryName(KeyboardKeyCategory category, bool isEnglish) =>
        (category, isEnglish) switch
        {
            (KeyboardKeyCategory.FunctionAndSystem, false) => "功能键与系统键",
            (KeyboardKeyCategory.FunctionAndSystem, true) => "Function and system",
            (KeyboardKeyCategory.Letters, false) => "字母",
            (KeyboardKeyCategory.Letters, true) => "Letters",
            (KeyboardKeyCategory.NumberRowAndPunctuation, false) => "数字行与标点",
            (KeyboardKeyCategory.NumberRowAndPunctuation, true) => "Number row and punctuation",
            (KeyboardKeyCategory.Modifiers, false) => "修饰键",
            (KeyboardKeyCategory.Modifiers, true) => "Modifiers",
            (KeyboardKeyCategory.EditingAndNavigation, false) => "编辑与导航",
            (KeyboardKeyCategory.EditingAndNavigation, true) => "Editing and navigation",
            (KeyboardKeyCategory.ArrowKeys, false) => "方向键",
            (KeyboardKeyCategory.ArrowKeys, true) => "Arrow keys",
            (KeyboardKeyCategory.Numpad, false) => "小键盘",
            (KeyboardKeyCategory.Numpad, true) => "Numpad",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    private static KeyboardKeyCatalog CreateDefault()
    {
        var items = new List<KeyboardKeyCatalogItem>();
        foreach (var group in StorageKeys)
        {
            foreach (var storageKey in group.Value)
            {
                var identity = KeyboardKeyIdentity.FromStorageKey(storageKey);
                if (!identity.IsValid)
                    throw new InvalidOperationException($"无效键盘目录键：{storageKey}");

                var aliases = Aliases.TryGetValue(storageKey, out var value)
                    ? value
                    : (English: string.Empty, Chinese: string.Empty);
                items.Add(new KeyboardKeyCatalogItem(
                    identity,
                    group.Key,
                    aliases.English,
                    aliases.Chinese));
            }
        }

        return new KeyboardKeyCatalog(items);
    }
}
