using System.Text.Json.Serialization;

namespace HyperKeyX.Domain.Preferences;

/// <summary>可持久化的组合键；修饰键位与 Win32 MOD_ALT/CONTROL/SHIFT/WIN 对齐。</summary>
public sealed record AppShortcut(int Key, int Modifiers)
{
    private static readonly IReadOnlyDictionary<string, int> NamedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 13, ["Tab"] = 9, ["Space"] = 32, ["Escape"] = 27,
        ["Home"] = 36, ["End"] = 35, ["PageUp"] = 33, ["PageDown"] = 34,
        ["Insert"] = 45, ["Delete"] = 46, ["Left"] = 37, ["Up"] = 38, ["Right"] = 39, ["Down"] = 40
    };
    [JsonIgnore]
    public bool IsValid => Modifiers is >= 0 and <= 15 &&
        (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 || NamedKeys.Values.Contains(Key));
    /// <summary>规范化 Ctrl+Shift+F11 等文本；不接受重复修饰键或多个主键。</summary>
    public static bool TryParse(string? text, out AppShortcut shortcut)
    {
        shortcut = new(0, 0);
        var parts = text?.Split('+', StringSplitOptions.TrimEntries);
        if (parts is null || parts.Length == 0 || parts.Any(string.IsNullOrWhiteSpace)) return false;
        var modifiers = 0;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToUpperInvariant() switch { "ALT" => 1, "CTRL" or "CONTROL" => 2, "SHIFT" => 4, "WIN" or "WINDOWS" => 8, _ => 0 };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var keyName = parts[^1].ToUpperInvariant();
        int key = keyName.Length == 1 && char.IsAsciiLetterOrDigit(keyName[0]) ? keyName[0] : 0;
        if (keyName.StartsWith('F') && int.TryParse(keyName.AsSpan(1), out var function) && function is >= 1 and <= 24) key = 0x6F + function;
        if (NamedKeys.TryGetValue(keyName, out var named)) key = named;
        shortcut = new(key, modifiers);
        return shortcut.IsValid;
    }
    /// <summary>按左右修饰键等价、修饰键组合精确匹配，避免多项快捷键同时触发。</summary>
    public bool Matches(int key, IEnumerable<int> pressedKeys)
    {
        if (key != Key) return false;
        var modifiers = 0;
        foreach (var pressed in pressedKeys)
            modifiers |= pressed switch { 0x12 or 0xA4 or 0xA5 => 1, 0x11 or 0xA2 or 0xA3 => 2, 0x10 or 0xA0 or 0xA1 => 4, 0x5B or 0x5C => 8, _ => 0 };
        return modifiers == Modifiers;
    }
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(Key is >= 0x70 and <= 0x87 ? $"F{Key - 0x6F}" :
            NamedKeys.FirstOrDefault(pair => pair.Value == Key).Key ?? ((char)Key).ToString());
        return string.Join("+", parts);
    }
}

/// <summary>启动时固定的三项快捷键快照，运行中修改偏好不替换停止条件。</summary>
public sealed record AppShortcutSettings
{
    public AppShortcut ToggleRuntime { get; init; } = new(0x7A, 6);
    public AppShortcut FinishRecording { get; init; } = new(0x79, 6);
    public AppShortcut EmergencyStop { get; init; } = new(0x7B, 6);
    [JsonIgnore]
    public bool IsValid => ToggleRuntime is { IsValid: true } && FinishRecording is { IsValid: true } &&
        EmergencyStop is { IsValid: true } && ToggleRuntime.Key != 0x7B &&
        ToggleRuntime != FinishRecording && ToggleRuntime != EmergencyStop && FinishRecording != EmergencyStop &&
        new[] { ToggleRuntime, FinishRecording, EmergencyStop }.All(key =>
            !(key.Key == 46 && (key.Modifiers & 3) == 3) &&
            !(key.Key == 9 && (key.Modifiers & 1) != 0) &&
            !(key.Key == 0x4C && (key.Modifiers & 8) != 0));
    public static AppShortcutSettings Normalize(AppShortcutSettings? value) => value is { IsValid: true } ? value : new();
}
